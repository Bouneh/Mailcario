using System.IO;
using System.Text;

namespace Mailcario;

public enum AttachmentPreviewKind { Unsupported, Text, Image, Pdf }

public static class AttachmentPreviewSupport
{
    static AttachmentPreviewSupport() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static AttachmentPreviewKind Kind(MailAttachment attachment)
    {
        string type = attachment.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        string extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
        if ((type == "application/pdf" || extension == ".pdf") &&
            attachment.Bytes.AsSpan().StartsWith("%PDF-"u8)) return AttachmentPreviewKind.Pdf;
        if (type is "image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/tiff" ||
            extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff")
            return AttachmentPreviewKind.Image;
        if (type.StartsWith("text/", StringComparison.Ordinal) ||
            type is "application/json" or "application/xml" or "application/ics" or "message/rfc822" ||
            extension is ".txt" or ".csv" or ".json" or ".xml" or ".ics" or ".eml" or ".md" or ".log")
            return AttachmentPreviewKind.Text;
        return AttachmentPreviewKind.Unsupported;
    }

    public static bool CanPreview(MailAttachment attachment) => Kind(attachment) != AttachmentPreviewKind.Unsupported;

    public static string ReadText(MailAttachment attachment, bool truncateForPreview = true)
    {
        const int limit = 1_000_000;
        int length = truncateForPreview ? Math.Min(attachment.Bytes.Length, limit) : attachment.Bytes.Length;
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(attachment.Charset ?? "utf-8"); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        using var stream = new MemoryStream(attachment.Bytes, 0, length, false);
        using var reader = new StreamReader(stream, encoding, true);
        string text = reader.ReadToEnd();
        return truncateForPreview && attachment.Bytes.Length > limit ? text + "\n\n…" : text;
    }
}
