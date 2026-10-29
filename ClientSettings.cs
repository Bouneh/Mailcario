using System.IO;
using System.Text.Json;

namespace Mailcario;

public sealed class ClientSettings
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailcario", "client-settings.json");

    public bool NotifyNewMail { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool ShowMessagePreview { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 2;
    public bool ShowRibbon { get; set; } = true;
    public bool HomeCommands { get; set; } = true;
    public bool SyncCommands { get; set; } = true;
    public bool FolderCommands { get; set; } = true;
    public bool ViewCommands { get; set; } = true;
    public bool ShowReadingPane { get; set; } = true;
    public bool CompactMessages { get; set; }
    public string SortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true;
    public string ThemeMode { get; set; } = "system";
    public Dictionary<string, FolderViewState> FolderViews { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ClientSettings Load()
    {
        try
        {
            if (!File.Exists(PathName)) return new ClientSettings();
            var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(PathName)) ?? new ClientSettings();
            settings.CheckIntervalMinutes = Math.Clamp(settings.CheckIntervalMinutes, 1, 60);
            if (settings.SortBy is not ("date" or "sender" or "subject")) settings.SortBy = "date";
            if (settings.ThemeMode is not ("system" or "light" or "dark")) settings.ThemeMode = "system";
            settings.FolderViews ??= new(StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new ClientSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public ClientSettings Copy() => (ClientSettings)MemberwiseClone();
}

public sealed class FolderViewState
{
    public bool AccountExpanded { get; set; } = true;
    public List<string> HiddenFolders { get; set; } = [];
    public List<string> CollapsedFolders { get; set; } = [];
}
