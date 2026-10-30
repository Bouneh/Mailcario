using System.Text.RegularExpressions;

namespace Mailcario;

public sealed class ConversationGroupKey(string key, string title, int count, int unread) :
    System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; } = key;
    private int _unread = unread;
    public string DisplayName => title + "  ·  " + count + (_unread > 0 ? "  ● " + _unread : "");
    public void OnReadChanged(bool wasRead, bool isRead)
    {
        _unread = Math.Max(0, _unread + (isRead ? -1 : 1));
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayName)));
    }
    public override string ToString() => DisplayName;
    public override bool Equals(object? obj) => obj is ConversationGroupKey other && Key == other.Key;
    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);
}

public static class ConversationSupport
{
    private static readonly Regex ReplyPrefix = new(@"^(?:(?:re|fw|fwd|tr|sv)\s*:\s*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IdPattern = new(@"<[^<>\s]+>", RegexOptions.Compiled);

    public static string SubjectKey(string subject) => ReplyPrefix.Replace(subject.Trim(), "").Trim();

    public static void AssignGroups(IReadOnlyList<MailItem> items)
    {
        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Root(int index)
        {
            while (parent[index] != index) { parent[index] = parent[parent[index]]; index = parent[index]; }
            return index;
        }
        void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parent[b] = a; }
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            string id = IdPattern.Match(items[i].MessageId).Value;
            if (id.Length > 0) byId.TryAdd(id, i);
        }
        var bySubject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            foreach (Match match in IdPattern.Matches(items[i].InReplyTo + " " + items[i].References))
                if (byId.TryGetValue(match.Value, out int other)) Join(i, other);
            string normalized = SubjectKey(items[i].Subject);
            if (normalized.Length == 0) continue;
            string subject = items[i].AccountEmail.ToLowerInvariant() + "\0" + normalized;
            if (bySubject.TryGetValue(subject, out int otherIndex)) Join(i, otherIndex);
            else bySubject[subject] = i;
        }
        foreach (var group in Enumerable.Range(0, items.Count).GroupBy(Root))
        {
            MailItem latest = group.Select(index => items[index]).MaxBy(item => item.Date) ?? items[group.Key];
            string title = SubjectKey(latest.Subject);
            var key = new ConversationGroupKey(group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                title.Length > 0 ? title : latest.Subject, group.Count(),
                group.Count(index => !items[index].IsRead));
            foreach (int index in group) items[index].ConversationGroup = key;
        }
    }

    public static IReadOnlyList<MailItem> RelatedSent(IEnumerable<MailItem> received,
        IEnumerable<MailItem> sent)
    {
        var subjects = received.Select(item => item.AccountEmail.ToLowerInvariant() + "\0" + SubjectKey(item.Subject))
            .Where(value => value[^1] != '\0').ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = received.Select(item => item.AccountEmail.ToLowerInvariant() + "\0" +
                IdPattern.Match(item.MessageId).Value)
            .Where(value => value[^1] != '\0').ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sent.Where(item => subjects.Contains(item.AccountEmail.ToLowerInvariant() + "\0" +
                SubjectKey(item.Subject)) ||
            IdPattern.Matches(item.InReplyTo + " " + item.References).Any(match =>
                ids.Contains(item.AccountEmail.ToLowerInvariant() + "\0" + match.Value)))
            .ToList();
    }
}
