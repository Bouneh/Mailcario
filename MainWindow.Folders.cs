using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Mailcario;

public partial class MainWindow
{
    private IReadOnlyList<MailFolder> _allFolders = [];
    private FolderViewState _folderView = new();
    private readonly Dictionary<(string Email, string Folder), int> _folderUnreadCounts = [];

    private static (string Email, string Folder) FolderKey(string email, string folder) =>
        (email.ToLowerInvariant(), folder.ToLowerInvariant());

    private void SetFolderUnreadCount(string email, string folder, int count)
    {
        int unread = Math.Max(0, count);
        _folderUnreadCounts[FolderKey(email, folder)] = unread;
        if (!email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase)) return;
        foreach (MailFolder visible in FolderList.Items.OfType<MailFolder>())
            if (visible.ServerName.Equals(folder, StringComparison.OrdinalIgnoreCase))
                visible.UnreadCount = unread;
    }

    private void ApplyServerFolderList(string email, IReadOnlyList<MailFolder> folders)
    {
        if (!email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase)) return;
        bool changed = _allFolders.Count != folders.Count || _allFolders.Zip(folders).Any(pair =>
            !pair.First.ServerName.Equals(pair.Second.ServerName, StringComparison.OrdinalIgnoreCase) ||
            pair.First.Delimiter != pair.Second.Delimiter || pair.First.CanSelect != pair.Second.CanSelect ||
            pair.First.IsSent != pair.Second.IsSent);
        if (!changed) return;
        _allFolders = folders;
        _folderDelimiter = folders.FirstOrDefault(value => value.ServerName.Equals(_folder,
            StringComparison.OrdinalIgnoreCase))?.Delimiter;
        ApplyFolderList();
    }

    private void RestoreFolderView()
    {
        _folderView = _account is not null && _settings.FolderViews.TryGetValue(_account.Email.ToLowerInvariant(), out var state)
            ? state : new FolderViewState();
        _folderView.HiddenFolders ??= [];
        _folderView.CollapsedFolders ??= [];
        _folderView.NotificationFolders ??= ["INBOX"];
        _folderView.FavoriteFolders ??= [];
        _folderView.ManualFolderOrder ??= [];
        _folderView.AlphabeticallySortedFolders ??= [];
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
            var visible = OrderedFolders().Where(folder =>
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
                    NotificationsEnabled = notified.Contains(folder.ServerName),
                    UnreadCount = _account is null ? 0 :
                        _folderUnreadCounts.GetValueOrDefault(FolderKey(_account.Email, folder.ServerName))
                }).ToList();
            FolderList.ItemsSource = visible;
            FolderList.SelectedItem = _localFolderId is null ? visible.FirstOrDefault(x =>
                x.ServerName.Equals(_folder, StringComparison.OrdinalIgnoreCase)) : null;
            RefreshFavoriteFolders();
        }
        finally { _updatingFolders = false; }
        FolderOptionsButton.IsEnabled = _allFolders.Count > 0;
    }

    private IEnumerable<MailFolder> OrderedFolders()
    {
        var names = _allFolders.Select(folder => folder.ServerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var original = _allFolders.Select((folder, index) => (folder.ServerName, index))
            .ToDictionary(entry => entry.ServerName, entry => entry.index, StringComparer.OrdinalIgnoreCase);
        var manual = _folderView.ManualFolderOrder.Select((name, index) => (name, index))
            .GroupBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        string Parent(MailFolder folder)
        {
            if (folder.Delimiter is not char separator) return "";
            int index = folder.ServerName.LastIndexOf(separator);
            string parent = index > 0 ? folder.ServerName[..index] : "";
            return names.Contains(parent) ? parent : "";
        }
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<MailFolder> Children(string parent)
        {
            IEnumerable<MailFolder> siblings = _allFolders.Where(folder =>
                Parent(folder).Equals(parent, StringComparison.OrdinalIgnoreCase));
            siblings = _folderView.AlphabeticallySortedFolders.Contains(parent,
                StringComparer.OrdinalIgnoreCase)
                ? siblings.OrderBy(folder => folder.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                : siblings.OrderBy(folder => manual.GetValueOrDefault(folder.ServerName, int.MaxValue))
                    .ThenBy(folder => original[folder.ServerName]);
            foreach (MailFolder folder in siblings)
            {
                if (!visited.Add(folder.ServerName)) continue;
                yield return folder;
                foreach (MailFolder child in Children(folder.ServerName)) yield return child;
            }
        }
        foreach (MailFolder folder in Children("")) yield return folder;
        foreach (MailFolder folder in _allFolders.Where(folder => !visited.Contains(folder.ServerName))) yield return folder;
    }

    private void RefreshFavoriteFolders()
    {
        FavoriteFoldersPanel.Children.Clear();
        if (_account is null) return;
        foreach (string name in _folderView.FavoriteFolders)
        {
            MailFolder? folder = _allFolders.FirstOrDefault(candidate => candidate.CanSelect &&
                candidate.ServerName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (folder is null || folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) continue;
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = "\uE8B7", FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = (Brush)FindResource("IconFolder"), FontSize = 15, Margin = new Thickness(0, 0, 11, 0) });
            label.Children.Add(new TextBlock { Text = folder.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = (Style)FindResource(_localFolderId is null && !_unifiedMode &&
                    folder.ServerName.Equals(_folder, StringComparison.OrdinalIgnoreCase)
                    ? "NavActiveButton" : "NavButton"), Margin = new Thickness(0, 0, 0, 4),
                ToolTip = _account.Email + " · " + folder.ServerName };
            string target = folder.ServerName;
            button.Click += async (_, _) => await NavigateToFolderAsync(target);
            FavoriteFoldersPanel.Children.Add(button);
        }
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
        if (sender is not ListBoxItem { DataContext: MailFolder folder } row || _account is null) return;
        bool enabled = IsFolderNotificationEnabled(_account.Email, folder.ServerName);
        bool inbox = folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase);
        bool favorite = _folderView.FavoriteFolders.Contains(folder.ServerName, StringComparer.OrdinalIgnoreCase);
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("MailMessageContextMenuStyle"),
            MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 28),
            PlacementTarget = row,
            Placement = PlacementMode.MousePoint
        };
        if (folder.CanSelect) AddAction(menu, "openFolderWindow", () => OpenFolderWindow(folder));
        else AddUnavailable(menu, "openFolderWindow");
        menu.Items.Add(NewSeparator());
        AddAction(menu, "newFolder", async () => await CreateChildFolderAsync(folder));
        if (inbox) AddUnavailable(menu, "renameFolder");
        else AddAction(menu, "renameFolder", async () => await RenameServerFolderAsync(folder));
        AddAction(menu, "copyFolder", async () => await CopyServerFolderAsync(folder));
        if (inbox) AddUnavailable(menu, "moveFolder");
        else AddAction(menu, "moveFolder", async () => await MoveServerFolderAsync(folder));
        if (inbox) AddUnavailable(menu, "deleteFolder");
        else AddAction(menu, "deleteFolder", async () => await DeleteServerFolderAsync(folder));
        menu.Items.Add(NewSeparator());
        if (folder.CanSelect)
        {
            AddAction(menu, "markFolderRead", async () => await MarkServerFolderReadAsync(folder));
            AddUnavailable(menu, "cleanFolder");
            AddAction(menu, "deleteFolderContents", async () => await DeleteServerFolderContentsAsync(folder));
        }
        else
        {
            AddUnavailable(menu, "markFolderRead");
            AddUnavailable(menu, "cleanFolder");
            AddUnavailable(menu, "deleteFolderContents");
        }
        menu.Items.Add(NewSeparator());
        AddAction(menu, "imapFolders", () => FolderOptionsButton_Click(FolderOptionsButton, new RoutedEventArgs()));
        menu.Items.Add(NewSeparator());
        if (inbox || !folder.CanSelect) AddUnavailable(menu, "addFolderFavorite");
        else AddAction(menu, favorite ? "removeFolderFavorite" : "addFolderFavorite",
            () => ToggleFavoriteFolder(folder));
        bool sorted = _folderView.AlphabeticallySortedFolders.Contains(folder.ServerName,
            StringComparer.OrdinalIgnoreCase);
        if (folder.HasChildren)
            AddAction(menu, sorted ? "useServerFolderOrder" : "sortSubfolders",
                () => ToggleAlphabeticalFolders(folder));
        else AddUnavailable(menu, "sortSubfolders");
        AddAction(menu, "moveFolderUp", () => MoveFolderOrder(folder, -1));
        AddAction(menu, "moveFolderDown", () => MoveFolderOrder(folder, 1));
        menu.Items.Add(NewSeparator());
        AddUnavailable(menu, "processMarkedHeaders");
        AddAction(menu, "folderProperties", async () => await ShowFolderPropertiesAsync(folder));
        menu.Items.Add(NewSeparator());
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
        _folderView.HiddenFolders = _folderView.HiddenFolders.Select(Rename).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _folderView.CollapsedFolders = _folderView.CollapsedFolders.Select(Rename).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _folderView.FavoriteFolders = _folderView.FavoriteFolders.Select(Rename).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _folderView.ManualFolderOrder = _folderView.ManualFolderOrder.Select(Rename).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _folderView.AlphabeticallySortedFolders = _folderView.AlphabeticallySortedFolders.Select(Rename)
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
