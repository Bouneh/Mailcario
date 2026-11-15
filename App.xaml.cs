using System.Windows;
using System.IO;
using System.Windows.Threading;

namespace Mailcario;

public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private bool _activationPending;
    private string? _pendingMailto;

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
        string? mailto = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
        _singleInstance = new SingleInstanceCoordinator("Mailcario", payload =>
        {
            try
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    Dispatcher.BeginInvoke(new Action(() => ActivateExistingWindow(payload)));
            }
            catch (InvalidOperationException) { /* The application is closing. */ }
        }, mailto);
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
        window.StartScheduledSending();
        if (_activationPending) ActivateExistingWindow(_pendingMailto);
        if (mailto is not null) window.OpenMailto(mailto);
    }

    private void ActivateExistingWindow(string? mailto = null)
    {
        if (MainWindow is Mailcario.MainWindow window)
        {
            window.ActivateFromSecondLaunch();
            if (mailto is not null) window.OpenMailto(mailto);
        }
        else { _activationPending = true; _pendingMailto = mailto; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
