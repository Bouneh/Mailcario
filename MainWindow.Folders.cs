using System.Windows;
using System.Windows.Controls;

namespace Mailcario;

public partial class MainWindow
{
    private IReadOnlyList<MailFolder> _allFolders = [];
    private FolderViewState _folderView = new();

    private void RestoreFolderView()
    {
        _folderView = _account is not null && _settings.FolderViews.TryGetValue(_account.Email.ToLowerInvariant(), out var state)
            ? state : new FolderViewState();
        _folderView.HiddenFolders ??= [];
        _folderView.CollapsedFolders ??= [];
        ApplyAccountFolderState();
    }

    private void SaveFolderView()
    {
        if (_account is null) return;
        _settings.FolderViews[_account.Email.ToLowerInvariant()] = _folderView;
        try { _settings.Save(); }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private static bool IsDescendant(MailFolder folder, string parentName, char? delimiter) =>
        delimiter is char separator && folder.ServerName.StartsWith(parentName + separator,
            StringComparison.OrdinalIgnoreCase);

    private void ApplyFolderList()
    {
        _updatingFolders = true;
        try
        {
            var hidden = _folderView.HiddenFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var collapsed = _folderView.CollapsedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var visible = _allFolders.Where(folder =>
                !hidden.Contains(folder.ServerName) &&
                !_allFolders.Any(parent => IsDescendant(folder, parent.ServerName, parent.Delimiter) &&
                    (hidden.Contains(parent.ServerName) || collapsed.Contains(parent.ServerName))))
                .Select(folder => new MailFolder
                {
                    ServerName = folder.ServerName,
                    Delimiter = folder.Delimiter,
                    CanSelect = folder.CanSelect,
                    HasChildren = _allFolders.Any(child => IsDescendant(child, folder.ServerName, folder.Delimiter)),
                    IsExpanded = !collapsed.Contains(folder.ServerName)
                }).ToList();
            FolderList.ItemsSource = visible;
            FolderList.SelectedItem = visible.FirstOrDefault(x => x.ServerName.Equals(_folder,
                StringComparison.OrdinalIgnoreCase));
        }
        finally { _updatingFolders = false; }
        FolderOptionsButton.IsEnabled = _allFolders.Count > 0;
    }

    private void ApplyAccountFolderState()
    {
        AccountChevron.Text = _folderView.AccountExpanded ? "\uE70D" : "\uE76C";
        AccountFolderButton.ToolTip = Localizer.T(_folderView.AccountExpanded ? "collapseAccountFolders" : "expandAccountFolders");
        FolderList.Visibility = _folderView.AccountExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AccountFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _folderView.AccountExpanded = !_folderView.AccountExpanded;
        ApplyAccountFolderState();
        SaveFolderView();
    }

    private void FolderExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MailFolder folder } || !folder.HasChildren) return;
        var collapsed = _folderView.CollapsedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!collapsed.Add(folder.ServerName)) collapsed.Remove(folder.ServerName);
        _folderView.CollapsedFolders = collapsed.ToList();
        SaveFolderView();
        ApplyFolderList();
        e.Handled = true;
    }

    private async void FolderOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_allFolders.Count == 0) return;
        var dialog = new FolderVisibilityWindow(_allFolders, _folderView.HiddenFolders) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _folderView.HiddenFolders = dialog.HiddenFolders.ToList();
        if (dialog.ExpandAllRequested)
        {
            _folderView.CollapsedFolders.Clear();
            _folderView.AccountExpanded = true;
            ApplyAccountFolderState();
        }
        SaveFolderView();
        bool selectedHidden = _folderView.HiddenFolders.Any(name =>
            _folder.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            (_folderDelimiter is char separator && _folder.StartsWith(name + separator,
                StringComparison.OrdinalIgnoreCase)));
        if (selectedHidden)
        {
            ResetMailbox();
            await RefreshAsync(true);
        }
        else ApplyFolderList();
    }
}
