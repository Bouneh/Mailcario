using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Mailcario;

internal static class WindowTheme
{
    private const int ImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Attach(Window window)
    {
        void Refresh(bool _) => Apply(window);
        window.SourceInitialized += (_, _) => Apply(window);
        ThemeManager.Changed += Refresh;
        window.Closed += (_, _) => ThemeManager.Changed -= Refresh;
    }

    private static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = ThemeManager.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref enabled, sizeof(int));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        int caption = ThemeManager.IsDark ? 0x00171717 : 0x00F9FAFB;
        int text = ThemeManager.IsDark ? 0x00F0F0F0 : 0x00202020;
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
    }
}
