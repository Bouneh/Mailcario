using System.Net.Security;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Mailcario;

public sealed class ImapSession : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private SslStream? _stream;
    private int _tag;

    private sealed record Part(string Line, byte[]? Literal);
    private sealed record Response(IReadOnlyList<Part> Parts);

    public static async Task<ImapSession> ConnectAsync(MailAccount account, string password, CancellationToken token = default)
    {
        string? accessToken = account.AuthMethod switch
        {
            "microsoft-oauth" => await MicrosoftOAuthService.AccessTokenAsync(account, password, token),
            "google-oauth" => await GoogleOAuthService.AccessTokenAsync(account, password, token),
            _ => null
        };
        return await OpenAsync(account, password, accessToken, token);
    }

    public static Task<ImapSession> ConnectWithOAuthTokenAsync(MailAccount account, string accessToken,
        CancellationToken token = default) => OpenAsync(account, "", accessToken, token);

    private static async Task<ImapSession> OpenAsync(MailAccount account, string password,
        string? accessToken, CancellationToken token)
    {
        var session = new ImapSession();
        try
        {
            await session._tcp.ConnectAsync(account.ImapHost, account.ImapPort, token);
            session._stream = new SslStream(session._tcp.GetStream(), false);
            await session._stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = account.ImapHost
            }, token);
            string greeting = await session.ReadLineAsync(token);
            if (!greeting.StartsWith("* OK", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The IMAP server refused the connection.");
            if (accessToken is null)
                await session.CommandAsync($"LOGIN {Quote(account.UserName)} {Quote(password)}", token);
            else
                await session.AuthenticateOAuthAsync(account.UserName, accessToken, token);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private async Task AuthenticateOAuthAsync(string userName, string accessToken, CancellationToken token)
    {
        string response = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "user=" + userName + "\u0001auth=Bearer " + accessToken + "\u0001\u0001"));
        string tag = "A" + Interlocked.Increment(ref _tag).ToString("D4");
        await _stream!.WriteAsync(Encoding.ASCII.GetBytes(tag + " AUTHENTICATE XOAUTH2 " + response + "\r\n"), token);
        await _stream.FlushAsync(token);
        while (true)
        {
            string line = await ReadLineAsync(token);
            if (line.StartsWith("+", StringComparison.Ordinal))
            {
                await _stream.WriteAsync("\r\n"u8.ToArray(), token);
                await _stream.FlushAsync(token);
                continue;
            }
            if (!line.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith(tag + " OK", StringComparison.OrdinalIgnoreCase)) return;
            throw new IOException("IMAP OAuth2 authentication failed: " + line[(tag.Length + 1)..]);
        }
    }

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken token = default)
    {
        var response = await CommandAsync("LIST \"\" \"*\"", token);
        var folders = new List<MailFolder> { new() { ServerName = "INBOX" } };
        foreach (var part in response.Parts)
        {
            string line = part.Line;
            if (!line.StartsWith("* LIST ", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(line, "\\* LIST \\(.*?\\) (?<delimiter>\\\"(?:[^\\\"]|\\\\.)*\\\"|NIL) (?<name>.*)$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            string name = match.Groups["name"].Value.Trim();
            if (name.StartsWith('"') && name.EndsWith('"')) name = name[1..^1].Replace("\\\"", "\"");
            string separator = match.Groups["delimiter"].Value;
            char? delimiter = separator.Equals("NIL", StringComparison.OrdinalIgnoreCase) || separator.Length < 3
                ? null : separator[1];
            if (!folders.Any(f => f.ServerName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                folders.Add(new MailFolder { ServerName = name, Delimiter = delimiter,
                    CanSelect = !line.Contains("\\Noselect", StringComparison.OrdinalIgnoreCase) });
        }
        return folders.Take(1).Concat(folders.Skip(1).OrderBy(f => ImapFolderName.Decode(f.ServerName),
            StringComparer.CurrentCultureIgnoreCase)).ToList();
    }

    public async Task<IReadOnlyList<MailItem>> LoadRecentAsync(string folder, int count = 30, CancellationToken token = default)
    {
        int total = await SelectAsync(folder, token);
        var items = new List<MailItem>();
        for (int sequence = total; sequence > Math.Max(0, total - count); sequence--)
        {
            token.ThrowIfCancellationRequested();
            var item = await FetchSummaryAsync($"FETCH {sequence}", token);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    public async Task<IReadOnlyList<MailItem>> LoadSinceUidAsync(string folder, uint lastUid,
        int count = 200, CancellationToken token = default)
    {
        if (lastUid == uint.MaxValue) return [];
        await SelectAsync(folder, token);
        var response = await CommandAsync($"UID SEARCH UID {lastUid + 1}:*", token);
        var uids = new List<uint>();
        foreach (var part in response.Parts)
        {
            if (!part.Line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string candidate in part.Line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(candidate, out uint uid) && uid > lastUid) uids.Add(uid);
        }
        var items = new List<MailItem>();
        foreach (uint uid in uids.Distinct().Order().Take(count))
        {
            token.ThrowIfCancellationRequested();
            var item = await FetchSummaryAsync($"UID FETCH {uid}", token);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    public async Task<InboxSnapshot> GetInboxSnapshotAsync(CancellationToken token = default)
    {
        var response = await CommandAsync("SELECT \"INBOX\"", token);
        uint validity = 0;
        int total = 0;
        foreach (var part in response.Parts)
        {
            var validityMatch = Regex.Match(part.Line, @"\[UIDVALIDITY (\d+)\]", RegexOptions.IgnoreCase);
            if (validityMatch.Success) uint.TryParse(validityMatch.Groups[1].Value, out validity);
            var existsMatch = Regex.Match(part.Line, @"^\* (\d+) EXISTS$", RegexOptions.IgnoreCase);
            if (existsMatch.Success) int.TryParse(existsMatch.Groups[1].Value, out total);
        }
        MailItem? latest = total > 0 ? await FetchSummaryAsync($"FETCH {total}", token) : null;
        return new InboxSnapshot(validity, latest);
    }

    public async Task SetReadAsync(string folder, uint uid, bool read, CancellationToken token = default)
    {
        await SelectAsync(folder, token);
        await CommandAsync($"UID STORE {uid} {(read ? "+" : "-")}FLAGS.SILENT (\\Seen)", token);
    }

    public async Task MoveAsync(string sourceFolder, uint uid, string targetFolder, CancellationToken token = default)
    {
        if (string.Equals(sourceFolder, targetFolder, StringComparison.OrdinalIgnoreCase)) return;
        await SelectAsync(sourceFolder, token);
        await CommandAsync($"UID MOVE {uid} {Quote(targetFolder)}", token);
    }

    public async Task SetFlaggedAsync(string folder, uint uid, bool flagged, CancellationToken token = default)
    {
        await SelectAsync(folder, token);
        await CommandAsync($"UID STORE {uid} {(flagged ? "+" : "-")}FLAGS.SILENT (\\Flagged)", token);
    }

    public async Task MarkAllReadAsync(string folder, CancellationToken token = default)
    {
        int count = await SelectAsync(folder, token);
        if (count > 0) await CommandAsync("STORE 1:* +FLAGS.SILENT (\\Seen)", token);
    }

    public async Task CreateFolderAsync(string name, CancellationToken token = default) =>
        await CommandAsync("CREATE " + Quote(name), token);

    public async Task RenameFolderAsync(string oldName, string newName, CancellationToken token = default) =>
        await CommandAsync($"RENAME {Quote(oldName)} {Quote(newName)}", token);

    public async Task<IReadOnlyList<MailItem>> SearchAsync(string folder, string query, int count = 100, CancellationToken token = default)
    {
        if (query.Length is < 1 or > 200) throw new ArgumentException("Search text must be between 1 and 200 characters.", nameof(query));
        await SelectAsync(folder, token);
        Response search;
        if (query.All(c => c <= 0x7F))
            search = await CommandAsync("UID SEARCH TEXT " + Quote(query), token);
        else
            search = await SearchUtf8Async(query, token);
        var uids = new List<uint>();
        foreach (var part in search.Parts)
        {
            if (!part.Line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string candidate in part.Line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(candidate, out uint uid)) uids.Add(uid);
        }
        var items = new List<MailItem>();
        foreach (uint uid in uids.TakeLast(count).Reverse())
        {
            token.ThrowIfCancellationRequested();
            var item = await FetchSummaryAsync($"UID FETCH {uid}", token);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    private async Task<Response> SearchUtf8Async(string query, CancellationToken token)
    {
        string tag = "A" + Interlocked.Increment(ref _tag).ToString("D4");
        byte[] queryBytes = Encoding.UTF8.GetBytes(query);
        byte[] prefix = Encoding.ASCII.GetBytes($"{tag} UID SEARCH CHARSET UTF-8 TEXT {{{queryBytes.Length}}}\r\n");
        await _stream!.WriteAsync(prefix, token);
        await _stream.FlushAsync(token);
        string continuation = await ReadLineAsync(token);
        if (!continuation.StartsWith('+')) throw new IOException("The IMAP server rejected UTF-8 search.");
        await _stream.WriteAsync(queryBytes, token);
        await _stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), token);
        await _stream.FlushAsync(token);
        return await ReadTaggedResponseAsync(tag, token);
    }

    private async Task<MailItem?> FetchSummaryAsync(string prefix, CancellationToken token)
    {
        var response = await CommandAsync($"{prefix} (UID FLAGS BODY.PEEK[HEADER.FIELDS (FROM TO CC SUBJECT DATE MESSAGE-ID)])", token, 128_000);
        byte[]? literal = response.Parts.FirstOrDefault(p => p.Literal is not null)?.Literal;
        if (literal is null) return null;
        var headers = MimeReader.ReadHeaders(literal);
        string meta = string.Join(" ", response.Parts.Select(p => p.Line));
        var uidMatch = Regex.Match(meta, @"\bUID (\d+)", RegexOptions.IgnoreCase);
        if (!uidMatch.Success || !uint.TryParse(uidMatch.Groups[1].Value, out uint uid)) return null;
        var seqMatch = Regex.Match(meta, @"\* (\d+) FETCH", RegexOptions.IgnoreCase);
        DateTimeOffset? date = null;
        if (DateTimeOffset.TryParse(headers.GetValueOrDefault("date"), out var parsedDate)) date = parsedDate;
        string from = MimeReader.DecodeHeader(headers.GetValueOrDefault("from") ?? "");
        return new MailItem
        {
            Sequence = seqMatch.Success && int.TryParse(seqMatch.Groups[1].Value, out int sequence) ? sequence : 0,
            Uid = uid,
            From = from,
            FromAddress = MimeReader.ExtractAddress(from),
            To = MimeReader.DecodeHeader(headers.GetValueOrDefault("to") ?? ""),
            Cc = MimeReader.DecodeHeader(headers.GetValueOrDefault("cc") ?? ""),
            Subject = MimeReader.DecodeHeader(headers.GetValueOrDefault("subject") ?? "(No subject)"),
            MessageId = headers.GetValueOrDefault("message-id") ?? "",
            Date = date,
            IsRead = Regex.IsMatch(meta, @"\\Seen\b", RegexOptions.IgnoreCase),
            IsFlagged = Regex.IsMatch(meta, @"\\Flagged\b", RegexOptions.IgnoreCase)
        };
    }

    public async Task<MailContent> LoadMessageAsync(string folder, uint uid, CancellationToken token = default)
    {
        await SelectAsync(folder, token);
        var response = await CommandAsync($"UID FETCH {uid} (BODY.PEEK[])", token, 20_000_000);
        byte[]? literal = response.Parts.FirstOrDefault(p => p.Literal is not null)?.Literal;
        if (literal is null) throw new IOException("The message is no longer available.");
        return MimeReader.ReadMessage(literal);
    }

    public async Task<IReadOnlyList<CalendarInvite>> LoadInvitesAsync(string folder, IEnumerable<uint> uids, CancellationToken token = default)
    {
        await SelectAsync(folder, token);
        var invites = new List<CalendarInvite>();
        foreach (uint uid in uids)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var response = await CommandAsync($"UID FETCH {uid} (BODY.PEEK[])", token, 20_000_000);
                byte[]? literal = response.Parts.FirstOrDefault(p => p.Literal is not null)?.Literal;
                if (literal is null) continue;
                var invite = CalendarSupport.Parse(MimeReader.ReadMessage(literal).CalendarText);
                if (invite is not null) invites.Add(invite);
            }
            catch (IOException) { /* A missing or oversized message does not hide other events. */ }
        }
        return invites.OrderBy(i => i.Start).ToList();
    }

    private async Task<int> SelectAsync(string folder, CancellationToken token)
    {
        var response = await CommandAsync($"SELECT {Quote(folder)}", token);
        foreach (var part in response.Parts)
        {
            var match = Regex.Match(part.Line, @"^\* (\d+) EXISTS$", RegexOptions.IgnoreCase);
            if (match.Success) return int.Parse(match.Groups[1].Value);
        }
        return 0;
    }

    private async Task<Response> CommandAsync(string command, CancellationToken token, int maxLiteral = 1_000_000)
    {
        string tag = "A" + Interlocked.Increment(ref _tag).ToString("D4");
        byte[] bytes = Encoding.UTF8.GetBytes(tag + " " + command + "\r\n");
        await _stream!.WriteAsync(bytes, token);
        await _stream.FlushAsync(token);
        return await ReadTaggedResponseAsync(tag, token, maxLiteral);
    }

    private async Task<Response> ReadTaggedResponseAsync(string tag, CancellationToken token, int maxLiteral = 1_000_000)
    {
        var parts = new List<Part>();
        while (true)
        {
            string line = await ReadLineAsync(token);
            byte[]? literal = null;
            var literalMatch = Regex.Match(line, @"\{(\d+)\}$");
            if (literalMatch.Success)
            {
                int length = int.Parse(literalMatch.Groups[1].Value);
                if (length > maxLiteral) throw new IOException("The message exceeds the current size limit.");
                literal = await ReadExactAsync(length, token);
            }
            parts.Add(new Part(line, literal));
            if (!line.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase)) continue;
            if (!line.StartsWith(tag + " OK", StringComparison.OrdinalIgnoreCase))
                throw new IOException("IMAP: " + line[(tag.Length + 1)..]);
            return new Response(parts);
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken token)
    {
        using var buffer = new MemoryStream();
        byte[] one = new byte[1];
        while (buffer.Length < 1_000_000)
        {
            int read = await _stream!.ReadAsync(one, token);
            if (read == 0) throw new IOException("The IMAP connection closed unexpectedly.");
            if (one[0] == '\n')
            {
                byte[] line = buffer.ToArray();
                if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
                return Encoding.UTF8.GetString(line);
            }
            buffer.WriteByte(one[0]);
        }
        throw new IOException("The IMAP server sent an oversized response line.");
    }

    private async Task<byte[]> ReadExactAsync(int length, CancellationToken token)
    {
        byte[] bytes = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await _stream!.ReadAsync(bytes.AsMemory(offset, length - offset), token);
            if (read == 0) throw new IOException("The IMAP connection closed during a message.");
            offset += read;
        }
        return bytes;
    }

    private static string Quote(string input) => "\"" + input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "") + "\"";

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _tcp.Dispose();
    }
}
