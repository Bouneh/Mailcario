using System.IO;
using System.Text.Json;

namespace Mailcario;

public sealed class MailRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountEmail { get; set; } = "";
    public string SenderContains { get; set; } = "";
    public string SubjectContains { get; set; } = "";
    public string Action { get; set; } = "mark-read";
    public string TargetFolder { get; set; } = "";
    public uint MinimumUid { get; set; }
    public uint LastProcessedUid { get; set; }
    public uint UidValidity { get; set; }
    public bool Enabled { get; set; } = true;
    public string Description => (Enabled ? "" : "⏸ ") +
        (SenderContains.Length > 0 ? SenderContains : SubjectContains) + " → " +
        (Action == "move" ? TargetFolder : Localizer.T(Action switch
        {
            "mark-unread" => "markUnread",
            "flag" => "followUp",
            _ => "markRead"
        }));

    public bool Matches(MailItem item) => item.Uid > MinimumUid &&
        (SenderContains.Length == 0 || item.From.Contains(SenderContains, StringComparison.OrdinalIgnoreCase)) &&
        (SubjectContains.Length == 0 || item.Subject.Contains(SubjectContains, StringComparison.OrdinalIgnoreCase));
}

public static class MailRuleStore
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailcario", "mail-rules.json");

    public static List<MailRule> Load()
    {
        try
        {
            return File.Exists(PathName)
                ? JsonSerializer.Deserialize<List<MailRule>>(File.ReadAllText(PathName)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return []; }
    }

    public static void Save(IReadOnlyList<MailRule> rules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathName, true);
    }

    public static async Task<IReadOnlyList<MailItem>> ApplyAsync(ImapSession session,
        string accountEmail, string folder, IReadOnlyList<MailItem> messages, CancellationToken token)
    {
        if (!folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return messages;
        var allRules = Load();
        var rules = allRules.Where(rule => rule.Enabled &&
            rule.AccountEmail.Equals(accountEmail, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rules.Count == 0) return messages;
        bool changed = false;
        foreach (MailRule rule in rules)
            if (rule.LastProcessedUid < rule.MinimumUid)
            {
                rule.LastProcessedUid = rule.MinimumUid;
                changed = true;
            }
        InboxSnapshot snapshot = await session.GetInboxSnapshotAsync(token);
        foreach (MailRule rule in rules)
        {
            if (rule.UidValidity == 0)
            {
                rule.UidValidity = snapshot.UidValidity;
                changed = true;
            }
            else if (snapshot.UidValidity != 0 && rule.UidValidity != snapshot.UidValidity)
            {
                rule.UidValidity = snapshot.UidValidity;
                rule.MinimumUid = rule.LastProcessedUid = snapshot.Latest?.Uid ?? 0;
                changed = true;
            }
        }
        if (changed) Save(allRules);
        uint firstCursor = rules.Min(rule => rule.LastProcessedUid);
        IReadOnlyList<MailItem> pending = await session.LoadSinceUidAsync(folder, firstCursor, token: token);
        if (pending.Count == 0) return messages;
        var visibleByUid = messages.ToDictionary(item => item.Uid);
        var movedUids = new HashSet<uint>();
        foreach (MailItem message in pending)
        {
            foreach (MailRule rule in rules)
            {
                token.ThrowIfCancellationRequested();
                if (message.Uid <= rule.LastProcessedUid || !rule.Matches(message)) continue;
                if (rule.Action == "mark-read" && !message.IsRead)
                {
                    await session.SetReadAsync(folder, message.Uid, true, token);
                    message.IsRead = true;
                    if (visibleByUid.TryGetValue(message.Uid, out var shown)) shown.IsRead = true;
                }
                else if (rule.Action == "mark-unread" && message.IsRead)
                {
                    await session.SetReadAsync(folder, message.Uid, false, token);
                    message.IsRead = false;
                    if (visibleByUid.TryGetValue(message.Uid, out var shown)) shown.IsRead = false;
                }
                else if (rule.Action == "flag" && !message.IsFlagged)
                {
                    await session.SetFlaggedAsync(folder, message.Uid, true, token);
                    message.IsFlagged = true;
                    if (visibleByUid.TryGetValue(message.Uid, out var shown)) shown.IsFlagged = true;
                }
                else if (rule.Action == "move" && rule.TargetFolder.Length > 0 &&
                    !rule.TargetFolder.Equals(folder, StringComparison.OrdinalIgnoreCase))
                {
                    await session.MoveAsync(folder, message.Uid, rule.TargetFolder, token);
                    movedUids.Add(message.Uid);
                    break;
                }
            }
        }
        uint lastUid = pending.Max(item => item.Uid);
        foreach (MailRule rule in rules) rule.LastProcessedUid = Math.Max(rule.LastProcessedUid, lastUid);
        Save(allRules);
        return messages.Where(item => !movedUids.Contains(item.Uid)).ToList();
    }
}
