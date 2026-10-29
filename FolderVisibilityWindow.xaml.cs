using System.Windows;
using System.Windows.Controls;

namespace Mailcario;

public partial class FolderVisibilityWindow : Window
{
    private readonly List<CheckBox> _checks = [];
    public IReadOnlyList<string> HiddenFolders { get; private set; } = [];
    public bool ExpandAllRequested { get; private set; }

    public FolderVisibilityWindow(IReadOnlyList<MailFolder> folders, IReadOnlyCollection<string> hidden)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        Heading.Text = Localizer.T("chooseFolders");
        Description.Text = Localizer.T("chooseFoldersHint");
        ShowAllButton.Content = Localizer.T("showAllFolders");
        CancelButton.Content = Localizer.T("cancel");
        SaveButton.Content = Localizer.T("save");
        var hiddenSet = hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MailFolder folder in folders)
        {
            var check = new CheckBox
            {
                Content = folder.DisplayName,
                Tag = folder.ServerName,
                IsChecked = !hiddenSet.Contains(folder.ServerName),
                IsEnabled = !folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(4 + Math.Min(folder.Depth, 7) * 16, 0, 4, 10)
            };
            _checks.Add(check);
            FolderOptionsList.Children.Add(check);
        }
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox check in _checks) check.IsChecked = true;
        ExpandAllRequested = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HiddenFolders = _checks.Where(x => x.IsChecked != true).Select(x => (string)x.Tag).ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
