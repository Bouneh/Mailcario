using System.IO;
using System.Text.Json;

namespace Mailcario;

internal sealed class ContactHistoryCursor
{
    public uint UidValidity { get; set; }
    public uint HighestUid { get; set; }
}

internal sealed class ContactHistoryIndexStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public ContactHistoryIndexStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mailcario", "contact-history-index.json");

    public ContactHistoryCursor Get(string accountEmail, string folder)
    {
        lock (_gate) return Read().GetValueOrDefault(Key(accountEmail, folder)) ?? new ContactHistoryCursor();
    }

    public void Set(string accountEmail, string folder, ContactHistoryCursor cursor)
    {
        lock (_gate)
        {
            var all = Read();
            all[Key(accountEmail, folder)] = cursor;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
    }

    private Dictionary<string, ContactHistoryCursor> Read()
    {
        try { return File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, ContactHistoryCursor>>(File.ReadAllText(_path)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private static string Key(string accountEmail, string folder) =>
        accountEmail.ToLowerInvariant() + "\n" + folder.ToLowerInvariant();
}
