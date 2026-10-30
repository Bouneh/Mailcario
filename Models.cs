using System.Security.Cryptography;
using System.Text;

namespace Mailcario;

public sealed class MailAccount
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string UserName { get; set; } = "";
    public string SmtpUserName { get; set; } = "";
    public string AuthMethod { get; set; } = "password";
    public string OAuthClientId { get; set; } = "";
    public string ImapHost { get; set; } = "imap.laposte.net";
    public int ImapPort { get; set; } = 993;
    public string SmtpHost { get; set; } = "smtp.laposte.net";
    public int SmtpPort { get; set; } = 587;
}

public sealed class MailItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public int Sequence { get; init; }
    public uint Uid { get; init; }
    public string From { get; init; } = "";
    public string FromAddress { get; init; } = "";
    public string To { get; init; } = "";
    public string Cc { get; init; } = "";
    public string Subject { get; init; } = "";
    public string MessageId { get; init; } = "";
    public DateTimeOffset? Date { get; init; }
    private bool _isRead;
    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead == value) return;
            _isRead = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsRead)));
        }
    }
    private bool _isFlagged;
    public bool IsFlagged
    {
        get => _isFlagged;
        set
        {
            if (_isFlagged == value) return;
            _isFlagged = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFlagged)));
        }
    }
    public string SenderName
    {
        get
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(From);
                return string.IsNullOrWhiteSpace(address.DisplayName) ? address.Address : address.DisplayName;
            }
            catch (FormatException) { return From; }
        }
    }
    public string AvatarLetter => SenderName.Length == 0 ? "?" : SenderName[..1].ToUpperInvariant();
    public string DateLabel => Date?.LocalDateTime.ToString("dd MMM HH:mm") ?? "";
    public string DayGroup
    {
        get
        {
            if (Date is null) return Localizer.T("olderMail");
            DateTime day = Date.Value.LocalDateTime.Date;
            if (day == DateTime.Today) return Localizer.T("today");
            if (day == DateTime.Today.AddDays(-1)) return Localizer.T("yesterday");
            return day.ToString("dddd d MMMM yyyy");
        }
    }
}

public sealed class MailFolder
{
    public string ServerName { get; init; } = "INBOX";
    public bool IsSent { get; set; }
    public char? Delimiter { get; init; } = '/';
    public bool CanSelect { get; init; } = true;
    public bool HasChildren { get; init; }
    public bool IsExpanded { get; init; } = true;
    public bool NotificationsEnabled { get; init; }
    public System.Windows.Visibility NotificationVisibility => NotificationsEnabled
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility ExpandVisibility => HasChildren ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
    public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE76C";
    public string ExpandHint => Localizer.T(IsExpanded ? "collapseFolder" : "expandFolder");
    private string[] Levels => Delimiter is char delimiter
        ? ImapFolderName.Decode(ServerName).Split(delimiter, StringSplitOptions.RemoveEmptyEntries)
        : [ImapFolderName.Decode(ServerName)];
    public int Depth => Math.Max(0, Levels.Length - 1);
    public System.Windows.Thickness Indent => new(Math.Min(Depth, 6) * 16, 0, 0, 0);
    public string DisplayName
    {
        get
        {
            if (ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return Localizer.T("inbox");
            if (IsSent) return Localizer.T("sentFolder");
            return Levels.LastOrDefault() ?? ImapFolderName.Decode(ServerName);
        }
    }
}

public sealed class MailContent
{
    public string PlainText { get; init; } = "";
    public string HtmlText { get; init; } = "";
    public UnsubscribeAction? Unsubscribe { get; init; }
    public string? CalendarText { get; init; }
    public IReadOnlyList<string> AttachmentNames { get; init; } = [];
    public IReadOnlyList<MailAttachment> Attachments { get; init; } = [];
    public IReadOnlyDictionary<string, InlineMailImage> InlineImages { get; init; } =
        new Dictionary<string, InlineMailImage>(StringComparer.OrdinalIgnoreCase);
}

public sealed record MailAttachment(string FileName, string ContentType, string? Charset, byte[] Bytes)
{
    public long Length => Bytes.LongLength;
    public string SizeLabel => Length < 1024 ? $"{Length} {(Localizer.Language == "fr" ? "o" : "B")}" : Length < 1024 * 1024
        ? $"{Length / 1024.0:0.#} {(Localizer.Language == "fr" ? "Ko" : "KB")}" :
        $"{Length / 1024.0 / 1024.0:0.#} {(Localizer.Language == "fr" ? "Mo" : "MB")}";
    public string Details => ContentType + " · " + SizeLabel;
    public bool CanPreview => AttachmentPreviewSupport.CanPreview(this);
    public bool CanPrint => CanPreview;
    public string OpenLabel => Localizer.T("openAttachment");
    public string SaveLabel => Localizer.T("saveAttachment");
    public string PrintLabel => Localizer.T("printAttachment");
}

public sealed record InlineMailImage(string ContentType, byte[] Bytes)
{
    public static string? NormalizeSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string source = value.Trim();
        if (source.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) return NormalizeId(source);
        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return "data:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        if (source.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(source, UriKind.Absolute, out _)) return null;
        if (source.Length > 255 || source.Any(char.IsControl)) return null;
        return "location:" + source.ToLowerInvariant();
    }

    public static string? NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string id = value.Trim().Trim('<', '>');
        if (id.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) id = id[4..];
        try { id = Uri.UnescapeDataString(id); }
        catch (UriFormatException) { return null; }
        if (id.Length is < 1 or > 255 || id.Any(char.IsControl)) return null;
        return "cid:" + id.ToLowerInvariant();
    }
}

public sealed record InboxSnapshot(uint UidValidity, MailItem? Latest, int UnreadCount);
public sealed record MailPage(IReadOnlyList<MailItem> Messages, int TotalCount, int NextSequence);
public sealed record MailSearchPage(IReadOnlyList<MailItem> Messages, int TotalCount, int NextIndex);

public sealed class CalendarInvite
{
    public string Uid { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Location { get; init; } = "";
    public string Organizer { get; init; } = "";
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public string RawCalendar { get; init; } = "";
    public string DateLabel => Start?.LocalDateTime.ToString("ddd dd MMM · HH:mm") ?? "";
}
