using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mailcario;

public static class ThemeManager
{
    private static readonly DispatcherTimer SystemThemeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static bool _initialized;
    private static bool _applied;

    public static string Mode { get; private set; } = "system";
    public static bool IsDark { get; private set; }
    public static event Action<bool>? Changed;

    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["Accent"] = "#438EC7", ["AccentDark"] = "#85C6F5", ["AccentSoft"] = "#2B3C4B",
        ["Ink"] = "#F0F0F0", ["Muted"] = "#AAAAAA", ["Stroke"] = "#424242",
        ["Canvas"] = "#242424", ["Sidebar"] = "#1D1D1D", ["Surface"] = "#292929",
        ["SurfaceAlt"] = "#303030", ["Hover"] = "#353535", ["Selected"] = "#414141",
        ["TitleBar"] = "#171717", ["Rail"] = "#171717", ["SearchField"] = "#303030",
        ["SearchStroke"] = "#494949", ["MessageGroup"] = "#363636", ["SelectedStroke"] = "#727272",
        ["StatusBar"] = "#1B1B1B", ["FileShade"] = "#AA080808", ["ReaderAvatar"] = "#155A91",
        ["PrivacyBg"] = "#323A43", ["PrivacyStroke"] = "#526477", ["InviteStroke"] = "#47617A",
        ["TrackingBg"] = "#3A3028", ["TrackingStroke"] = "#796547", ["TrackingText"] = "#F0C995",
        ["IconMail"] = "#78C9ED", ["IconReply"] = "#C49ADD", ["IconForward"] = "#86B7EE",
        ["IconNeutral"] = "#A4C8DC", ["IconSync"] = "#89C99D", ["IconFolder"] = "#D3B176",
        ["IconDanger"] = "#D78F8F", ["AccentHover"] = "#347FB5", ["ErrorText"] = "#EE8A8A"
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["Accent"] = "#2A75B6", ["AccentDark"] = "#135C95", ["AccentSoft"] = "#DCECF8",
        ["Ink"] = "#202A33", ["Muted"] = "#626F7A", ["Stroke"] = "#DCE2E8",
        ["Canvas"] = "#F7F8FA", ["Sidebar"] = "#F7F9FB", ["Surface"] = "#FFFFFF",
        ["SurfaceAlt"] = "#F1F4F7", ["Hover"] = "#E9F0F6", ["Selected"] = "#DCEBF7",
        ["TitleBar"] = "#F9FAFB", ["Rail"] = "#EFF2F5", ["SearchField"] = "#EDF1F5",
        ["SearchStroke"] = "#CFD8E0", ["MessageGroup"] = "#F0F3F6", ["SelectedStroke"] = "#A9C6DF",
        ["StatusBar"] = "#EEF2F5", ["FileShade"] = "#99000000", ["ReaderAvatar"] = "#176AAB",
        ["PrivacyBg"] = "#EDF5FD", ["PrivacyStroke"] = "#AFCFEA", ["InviteStroke"] = "#AFCFEA",
        ["TrackingBg"] = "#FFF3E1", ["TrackingStroke"] = "#E6C78A", ["TrackingText"] = "#795A23",
        ["IconMail"] = "#246FA9", ["IconReply"] = "#8756A5", ["IconForward"] = "#286DAF",
        ["IconNeutral"] = "#39719A", ["IconSync"] = "#278457", ["IconFolder"] = "#94712B",
        ["IconDanger"] = "#B94B4B", ["AccentHover"] = "#1D639E", ["ErrorText"] = "#B63C3C"
    };

    public static void Initialize(string mode)
    {
        if (!_initialized)
        {
            SystemThemeTimer.Tick += (_, _) => RefreshSystemTheme();
            _initialized = true;
        }
        SetMode(mode);
        SystemThemeTimer.Start();
    }

    public static void SetMode(string mode)
    {
        Mode = mode is "light" or "dark" or "system" ? mode : "system";
        Apply(Mode == "system" ? WindowsUsesDarkApps() : Mode == "dark");
    }

    public static void RefreshSystemTheme()
    {
        if (Mode == "system") Apply(WindowsUsesDarkApps());
    }

    public static void Shutdown() => SystemThemeTimer.Stop();

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        { return false; }
    }

    private static void Apply(bool dark)
    {
        if (_applied && IsDark == dark) return;
        IsDark = dark;
        _applied = true;
        var palette = dark ? Dark : Light;
        foreach (var (key, hex) in palette)
        {
            if (Application.Current?.Resources.Contains(key) == true)
                Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        Changed?.Invoke(dark);
    }
}
