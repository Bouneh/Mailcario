using System.IO;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Mailcario;

internal static class OAuthSmtpSender
{
    public static async Task SendAsync(MailAccount account, string refreshToken, MailMessage message,
        CancellationToken cancellation = default)
    {
        string accessToken = account.AuthMethod switch
        {
            "microsoft-oauth" => await MicrosoftOAuthService.AccessTokenAsync(account, refreshToken, cancellation),
            "google-oauth" => await GoogleOAuthService.AccessTokenAsync(account, refreshToken, cancellation),
            _ => throw new InvalidOperationException("This account does not use OAuth.")
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(account.SmtpHost, account.SmtpPort, token);
        var protocol = new Protocol(tcp.GetStream());
        await protocol.ExpectAsync(220, token);
        await protocol.SendAsync("EHLO mailcario.local", token);
        string capabilities = await protocol.ExpectAsync(250, token);
        if (!capabilities.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The SMTP server does not offer STARTTLS.");
        await protocol.SendAsync("STARTTLS", token);
        await protocol.ExpectAsync(220, token);
        using var ssl = new SslStream(tcp.GetStream(), false);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = account.SmtpHost
        }, token);
        protocol = new Protocol(ssl);
        await protocol.SendAsync("EHLO mailcario.local", token);
        capabilities = await protocol.ExpectAsync(250, token);
        if (!capabilities.Contains("XOAUTH2", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The SMTP server does not offer OAuth2.");
        string user = string.IsNullOrWhiteSpace(account.SmtpUserName) ? account.UserName : account.SmtpUserName;
        string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "user=" + user + "\u0001auth=Bearer " + accessToken + "\u0001\u0001"));
        await protocol.SendAsync("AUTH XOAUTH2 " + auth, token);
        var reply = await protocol.ReadReplyAsync(token);
        if (reply.Code == 334)
        {
            await protocol.SendAsync("", token);
            reply = await protocol.ReadReplyAsync(token);
        }
        if (reply.Code != 235) throw new IOException("SMTP OAuth2 authentication failed: " + reply.Text);
        await protocol.SendAsync("MAIL FROM:<" + SafeAddress(message.From?.Address) + ">", token);
        await protocol.ExpectAsync(250, token);
        var recipients = message.To.Cast<MailAddress>().Concat(message.CC.Cast<MailAddress>())
            .Concat(message.Bcc.Cast<MailAddress>()).ToList();
        if (recipients.Count == 0) throw new FormatException("At least one recipient is required.");
        foreach (MailAddress recipient in recipients)
        {
            await protocol.SendAsync("RCPT TO:<" + SafeAddress(recipient.Address) + ">", token);
            var rcpt = await protocol.ReadReplyAsync(token);
            if (rcpt.Code is not (250 or 251)) throw new IOException("SMTP recipient rejected: " + rcpt.Text);
        }
        await protocol.SendAsync("DATA", token);
        await protocol.ExpectAsync(354, token);
        await protocol.SendDataAsync(BuildMime(message), token);
        await protocol.ExpectAsync(250, token);
        await protocol.SendAsync("QUIT", token);
    }

