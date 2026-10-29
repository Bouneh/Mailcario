using System.Windows;
using System.IO;
using System.Windows.Threading;

namespace Mailcario;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject.ToString() ?? "Unknown error");
    }

    private static void Log(string message)
    {
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "Mailcario-startup-error.log"), message); }
        catch { /* A crash log must not cause another crash. */ }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception.ToString());
        MessageBox.Show(e.Exception.Message, "Mailcario", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Localizer.SetLanguage(AccountStore.LoadLanguage());
        ThemeManager.Initialize(ClientSettings.Load().ThemeMode);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
