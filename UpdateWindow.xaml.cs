using System.Windows;

namespace Mailcario;

public partial class UpdateWindow : Window
{
    private readonly AvailableUpdate _update;
    private bool _downloading;

    internal UpdateWindow(AvailableUpdate update)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        _update = update;
        Heading.Text = Localizer.T("updateAvailable");
        Description.Text = string.Format(Localizer.T("updateDescription"), update.Version.ToString(3));
        LaterButton.Content = Localizer.T("updateLater");
        DownloadButton.Content = Localizer.T("updateDownload");
        Closing += (_, args) => { if (_downloading) args.Cancel = true; };
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading) return;
        _downloading = true;
        DownloadButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        ProgressLabel.Text = Localizer.T("updateDownloading");
        try
        {
            var progress = new Progress<double>(value =>
            {
                DownloadProgress.Value = value;
                ProgressLabel.Text = string.Format(Localizer.T("updateProgress"), (int)value);
            });
            string installer = await UpdateService.DownloadAsync(_update, progress, CancellationToken.None);
            ProgressLabel.Text = Localizer.T("updateInstalling");
            UpdateService.ApplyAndRestart(installer, _update.Sha256);
            if (Owner is MainWindow mainWindow) mainWindow.QuitMailcario();
            else Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ProgressLabel.Text = string.Format(Localizer.T("updateFailed"), ex.Message);
            DownloadButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            _downloading = false;
        }
    }
}
