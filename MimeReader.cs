using System.Net;
using System.IO;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Mailcario;

public static class MimeReader
{
    private sealed class Collector
    {
        public string PlainText = "";
        public string HtmlText = "";
        public string? CalendarText;
        public List<string> Attachments = [];
        public Dictionary<string, InlineMailImage> InlineImages = new(StringComparer.OrdinalIgnoreCase);
    }

    static MimeReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Dictionary<string, string> ReadHeaders(byte[] raw)
    {
        string text = Encoding.Latin1.GetString(raw);
        int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end < 0) end = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (end >= 0) text = text[..end];
        text = Regex.Replace(text, @"\r?\n[ \t]+", " ");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in Regex.Split(text, @"\r?\n"))
        {
            int colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    public static MailContent ReadMessage(byte[] raw)
    {
        var collector = new Collector();
        ReadPart(Encoding.Latin1.GetString(raw), collector, 0);
        CollectDataImages(collector);
        string body = collector.PlainText;
        if (string.IsNullOrWhiteSpace(body)) body = HtmlToText(collector.HtmlText);
        return new MailContent
        {
            PlainText = body.Trim(),
            HtmlText = collector.HtmlText,
            CalendarText = collector.CalendarText,
            AttachmentNames = collector.Attachments,
            InlineImages = collector.InlineImages
        };
    }

    private static void ReadPart(string raw, Collector collector, int depth)
    {
        if (depth > 12) return;
        int separator = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int separatorSize = 4;
        if (separator < 0) { separator = raw.IndexOf("\n\n", StringComparison.Ordinal); separatorSize = 2; }
        if (separator < 0) return;
        var headers = ReadHeaders(Encoding.Latin1.GetBytes(raw[..separator]));
        string body = raw[(separator + separatorSize)..];
        string contentType = headers.GetValueOrDefault("content-type") ?? "text/plain";
        string disposition = headers.GetValueOrDefault("content-disposition") ?? "";
        string? filename = Parameter(disposition, "filename") ?? Parameter(contentType, "name");
        if (filename is not null) collector.Attachments.Add(DecodeHeader(filename));

        if (contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            string? boundary = Parameter(contentType, "boundary");
            if (string.IsNullOrEmpty(boundary)) return;
            string delimiter = "--" + boundary;
            foreach (string piece in body.Split(delimiter, StringSplitOptions.None).Skip(1))
            {
                if (piece.StartsWith("--")) break;
                string part = piece.TrimStart('\r', '\n');
                if (!string.IsNullOrWhiteSpace(part)) ReadPart(part, collector, depth + 1);
            }
            return;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            InlineMailImage.NormalizeId(headers.GetValueOrDefault("content-id")) is string contentId)
        {
            byte[] imageBytes = DecodeTransfer(body, headers.GetValueOrDefault("content-transfer-encoding") ?? "");
            if (imageBytes.Length is > 0 and <= 4_000_000)
            {
                collector.InlineImages.TryAdd(contentId,
                    new InlineMailImage(contentType.Split(';')[0].Trim(), imageBytes));
                if (InlineMailImage.NormalizeSource(headers.GetValueOrDefault("content-location")) is string location)
                    collector.InlineImages.TryAdd(location,
                        new InlineMailImage(contentType.Split(';')[0].Trim(), imageBytes));
            }
            return;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            InlineMailImage.NormalizeSource(headers.GetValueOrDefault("content-location")) is string imageLocation)
        {
            byte[] imageBytes = DecodeTransfer(body, headers.GetValueOrDefault("content-transfer-encoding") ?? "");
            if (imageBytes.Length is > 0 and <= 4_000_000)
                collector.InlineImages.TryAdd(imageLocation,
                    new InlineMailImage(contentType.Split(';')[0].Trim(), imageBytes));
            return;
        }

        if (filename is not null && !contentType.StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase) &&
            !filename.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)) return;
        byte[] decoded = DecodeTransfer(body, headers.GetValueOrDefault("content-transfer-encoding") ?? "");
        string charset = Parameter(contentType, "charset") ?? "utf-8";
        string text = DecodeBytes(decoded, charset);
        if (contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            if (collector.PlainText.Length == 0) collector.PlainText = text;
        }
        else if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            if (collector.HtmlText.Length == 0) collector.HtmlText = text;
        }
        else if (contentType.StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase) ||
                 filename?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) == true)
        {
            collector.CalendarText = text;
        }
    }

    private static void CollectDataImages(Collector collector)
    {
        if (collector.HtmlText.Length is 0 or > 2_000_000) return;
        var document = new HtmlDocument();
        document.LoadHtml(collector.HtmlText);
        foreach (HtmlNode image in document.DocumentNode.Descendants("img"))
        {
            string? source = image.GetAttributeValue("src", null);
            if (source is null || source.Length > 5_500_000 ||
                !source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) continue;
            int comma = source.IndexOf(',');
            if (comma < 0 || !source[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) continue;
            string type = source[5..source.IndexOf(';')];
            string? key = InlineMailImage.NormalizeSource(source);
            if (key is null) continue;
            try
            {
                byte[] bytes = Convert.FromBase64String(source[(comma + 1)..]);
                if (bytes.Length is > 0 and <= 4_000_000)
                    collector.InlineImages.TryAdd(key, new InlineMailImage(type, bytes));
            }
            catch (FormatException) { /* Malformed inline data remains blocked. */ }
        }
    }

    private static string? Parameter(string header, string name)
    {
        var match = Regex.Match(header, @"(?:^|;)\s*" + Regex.Escape(name) + @"\s*=\s*(?:""(?<quoted>[^""]*)""|(?<plain>[^;\s]+))", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value) : null;
    }

    private static byte[] DecodeTransfer(string body, string encoding)
    {
        if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            string encoded = Regex.Replace(body, @"\s", "");
            try { return Convert.FromBase64String(encoded); }
            catch (FormatException)
            {
                // IMAP notification previews are intentionally partial; decode complete base64 quads.
                int completeLength = encoded.Length / 4 * 4;
                if (completeLength < 4) return [];
                try { return Convert.FromBase64String(encoded[..completeLength]); }
                catch (FormatException) { return []; }
            }
        }
        if (!encoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
            return Encoding.Latin1.GetBytes(body);
        body = Regex.Replace(body, @"=\r?\n", "");
        using var output = new MemoryStream();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '=' && i + 2 < body.Length && byte.TryParse(body.Substring(i + 1, 2),
                System.Globalization.NumberStyles.HexNumber, null, out byte value))
            {
                output.WriteByte(value);
                i += 2;
            }
            else output.WriteByte((byte)body[i]);
        }
        return output.ToArray();
    }

    private static string DecodeBytes(byte[] bytes, string charset)
    {
        try { return Encoding.GetEncoding(charset).GetString(bytes); }
        catch (ArgumentException) { return Encoding.UTF8.GetString(bytes); }
    }

    public static string DecodeHeader(string value)
    {
        return Regex.Replace(value, @"=\?(?<charset>[^?]+)\?(?<mode>[bBqQ])\?(?<data>[^?]*)\?=", match =>
        {
            try
            {
                string payload = match.Groups["data"].Value;
                byte[] bytes = match.Groups["mode"].Value.Equals("b", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(payload)
                    : DecodeTransfer(payload.Replace('_', ' '), "quoted-printable");
                return DecodeBytes(bytes, match.Groups["charset"].Value);
            }
            catch { return match.Value; }
        });
    }

    public static string ExtractAddress(string value)
    {
        try { return new MailAddress(value).Address; }
        catch (FormatException)
        {
            var match = Regex.Match(value, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            return match.Success ? match.Value : "";
        }
    }

    private static string HtmlToText(string html)
    {
        html = Regex.Replace(html, @"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<\s*(br|/p|/div|/li)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(html);
    }
}
