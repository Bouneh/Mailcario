using System.IO;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace Mailcario;

public sealed class ScheduledMail
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountEmail { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Bcc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string PlainText { get; set; } = "";
    public string Html { get; set; } = "";
    public string? ReplyToId { get; set; }
    public MailPriority Priority { get; set; }
    public bool ReadReceipt { get; set; }
    public bool IsUndoSend { get; set; }
    public int UndoDelaySeconds { get; set; }
    public DateTimeOffset DueUtc { get; set; }
    public string State { get; set; } = "pending";
    public string Error { get; set; } = "";
    public List<string> AttachmentFiles { get; set; } = [];
    public List<string> AttachmentNames { get; set; } = [];
    public string Summary => DueUtc.LocalDateTime.ToString("dd/MM/yyyy HH:mm") + "  ·  " +
        Subject + "  ·  " + To + "  ·  " + Localizer.T(State switch
        {
            "pending" => "scheduledPending", "sending" => "scheduledSending",
            "sent" => "scheduledSent", "failed" => "scheduledFailed",
            _ => "scheduledInterrupted"
        });
}

public sealed class ScheduledMailStore
{
    private readonly string _root;
    private readonly string _catalog;
    private static readonly object Gate = new();

    public ScheduledMailStore(string? root = null)
    {
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailcario", "Scheduled");
        _catalog = Path.Combine(_root, "queue.json");
    }

    public void RecoverInterrupted()
    {
        // An interrupted SMTP transaction must never resend automatically.
        lock (Gate)
        {
            var all = Read();
            bool changed = false;
            foreach (var item in all.Where(item => item.State == "sending"))
            { item.State = "interrupted"; changed = true; }
            if (changed) Write(all);
        }
    }

    public IReadOnlyList<ScheduledMail> List() { lock (Gate) return Read().OrderBy(item => item.DueUtc).ToList(); }

    public void Enqueue(ScheduledMail mail, IReadOnlyList<string> attachments)
    {
        if (mail.DueUtc <= DateTimeOffset.UtcNow) throw new ArgumentException("Scheduled time must be in the future.");
        lock (Gate)
        {
            string directory = Path.Combine(_root, mail.Id);
            Directory.CreateDirectory(directory);
            try
            {
                foreach (string path in attachments)
                {
                    string destination = Path.Combine(directory, Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(path));
                    File.Copy(path, destination);
                    mail.AttachmentFiles.Add(destination);
                    mail.AttachmentNames.Add(Path.GetFileName(path));
                }
                if (mail.IsUndoSend && mail.UndoDelaySeconds > 0)
                    mail.DueUtc = DateTimeOffset.UtcNow.AddSeconds(mail.UndoDelaySeconds);
                var all = Read();
                all.Add(mail);
                Write(all);
            }
            catch { Directory.Delete(directory, true); throw; }
        }
    }

    public ScheduledMail? ClaimDue(DateTimeOffset now)
    {
        lock (Gate)
        {
            var all = Read();
            var item = all.FirstOrDefault(value => value.State == "pending" && value.DueUtc <= now);
            if (item is null) return null;
            item.State = "sending";
            Write(all);
            return item;
        }
    }

    public bool TryCancelPending(string id)
    {
        lock (Gate)
        {
            var all = Read();
            var item = all.FirstOrDefault(value => value.Id == id && value.State == "pending");
            if (item is null) return false;
            item.State = "cancelled";
            item.AttachmentFiles.Clear();
            item.AttachmentNames.Clear();
            Write(all);
            string directory = Path.Combine(_root, id);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return true;
        }
    }

    public void SetState(string id, string state, string error = "")
    {
        lock (Gate)
        {
            var all = Read();
            var item = all.FirstOrDefault(value => value.Id == id);
            if (item is null) return;
            item.State = state;
            item.Error = error;
            if (state is "sent" or "cancelled")
            {
                item.AttachmentFiles.Clear();
                item.AttachmentNames.Clear();
                Write(all);
                string directory = Path.Combine(_root, id);
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            else Write(all);
        }
    }

    public void Retry(string id)
    {
        lock (Gate)
        {
            var all = Read();
            var item = all.FirstOrDefault(value => value.Id == id && value.State is ("failed" or "interrupted"));
            if (item is null) return;
            item.State = "pending";
            item.Error = "";
            Write(all);
        }
    }

    public static MailMessage BuildMessage(MailAccount account, ScheduledMail job)
    {
        var mail = SmtpService.CreateMessage(account, job.To, job.Cc, job.Subject, job.PlainText, job.ReplyToId);
        foreach (string address in job.Bcc.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            mail.Bcc.Add(new MailAddress(address));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(job.Html, Encoding.UTF8, "text/html"));
        mail.Priority = job.Priority;
        if (job.ReadReceipt) mail.Headers.Add("Disposition-Notification-To", account.Email);
        for (int i = 0; i < job.AttachmentFiles.Count; i++)
        {
            string path = job.AttachmentFiles[i];
            mail.Attachments.Add(new Attachment(path)
            {
                Name = i < job.AttachmentNames.Count ? job.AttachmentNames[i] : Path.GetFileName(path),
                NameEncoding = Encoding.UTF8
            });
        }
        return mail;
    }

    private List<ScheduledMail> Read()
    {
        try { return File.Exists(_catalog) ? JsonSerializer.Deserialize<List<ScheduledMail>>(File.ReadAllText(_catalog)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { throw new InvalidDataException("The scheduled mail queue could not be read.", ex); }
    }

    private void Write(List<ScheduledMail> all)
    {
        Directory.CreateDirectory(_root);
        string temp = _catalog + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _catalog, true);
    }
}
