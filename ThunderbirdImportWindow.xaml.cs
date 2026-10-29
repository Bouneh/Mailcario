using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace Mailcario;

public partial class ThunderbirdImportWindow : Window
{
    private IReadOnlyList<ThunderbirdCandidate> _candidates = [];
    public IReadOnlyList<MailAccount> SelectedAccounts { get; private set; } = [];

    public ThunderbirdImportWindow()
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        Heading.Text = Localizer.T("thunderbirdImportTitle");
        Description.Text = Localizer.T("thunderbirdImportDescription");
        RescanButton.Content = Localizer.T("thunderbirdRescan");
        ChooseFileButton.Content = Localizer.T("thunderbirdChoosePrefs");
        PrivacyNote.Text = Localizer.T("thunderbirdPrivacy");
        CancelButton.Content = Localizer.T("cancel");
        ImportButton.Content = Localizer.T("thunderbirdImportButton");
        Scan();
    }

    private void Scan()
    {
        try { ShowCandidates(ThunderbirdImporter.ScanDefaultProfiles()); }
        catch (Exception ex) { ShowCandidates([]); EmptyLabel.Text = ex.Message; }
    }

    private void ShowCandidates(IReadOnlyList<ThunderbirdCandidate> candidates)
    {
        var existing = AccountStore.LoadAccounts().Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _candidates = candidates.Where(x => !existing.Contains(x.Account.Email)).ToList();
        CandidateList.ItemsSource = _candidates;
        CandidateList.Visibility = _candidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Visibility = _candidates.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyLabel.Text = Localizer.T("thunderbirdNoAccounts");
        ImportButton.IsEnabled = _candidates.Count > 0;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => Scan();

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var chooser = new OpenFileDialog { Filter = "Thunderbird prefs.js|prefs.js|JavaScript preferences|*.js", CheckFileExists = true };
        if (chooser.ShowDialog(this) != true) return;
        try { ShowCandidates(ThunderbirdImporter.ReadProfile(chooser.FileName, Path.GetFileName(Path.GetDirectoryName(chooser.FileName)) ?? "Thunderbird")); }
        catch (Exception ex) { ShowCandidates([]); EmptyLabel.Text = ex.Message; }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SelectedAccounts = _candidates.Where(x => x.IsSelected).Select(x => x.Account).ToList();
        if (SelectedAccounts.Count == 0) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
