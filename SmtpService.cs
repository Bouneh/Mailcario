using System.Net;
using System.Net.Mail;
using System.Text;

namespace Mailcario;

public static class SmtpService
{
    public sealed record SendAndSaveResult(bool CopySaved, string? CopyError);

    public static async Task<SendAndSaveResult> SendAndSaveAsync(MailAccount account, string password,
        MailMessage message)
    {
        string messageId = "<" + Guid.NewGuid().ToString("N") + "@mailcario.local>";
        message.Headers["Message-ID"] = messageId;
        // Prepare the copy before SMTP sends: attachments may use streams that are
        // no longer readable after the transport completes.
        byte[] copy = Encoding.UTF8.GetBytes(OAuthSmtpSender.BuildMime(message));
        await SendAsync(account, password, message);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var session = await ImapSession.ConnectAsync(account, password, timeout.Token);
            string sentFolder = await session.FindOrCreateSentFolderAsync(timeout.Token);
            if (await session.ContainsMessageIdAsync(sentFolder, messageId, timeout.Token))
                return new SendAndSaveResult(true, null);
            await Task.Delay(900, timeout.Token);
            if (await session.ContainsMessageIdAsync(sentFolder, messageId, timeout.Token))
                return new SendAndSaveResult(true, null);
            await session.AppendSentAsync(sentFolder, copy, timeout.Token);
            return new SendAndSaveResult(true, null);
        }
        catch (Exception ex)
        {
            return new SendAndSaveResult(false, ex.Message);
        }
    }

    public static async Task SendAsync(MailAccount account, string password, MailMessage message)
    {
        if (account.AuthMethod is "microsoft-oauth" or "google-oauth")
        {
            await OAuthSmtpSender.SendAsync(account, password, message);
            return;
        }
        using var client = new SmtpClient(account.SmtpHost, account.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(
                string.IsNullOrWhiteSpace(account.SmtpUserName) ? account.UserName : account.SmtpUserName, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000
        };
        await client.SendMailAsync(message);
    }

    public static MailMessage CreateMessage(MailAccount account, string to, string cc, string subject, string body, string? replyToId = null)
    {
        var message = new MailMessage
        {
            From = new MailAddress(account.Email, account.DisplayName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = body,
            BodyEncoding = Encoding.UTF8
        };
        foreach (string address in to.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(new MailAddress(address));
        foreach (string address in cc.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.CC.Add(new MailAddress(address));
        if (!string.IsNullOrWhiteSpace(replyToId))
        {
            message.Headers.Add("In-Reply-To", replyToId);
            message.Headers.Add("References", replyToId);
        }
        return message;
    }
}
