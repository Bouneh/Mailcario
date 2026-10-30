using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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
        _folderView.NotificationFolders ??= ["INBOX"];
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
            var notified = _folderView.NotificationFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var visible = _allFolders.Where(folder =>
                !hidden.Contains(folder.ServerName) &&
                !_allFolders.Any(parent => IsDescendant(folder, parent.ServerName, parent.Delimiter) &&
                    (hidden.Contains(parent.ServerName) || collapsed.Contains(parent.ServerName))))
                .Select(folder => new MailFolder
                {
                    ServerName = folder.ServerName,
                    Delimiter = folder.Delimiter,
                    CanSelect = folder.CanSelect,
                    IsSent = folder.IsSent,
                    HasChildren = _allFolders.Any(child => IsDescendant(child, folder.ServerName, folder.Delimiter)),
                    IsExpanded = !collapsed.Contains(folder.ServerName),
                    NotificationsEnabled = notified.Contains(folder.ServerName)
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

    private IReadOnlyList<string> NotificationFoldersFor(string email) =>
        _settings.FolderViews.TryGetValue(email.ToLowerInvariant(), out FolderViewState? view)
            ? view.NotificationFolders ?? ["INBOX"] : ["INBOX"];

    private bool IsFolderNotificationEnabled(string email, string folder) =>
        NotificationFoldersFor(email).Contains(folder, StringComparer.OrdinalIgnoreCase);

    private void FolderItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: MailFolder folder } row ||
            !folder.CanSelect || _account is null) return;
        bool enabled = IsFolderNotificationEnabled(_account.Email, folder.ServerName);
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("MailMessageContextMenuStyle"),
            PlacementTarget = row,
            Placement = PlacementMode.MousePoint
        };
        var toggle = NewMenuItem(enabled ? "disableFolderNotifications" : "enableFolderNotifications");
        toggle.Click += async (_, _) => await SetFolderNotificationsAsync(folder, !enabled);
        menu.Items.Add(toggle);
        row.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task SetFolderNotificationsAsync(MailFolder folder, bool enabled)
    {
        MailAccount? account = _account;
        if (account is null) return;
        if (enabled) await PrimeFolderNotificationCursorAsync(account, folder.ServerName);
        if (_account != account) return;
        var notified = _folderView.NotificationFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabled) notified.Add(folder.ServerName);
        else notified.Remove(folder.ServerName);
        _folderView.NotificationFolders = notified.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        SaveFolderView();
        ApplyFolderList();
    }

    private void RenameFolderNotificationPreference(string source, string target, char separator)
    {
        string Rename(string name) => name.Equals(source, StringComparison.OrdinalIgnoreCase)
            ? target : name.StartsWith(source + separator, StringComparison.OrdinalIgnoreCase)
            ? target + name[source.Length..] : name;
        _folderView.NotificationFolders = _folderView.NotificationFolders.Select(Rename)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_account is not null)
        {
            foreach (var key in _folderCursors.Keys.Where(key =>
                         key.Email.Equals(_account.Email, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                string renamed = Rename(key.Folder);
                if (renamed == key.Folder) continue;
                var cursor = _folderCursors[key];
                _folderCursors.Remove(key);
                _folderCursors[(key.Email, renamed)] = cursor;
            }
        }
        SaveFolderView();
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
