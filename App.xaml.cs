using System.Windows;
using System.IO;
using System.Windows.Threading;

namespace Mailcario;

public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private bool _activationPending;

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
        base.OnStartup(e);
        _singleInstance = new SingleInstanceCoordinator("Mailcario", () =>
        {
            try
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    Dispatcher.BeginInvoke(new Action(ActivateExistingWindow));
            }
            catch (InvalidOperationException) { /* The application is closing. */ }
        });
        if (!_singleInstance.IsPrimary)
        {
            Shutdown();
            return;
        }
        Localizer.SetLanguage(AccountStore.LoadLanguage());
        ThemeManager.Initialize(ClientSettings.Load().ThemeMode);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (_activationPending) ActivateExistingWindow();
    }

    private void ActivateExistingWindow()
    {
        if (MainWindow is Mailcario.MainWindow window) window.ActivateFromSecondLaunch();
        else _activationPending = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
