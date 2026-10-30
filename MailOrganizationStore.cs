using System.IO;
using System.Text.Json;

namespace Mailcario;

public sealed class MailOrganizationStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, MailOrganization> _items;

    public MailOrganizationStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailcario", "organization.json");
        try { _items = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, MailOrganization>>(
            File.ReadAllText(_path)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _items = []; }
    }

    public static string Key(MailItem mail)
    {
        if (mail.LocalId.Length > 0) return "local:" + mail.LocalId;
        string account = mail.AccountEmail.ToLowerInvariant();
        return mail.MessageId.Trim().Length > 0 ? account + ":id:" + mail.MessageId.Trim().ToLowerInvariant() :
            account + ":" + mail.FolderName + ":" + mail.Uid;
    }

    public MailOrganization Get(MailItem mail)
    {
        lock (_gate) return _items.TryGetValue(Key(mail), out var state) ? state : new();
    }

    public void SetCategory(MailItem mail, string name, string color) => Update(mail, state =>
    {
        state.CategoryName = name;
        state.CategoryColor = color;
    });

    public void SetSnooze(MailItem mail, DateTimeOffset? until) => Update(mail, state => state.SnoozedUntil = until);

    private void Update(MailItem mail, Action<MailOrganization> change)
    {
        lock (_gate)
        {
            string key = Key(mail);
            if (!_items.TryGetValue(key, out var state)) _items[key] = state = new();
            change(state);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, true);
        }
    }
}

public sealed class MailOrganization
{
    public string CategoryName { get; set; } = "";
    public string CategoryColor { get; set; } = "#4B9CD3";
    public DateTimeOffset? SnoozedUntil { get; set; }
}
