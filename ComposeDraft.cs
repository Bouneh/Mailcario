namespace Mailcario;

public sealed class ComposeDraft
{
    public string AccountEmail { get; init; } = "";
    public string To { get; init; } = "";
    public string Cc { get; init; } = "";
    public string Bcc { get; init; } = "";
    public string Subject { get; init; } = "";
    public byte[] DocumentPackage { get; init; } = [];
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public System.Net.Mail.MailPriority Priority { get; init; }
    public bool ReadReceipt { get; init; }
}