    private static string SafeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.IndexOfAny(['<', '>', '\r', '\n']) >= 0 ||
            address.Any(c => c > 127))
            throw new FormatException("Invalid mail address.");
        return address;
    }

    internal static string BuildMime(MailMessage message)
    {
        if (message.From is null) throw new FormatException("Missing sender.");
        var headers = new StringBuilder();
        headers.Append("From: ").Append(FormatAddress(message.From)).Append("\r\n");
        headers.Append("To: ").Append(string.Join(", ", message.To.Cast<MailAddress>().Select(FormatAddress))).Append("\r\n");
        if (message.CC.Count > 0)
            headers.Append("Cc: ").Append(string.Join(", ", message.CC.Cast<MailAddress>().Select(FormatAddress))).Append("\r\n");
        // RFC 5322 expects a numeric zone without the colon.
        string date = DateTimeOffset.Now.ToString("ddd, dd MMM yyyy HH:mm:ss zzz",
            System.Globalization.CultureInfo.InvariantCulture);
        headers.Append("Date: ").Append(date[..^3]).Append(date[^2..]).Append("\r\n");
        headers.Append("Subject: ").Append(EncodeHeader(message.Subject)).Append("\r\n");
        if (message.Priority != MailPriority.Normal)
        {
            headers.Append("Importance: ").Append(message.Priority == MailPriority.High ? "high" : "low").Append("\r\n");
            headers.Append("X-Priority: ").Append(message.Priority == MailPriority.High ? "1" : "5").Append("\r\n");
        }
        string? receipt = message.Headers["Disposition-Notification-To"];
        if (!string.IsNullOrWhiteSpace(receipt))
            headers.Append("Disposition-Notification-To: <").Append(SafeAddress(receipt)).Append(">\r\n");
        foreach (string name in new[] { "In-Reply-To", "References" })
        {
            string? value = message.Headers[name];
            if (!string.IsNullOrWhiteSpace(value) && value.IndexOfAny(['\r', '\n']) < 0)
                headers.Append(name).Append(": ").Append(value).Append("\r\n");
        }
        headers.Append("MIME-Version: 1.0\r\n");
        string? mixed = message.Attachments.Count > 0 ? "mailcario-mixed-" + Guid.NewGuid().ToString("N") : null;
        if (mixed is not null)
            headers.Append("Content-Type: multipart/mixed; boundary=\"").Append(mixed).Append("\"\r\n\r\n")
                .Append("--").Append(mixed).Append("\r\n");
        if (message.AlternateViews.Count == 0)
        {
            headers.Append("Content-Type: text/plain; charset=utf-8\r\n");
            headers.Append("Content-Transfer-Encoding: base64\r\n\r\n");
            AppendBase64(headers, Encoding.UTF8.GetBytes(message.Body ?? ""));
        }
        else
        {
            string boundary = "mailcario-" + Guid.NewGuid().ToString("N");
            headers.Append("Content-Type: multipart/alternative; boundary=\"").Append(boundary)
                .Append("\"\r\n\r\n");
            headers.Append("--").Append(boundary).Append("\r\n");
            headers.Append("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
            AppendBase64(headers, Encoding.UTF8.GetBytes(message.Body ?? ""));
            foreach (AlternateView view in message.AlternateViews)
            {
                string mediaType = view.ContentType.MediaType.ToLowerInvariant();
                if (mediaType is not ("text/calendar" or "text/html" or "text/plain"))
                    throw new NotSupportedException("Unsupported alternate MIME content type.");
                headers.Append("--").Append(boundary).Append("\r\nContent-Type: ").Append(mediaType)
                    .Append("; charset=utf-8");
                string? method = view.ContentType.Parameters["method"];
                if (mediaType == "text/calendar" && method is "REPLY" or "REQUEST" or "CANCEL")
                    headers.Append("; method=").Append(method);
                headers.Append("\r\nContent-Transfer-Encoding: base64\r\n\r\n");
                using var content = new MemoryStream();
                long position = view.ContentStream.CanSeek ? view.ContentStream.Position : 0;
                if (view.ContentStream.CanSeek) view.ContentStream.Position = 0;
                view.ContentStream.CopyTo(content);
                if (view.ContentStream.CanSeek) view.ContentStream.Position = position;
                AppendBase64(headers, content.ToArray());
            }
            headers.Append("--").Append(boundary).Append("--\r\n");
        }
        if (mixed is not null)
        {
            foreach (Attachment attachment in message.Attachments)
            {
                string filename = Path.GetFileName(attachment.Name) ?? "attachment";
                string parameter = Uri.EscapeDataString(filename);
                string ascii = new(filename.Select(c => c is >= ' ' and <= '~' && c != '"' && c != '\\' ? c : '_').ToArray());
                string mediaType = attachment.ContentType.MediaType;
                if (mediaType.IndexOfAny(['\r', '\n']) >= 0) mediaType = "application/octet-stream";
                headers.Append("--").Append(mixed).Append("\r\nContent-Type: ").Append(mediaType)
                    .Append("; name=\"").Append(ascii).Append("\"; name*=UTF-8''").Append(parameter)
                    .Append("\r\nContent-Disposition: attachment; filename=\"").Append(ascii)
                    .Append("\"; filename*=UTF-8''").Append(parameter)
                    .Append("\r\nContent-Transfer-Encoding: base64\r\n\r\n");
                using var content = new MemoryStream();
                long position = attachment.ContentStream.CanSeek ? attachment.ContentStream.Position : 0;
                if (attachment.ContentStream.CanSeek) attachment.ContentStream.Position = 0;
                attachment.ContentStream.CopyTo(content);
                if (attachment.ContentStream.CanSeek) attachment.ContentStream.Position = position;
                AppendBase64(headers, content.ToArray());
            }
            headers.Append("--").Append(mixed).Append("--\r\n");
        }
        return headers.ToString();
    }

    private static void AppendBase64(StringBuilder output, byte[] bytes)
    {
        string encoded = Convert.ToBase64String(bytes);
        for (int i = 0; i < encoded.Length; i += 76)
            output.Append(encoded.AsSpan(i, Math.Min(76, encoded.Length - i))).Append("\r\n");
    }

    private static string FormatAddress(MailAddress address) =>
        string.IsNullOrWhiteSpace(address.DisplayName) ? "<" + SafeAddress(address.Address) + ">" :
            EncodeHeader(address.DisplayName) + " <" + SafeAddress(address.Address) + ">";

    private static string EncodeHeader(string? text)
    {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ");
        var words = new List<string>();
        var current = new List<byte>();
        foreach (Rune rune in text.EnumerateRunes())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(rune.ToString());
            if (current.Count + bytes.Length > 45)
            {
                words.Add("=?UTF-8?B?" + Convert.ToBase64String(current.ToArray()) + "?=");
                current.Clear();
            }
            current.AddRange(bytes);
        }
        if (current.Count > 0) words.Add("=?UTF-8?B?" + Convert.ToBase64String(current.ToArray()) + "?=");
        return string.Join("\r\n ", words);
    }

    private sealed class Protocol(Stream stream)
    {
        private readonly Stream _stream = stream;

        public async Task SendAsync(string line, CancellationToken token)
        {
            await _stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), token);
            await _stream.FlushAsync(token);
        }

        public async Task SendDataAsync(string data, CancellationToken token)
        {
            await _stream.WriteAsync(Encoding.ASCII.GetBytes(data + ".\r\n"), token);
            await _stream.FlushAsync(token);
        }

        public async Task<string> ExpectAsync(int expected, CancellationToken token)
        {
            var reply = await ReadReplyAsync(token);
            if (reply.Code != expected) throw new IOException("SMTP: " + reply.Text);
            return reply.Text;
        }

        public async Task<(int Code, string Text)> ReadReplyAsync(CancellationToken token)
        {
            var lines = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                string line = await ReadLineAsync(token);
                lines.Add(line);
                if (line.Length < 4 || !int.TryParse(line[..3], out int code))
                    throw new IOException("Invalid SMTP response.");
                if (line[3] == ' ') return (code, string.Join("\n", lines));
                if (line[3] != '-') throw new IOException("Invalid SMTP response.");
            }
            throw new IOException("Oversized SMTP response.");
        }

        private async Task<string> ReadLineAsync(CancellationToken token)
        {
            using var buffer = new MemoryStream();
            byte[] one = new byte[1];
            while (buffer.Length < 8192)
            {
                if (await _stream.ReadAsync(one, token) == 0) throw new IOException("SMTP connection closed.");
                if (one[0] == '\n') return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
                buffer.WriteByte(one[0]);
            }
            throw new IOException("Oversized SMTP line.");
        }
    }
}
