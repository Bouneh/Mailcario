using System.Windows;

namespace Mailcario;

public partial class MainWindow
{
    private async void FileImportThunderbird_Click(object sender, RoutedEventArgs e)
    {
        FileOverlay.Visibility = Visibility.Collapsed;
        var window = new ThunderbirdImportWindow { Owner = this };
        if (window.ShowDialog() != true) return;
        int imported = AccountStore.ImportAccounts(window.SelectedAccounts);
        _accounts = AccountStore.LoadAccounts();
        RefreshAccountList();
        StatusLabel.Text = string.Format(Localizer.T("thunderbirdImported"), imported);
        MailAccount? first = window.SelectedAccounts.FirstOrDefault(x => _accounts.Any(a => a.Email.Equals(x.Email, StringComparison.OrdinalIgnoreCase)));
        if (first is not null && ShowAccountWindow(first)) await RefreshAsync(true);
    }
}
