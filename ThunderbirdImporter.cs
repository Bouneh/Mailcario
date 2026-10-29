using System.IO;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mailcario;

public sealed class ThunderbirdCandidate
{
    public bool IsSelected { get; set; } = true;
    public required MailAccount Account { get; init; }
    public required string Profile { get; init; }
    public string ServerSummary => $"{Account.ImapHost}:{Account.ImapPort}  ·  {Account.SmtpHost}:{Account.SmtpPort}";
    public string Note => Account.SmtpPort == 465
        ? Localizer.T("thunderbirdSmtpReview") : Account.AuthMethod == "microsoft-oauth"
            ? Localizer.T("thunderbirdMicrosoftNote") : "";
}

public static class ThunderbirdImporter
{
    private static readonly Regex PreferenceLine = new(
        "^\\s*user_pref\\(\"(?<key>[^\"]+)\",\\s*(?<value>\"(?:\\\\.|[^\"\\\\])*\"|-?\\d+|true|false)\\s*\\);\\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex AccountServer = new(@"^mail\.account\.(account\d+)\.server$", RegexOptions.Compiled);

    public static IReadOnlyList<ThunderbirdCandidate> ScanDefaultProfiles()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thunderbird");
        string ini = Path.Combine(root, "profiles.ini");
        if (!File.Exists(ini)) return [];
        var profiles = new List<ThunderbirdCandidate>();
        foreach (var section in ReadIni(ini))
        {
            if (!section.Key.StartsWith("Profile", StringComparison.OrdinalIgnoreCase)
                || !section.Value.TryGetValue("Path", out string? path) || string.IsNullOrWhiteSpace(path)) continue;
            bool relative = !section.Value.TryGetValue("IsRelative", out string? relativeValue) || relativeValue != "0";
            string profilePath = Path.GetFullPath(relative ? Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)) : path);
            if (relative && !profilePath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            profiles.AddRange(ReadProfile(Path.Combine(profilePath, "prefs.js"), section.Value.GetValueOrDefault("Name") ?? section.Key));
        }
        return profiles.DistinctBy(x => x.Account.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<ThunderbirdCandidate> ReadProfile(string prefsPath, string profileName)
    {
        var file = new FileInfo(prefsPath);
        if (!file.Exists) return [];
        if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("Thunderbird prefs.js is unexpectedly large.");
        string contents = File.ReadAllText(prefsPath);
        var prefs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in PreferenceLine.Matches(contents))
        {
            string literal = match.Groups["value"].Value;
            string? value = literal.StartsWith('"') ? TryDecodeString(literal) : literal;
            if (value is not null) prefs[match.Groups["key"].Value] = value;
        }
        string Get(string key) => prefs.GetValueOrDefault(key) ?? "";
        var results = new List<ThunderbirdCandidate>();
        foreach (string key in prefs.Keys)
        {
            Match accountMatch = AccountServer.Match(key);
            if (!accountMatch.Success) continue;
            string accountId = accountMatch.Groups[1].Value;
            string serverId = Get(key);
            string prefix = $"mail.server.{serverId}.";
            if (Get(prefix + "type") != "imap") continue;
            string host = Get(prefix + "hostname").Trim();
            string user = Get(prefix + "userName").Trim();
            int imapPort = ReadPort(Get(prefix + "port"), 993);
            int socketType = ReadInt(Get(prefix + "socketType"), imapPort == 993 ? 3 : 0);
            if (host.Length == 0 || socketType != 3) continue;

            string firstIdentity = Get($"mail.account.{accountId}.identities").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string identityPrefix = $"mail.identity.{firstIdentity}.";
            string email = Get(identityPrefix + "useremail").Trim();
            if (email.Length == 0 && user.Contains('@')) email = user;
            if (!MailAddress.TryCreate(email, out MailAddress? parsed) || parsed.Address != email) continue;
            string displayName = Get(identityPrefix + "fullName").Trim();

            string smtpId = Get(identityPrefix + "smtpServer");
            if (smtpId.Length == 0) smtpId = Get("mail.smtp.defaultserver");
            if (smtpId.Length == 0) smtpId = Get("mail.smtpservers").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string smtpPrefix = $"mail.smtpserver.{smtpId}.";
            string smtpHost = Get(smtpPrefix + "hostname").Trim();
            if (smtpHost.Length == 0) continue;
            int smtpPort = ReadPort(Get(smtpPrefix + "port"), 587);
            if (smtpPort == 465 && smtpHost.ToLowerInvariant() is "smtp.gmail.com" or "smtp.laposte.net" or "smtp.mail.me.com" or "smtp-mail.outlook.com")
                smtpPort = 587;
            string smtpUser = Get(smtpPrefix + "username").Trim();

            bool google = host.Equals("imap.gmail.com", StringComparison.OrdinalIgnoreCase);
            bool microsoft = host.Equals("outlook.office365.com", StringComparison.OrdinalIgnoreCase);
            var account = new MailAccount
            {
                DisplayName = displayName.Length > 0 ? displayName : email,
                Email = email, UserName = user.Length > 0 ? user : email,
                SmtpUserName = smtpUser.Length > 0 ? smtpUser : email,
                ImapHost = host, ImapPort = imapPort, SmtpHost = smtpHost, SmtpPort = smtpPort,
                AuthMethod = google && GoogleOAuthService.ConfiguredClientId.Length > 0 ? "google-oauth"
                    : microsoft ? "microsoft-oauth" : "password",
                OAuthClientId = google ? GoogleOAuthService.ConfiguredClientId
                    : microsoft ? MicrosoftOAuthService.ConfiguredClientId : ""
            };
            results.Add(new ThunderbirdCandidate { Account = account, Profile = profileName });
        }
        return results.DistinctBy(x => x.Account.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? TryDecodeString(string literal)
    {
        try { return JsonSerializer.Deserialize<string>(literal); }
        catch (JsonException) { return null; }
    }

    private static int ReadPort(string value, int fallback) => int.TryParse(value, out int port) && port is >= 1 and <= 65535 ? port : fallback;
    private static int ReadInt(string value, int fallback) => int.TryParse(value, out int number) ? number : fallback;

    private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
                sections[line[1..^1]] = current = new(StringComparer.OrdinalIgnoreCase);
            else if (current is not null && line.IndexOf('=') is int equals && equals > 0)
                current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return sections;
    }
}
