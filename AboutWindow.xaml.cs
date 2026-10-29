using System.Diagnostics;
using System.Windows;

namespace Mailcario;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/Bouneh/Mailcario";
    private AvailableUpdate? _availableUpdate;
    private bool _checking;

    public AboutWindow()
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        AboutHeading.Text = Localizer.T("about");
        VersionLabel.Text = string.Format(Localizer.T("installedVersion"), UpdateService.CurrentVersion.ToString(3));
        RepositoryButton.Content = Localizer.T("openRepository");
        RepositoryButton.ToolTip = RepositoryUrl;
        CheckButton.Content = Localizer.T("checkUpdates");
        UpdateStatus.Text = Localizer.T("checkUpdates");
        DownloadButton.Content = Localizer.T("updateDownload");
        CloseButton.Content = Localizer.T("close");
        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_checking) return;
        _checking = true;
        CheckButton.IsEnabled = false;
        DownloadButton.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = Localizer.T("checkingUpdates");
        try
        {
            _availableUpdate = await UpdateService.CheckAsync();
            if (!IsLoaded) return;
            UpdateStatus.Text = _availableUpdate is null ? Localizer.T("upToDate") :
                string.Format(Localizer.T("availableVersion"), _availableUpdate.Version.ToString(3));
            DownloadButton.Visibility = _availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (IsLoaded) UpdateStatus.Text = string.Format(Localizer.T("updateCheckFailed"), ex.Message);
        }
        finally
        {
            _checking = false;
            CheckButton.IsEnabled = true;
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
            new UpdateWindow(_availableUpdate) { Owner = this }.ShowDialog();
    }

    private void Repository_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { UpdateStatus.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
