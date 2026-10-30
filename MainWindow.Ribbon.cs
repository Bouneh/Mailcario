using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

public partial class MainWindow
{
    private string _activeRibbonTab = "home";
    private bool _updatingRibbon;
    private bool _unreadOnly;

    private void InitializeRibbon()
    {
        _updatingRibbon = true;
        RibbonSortBox.SelectedItem = RibbonSortBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == _settings.SortBy) ?? RibbonSortBox.Items[0];
        RibbonReaderPane.IsChecked = _settings.ShowReadingPane;
        RibbonCompact.IsChecked = _settings.CompactMessages;
        RibbonOffline.IsChecked = false;
        _updatingRibbon = false;
        ApplyRibbonPreferences();
        ApplyViewPreferences();
    }

    private void ApplyRibbonLanguage()
    {
        FileTabButton.Content = Localizer.T("file");
        HomeTabButton.Content = Localizer.T("home");
        SyncTabButton.Content = Localizer.T("sendReceive");
        FolderTabButton.Content = Localizer.T("folder");
        ViewTabButton.Content = Localizer.T("view");
        SettingsTabButton.Content = Localizer.T("settings");
        AboutTabButton.Content = Localizer.T("about");
        RibbonNew.Content = Localizer.T("newMail");
        RibbonNewGroupCaption.Text = Localizer.T("newGroup");
        RibbonReplyGroupCaption.Text = Localizer.T("respondGroup");
        RibbonManageGroupCaption.Text = Localizer.T("manageGroup");
        RibbonReply.Content = Localizer.T("reply");
        RibbonForward.Content = Localizer.T("forward");
        RibbonMarkRead.Content = Localizer.T("markRead");
        RibbonMarkUnread.Content = Localizer.T("markUnread");
        RibbonRefresh.Content = Localizer.T("refresh");
        RibbonRules.Content = Localizer.T("rules");
        RibbonSyncAll.Content = Localizer.T("syncAll");
        RibbonSyncFolder.Content = Localizer.T("syncFolder");
        RibbonCancelSync.Content = Localizer.T("cancelSync");
        RibbonOffline.Content = Localizer.T("workOffline");
        RibbonNewFolder.Content = Localizer.T("newFolder");
        RibbonRenameFolder.Content = Localizer.T("renameFolder");
        RibbonRefreshFolders.Content = Localizer.T("refreshFolders");
        RibbonMarkFolderRead.Content = Localizer.T("markFolderRead");
        RibbonSortLabel.Text = Localizer.T("sortBy");
        ((ComboBoxItem)RibbonSortBox.Items[0]).Content = Localizer.T("date");
        ((ComboBoxItem)RibbonSortBox.Items[1]).Content = Localizer.T("sender");
        ((ComboBoxItem)RibbonSortBox.Items[2]).Content = Localizer.T("subject");
        RibbonReverseSort.Content = Localizer.T("reverseSort");
        RibbonReaderPane.Content = Localizer.T("readerPane");
        RibbonCompact.Content = Localizer.T("compact");
        FileBackButton.Content = Localizer.T("back");
        FileHeading.Text = Localizer.T("file");
        FileSubtitle.Text = Localizer.T("fileSubtitle");
        FileAccountHeading.Text = Localizer.T("accounts");
        FileNoAccounts.Text = Localizer.T("noAccounts");
        FileAddAccount.Content = Localizer.T("addAccount");
        FileImportThunderbird.Content = Localizer.T("thunderbirdImportMenu");
        FileSwitchAccount.Content = Localizer.T("switchAccount");
        FileEditAccount.Content = Localizer.T("editAccount");
        FileClientHeading.Text = Localizer.T("clientSettings");
        FileSettings.Content = Localizer.T("settings");
        FileAbout.Content = Localizer.T("about");
        FileClose.Content = Localizer.T("closeFile");
        FileQuit.Content = Localizer.T("quitMailcarioTray");
    }

    private void ApplyRibbonPreferences()
    {
        RibbonRow.Height = new GridLength(_settings.ShowRibbon ? 132 : 34);
        HomeRibbon.Visibility = _settings.ShowRibbon && _settings.HomeCommands && _activeRibbonTab == "home" ? Visibility.Visible : Visibility.Collapsed;
        SyncRibbon.Visibility = _settings.ShowRibbon && _settings.SyncCommands && _activeRibbonTab == "sync" ? Visibility.Visible : Visibility.Collapsed;
        FolderRibbon.Visibility = _settings.ShowRibbon && _settings.FolderCommands && _activeRibbonTab == "folder" ? Visibility.Visible : Visibility.Collapsed;
        ViewRibbon.Visibility = _settings.ShowRibbon && _settings.ViewCommands && _activeRibbonTab == "view" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (name, button) in new[] { ("home", HomeTabButton), ("sync", SyncTabButton), ("folder", FolderTabButton), ("view", ViewTabButton) })
        {
            button.Background = name == _activeRibbonTab ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
            button.Foreground = name == _activeRibbonTab ? (Brush)FindResource("AccentDark") : (Brush)FindResource("Ink");
        }
        UpdateRibbonSelection();
    }

    private void UpdateRibbonSelection()
    {
        if (!IsInitialized) return;
        bool hasMessage = _selected is not null;
        RibbonReply.IsEnabled = RibbonForward.IsEnabled = RibbonMarkRead.IsEnabled = RibbonMarkUnread.IsEnabled = hasMessage;
        RibbonRenameFolder.IsEnabled = _localFolderId is null && !_folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase);
        FileEditAccount.IsEnabled = _accounts.Count > 0;
        FileSwitchAccount.IsEnabled = _accounts.Count > 0;
    }

    private void SelectRibbonTab(string tab)
    {
        FileOverlay.Visibility = Visibility.Collapsed;
        _activeRibbonTab = tab;
        ApplyRibbonPreferences();
    }

    private void AllFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _unreadOnly = false;
        UpdateMessageFilterButtons();
        FilterMessages();
    }

    private void UnreadFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _unreadOnly = true;
        UpdateMessageFilterButtons();
        FilterMessages();
    }

    private void SortSummaryButton_Click(object sender, RoutedEventArgs e) => SelectRibbonTab("view");

    private void UpdateMessageFilterButtons()
    {
        AllFilterButton.Content = Localizer.T("allMail");
        UnreadFilterButton.Content = Localizer.T("unreadMail");
        AllFilterButton.Foreground = _unreadOnly ? (Brush)FindResource("Muted") : (Brush)FindResource("AccentDark");
        UnreadFilterButton.Foreground = _unreadOnly ? (Brush)FindResource("AccentDark") : (Brush)FindResource("Muted");
        SortSummaryButton.Content = Localizer.T(_settings.SortBy switch
        {
            "sender" => "sortSender",
            "subject" => "sortSubject",
            _ => "sortDate"
        }) + (_settings.SortDescending ? "  ↓" : "  ↑");
    }

    private void FileTab_Click(object sender, RoutedEventArgs e)
    {
        FileAccounts.ItemsSource = _accounts;
        FileAccounts.SelectedItem = _account;
        FileNoAccounts.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileOverlay.Visibility = Visibility.Visible;
    }
    private void HomeTab_Click(object sender, RoutedEventArgs e) => SelectRibbonTab("home");
    private void SyncTab_Click(object sender, RoutedEventArgs e) => SelectRibbonTab("sync");
    private void FolderTab_Click(object sender, RoutedEventArgs e) => SelectRibbonTab("folder");
    private void ViewTab_Click(object sender, RoutedEventArgs e) => SelectRibbonTab("view");
    private void FileBack_Click(object sender, RoutedEventArgs e) => FileOverlay.Visibility = Visibility.Collapsed;
    private void FileClose_Click(object sender, RoutedEventArgs e) => FileOverlay.Visibility = Visibility.Collapsed;
    private void FileQuit_Click(object sender, RoutedEventArgs e) => QuitMailcario();
    private void AboutTab_Click(object sender, RoutedEventArgs e)
    {
        FileOverlay.Visibility = Visibility.Collapsed;
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void FileSwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        if (FileAccounts.SelectedItem is not MailAccount account) return;
        FileOverlay.Visibility = Visibility.Collapsed;
        AccountList.SelectedItem = account;
    }

    private async void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        FileOverlay.Visibility = Visibility.Collapsed;
        var window = new SettingsWindow(_settings, _accounts) { Owner = this };
        if (window.ShowDialog() != true) return;
        _settings = window.Settings;
        bool activeRemoved = window.AccountsToRemove.Contains(_account?.Email ?? "", StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string email in window.AccountsToRemove)
            {
                if (!AccountStore.RemoveAccount(email)) continue;
                _settings.AutoLoadImages.Remove(email);
                foreach (string key in _settings.FolderViews.Keys.Where(key =>
                             key.Equals(email, StringComparison.OrdinalIgnoreCase)).ToList())
                    _settings.FolderViews.Remove(key);
                SignatureStore.RemoveAccount(email);
                MailRuleStore.RemoveAccount(email);
                _connectedAccounts.Remove(email);
                _connectionErrors.Remove(email);
                _inboxCursors.Remove(email);
                foreach (var key in _folderCursors.Keys.Where(key =>
                             key.Email.Equals(email, StringComparison.OrdinalIgnoreCase)).ToList())
                    _folderCursors.Remove(key);
                _unreadInboxCounts.Remove(email);
            }
            _settings.Save();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        if (window.AccountsToRemove.Count > 0)
        {
            _accounts = AccountStore.LoadAccounts();
            UpdateUnreadBadge();
            if (activeRemoved)
            {
                _account = _accounts.FirstOrDefault();
                _password = _account is null ? null : AccountStore.LoadPassword(_account.Email);
                ResetMailbox();
            }
            RefreshAccountList();
            if (activeRemoved && _account is not null && _password is not null)
                await RefreshAsync(true);
            else if (activeRemoved && _account is null)
                StatusLabel.Text = Localizer.T("statusConnect");
        }
        ThemeManager.SetMode(_settings.ThemeMode);
        ConfigureMailTimer();
        ApplyRibbonPreferences();
        ApplyViewPreferences();
        FilterMessages();
        if (_account is not null && _settings.AutoLoadImages.TryGetValue(_account.Email, out bool autoImages)
            && autoImages && _selectedContent?.HtmlText.Length > 0)
            await LoadImagesAsync();
    }

    private void ApplyViewPreferences()
    {
        MessageList.Tag = _settings.CompactMessages ? new Thickness(17, 8, 16, 8) : new Thickness(17, 15, 16, 15);
        _updatingRibbon = true;
        RibbonReaderPane.IsChecked = _settings.ShowReadingPane;
        RibbonCompact.IsChecked = _settings.CompactMessages;
        _updatingRibbon = false;
        ApplyReadingPane();
    }

    private void ApplyReadingPane()
    {
        if (_calendarMode) return;
        MessagesPanel.Visibility = Visibility.Visible;
        if (_settings.ShowReadingPane)
        {
            MessageColumn.MinWidth = 330;
            MessageColumn.MaxWidth = 760;
            MessageColumn.Width = _savedMessageWidth.Value > 0 ? _savedMessageWidth : new GridLength(510);
            ReaderColumn.MinWidth = 300;
            ReaderColumn.Width = new GridLength(1, GridUnitType.Star);
            MessageSplitterColumn.Width = new GridLength(4);
            ReaderPanel.Visibility = MessageSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            _savedMessageWidth = MessageColumn.Width.Value > 0 && MessageColumn.Width.IsAbsolute ? MessageColumn.Width : _savedMessageWidth;
            ReaderPanel.Visibility = MessageSplitter.Visibility = Visibility.Collapsed;
            ReaderColumn.MinWidth = 0;
            ReaderColumn.Width = new GridLength(0);
            MessageSplitterColumn.Width = new GridLength(0);
            MessageColumn.MaxWidth = double.PositiveInfinity;
            MessageColumn.Width = new GridLength(1, GridUnitType.Star);
        }
    }

    private void RibbonSortBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRibbon || !IsInitialized || RibbonSortBox.SelectedItem is not ComboBoxItem item) return;
        _settings.SortBy = item.Tag?.ToString() ?? "date";
        _settings.Save();
        UpdateMessageFilterButtons();
        FilterMessages();
    }

    private void RibbonReverseSort_Click(object sender, RoutedEventArgs e)
    {
        _settings.SortDescending = !_settings.SortDescending;
        _settings.Save();
        UpdateMessageFilterButtons();
        FilterMessages();
    }

    private void RibbonReaderPane_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingRibbon || !IsInitialized) return;
        _settings.ShowReadingPane = RibbonReaderPane.IsChecked == true;
        _settings.Save();
        ApplyReadingPane();
    }

    private void RibbonCompact_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingRibbon || !IsInitialized) return;
        _settings.CompactMessages = RibbonCompact.IsChecked == true;
        _settings.Save();
        ApplyViewPreferences();
    }

    private async void RibbonSyncAll_Click(object sender, RoutedEventArgs e)
    {
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        await RefreshAsync(true);
        await PollAllAccountsAsync();
    }

    private void RibbonCancelSync_Click(object sender, RoutedEventArgs e)
    {
        _mailLoadCts?.Cancel();
        _searchCts?.Cancel();
        StatusLabel.Text = Localizer.T("syncCancelled");
    }

    private void RibbonOffline_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingRibbon || !IsInitialized) return;
        _offlineMode = RibbonOffline.IsChecked == true;
        if (_offlineMode)
        {
            _mailLoadCts?.Cancel();
            _searchCts?.Cancel();
        }
        ConfigureMailTimer();
        if (_offlineMode) StatusLabel.Text = Localizer.T("offlineStatus");
        UpdateConnectionStatus();
    }

    private async Task RunImapActionAsync(Func<ImapSession, Task> action, bool refreshFolders = false)
    {
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        if (_account is null || _password is null) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(_account, _password);
            await action(session);
            await RefreshAsync(refreshFolders);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async void RibbonMarkRead_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } mail) return;
        if (mail.LocalId.Length > 0) SetLocalRead(mail, true);
        else await RunImapActionAsync(s => s.SetReadAsync(_folder, mail.Uid, true));
    }
    private async void RibbonMarkUnread_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } mail) return;
        if (mail.LocalId.Length > 0) SetLocalRead(mail, false);
        else await RunImapActionAsync(s => s.SetReadAsync(_folder, mail.Uid, false));
    }
    private async void RibbonMarkFolderRead_Click(object sender, RoutedEventArgs e)
    {
        if (_localFolderId is not null)
        {
            foreach (MailItem message in _messages.Where(item => !item.IsRead).ToList()) SetLocalRead(message, true);
            return;
        }
        await RunImapActionAsync(s => s.MarkAllReadAsync(_folder));
    }
    private async void RibbonRefreshFolders_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private async void RibbonNewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_account is null) return;
        string? name = FolderNamePrompt(Localizer.T("newFolder"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        char separator = _folderDelimiter ?? '/';
        if (name.Contains(separator)) return;
        string fullName = _folder + separator + ImapFolderName.Encode(name.Trim());
        await RunImapActionAsync(s => s.CreateFolderAsync(fullName), true);
    }

    private async void RibbonRenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            StatusLabel.Text = Localizer.T("cannotRenameInbox");
            return;
        }
        char separator = _folderDelimiter ?? '/';
        int lastSeparator = _folder.LastIndexOf(separator);
        string? name = FolderNamePrompt(Localizer.T("renameFolder"), CurrentFolderLabel());
        if (string.IsNullOrWhiteSpace(name) || name.Contains(separator)) return;
        string target = (lastSeparator >= 0 ? _folder[..(lastSeparator + 1)] : "") + ImapFolderName.Encode(name.Trim());
        string source = _folder;
        if (source == target) return;
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        if (_account is null || _password is null) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(_account, _password);
            await session.RenameFolderAsync(source, target);
            RenameFolderNotificationPreference(source, target, separator);
            _folder = target;
            await RefreshAsync(true);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private string? FolderNamePrompt(string title, string initial)
    {
        Window prompt = CreateFolderNamePromptWindow(title, initial, out TextBox input);
        return prompt.ShowDialog() == true ? input.Text : null;
    }

    private Window CreateFolderNamePromptWindow(string title, string initial, out TextBox input)
    {
        var prompt = new Window
        {
            Title = title, Owner = IsLoaded ? this : null,
            WindowStartupLocation = IsLoaded ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Width = 400, MinHeight = 220, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("Canvas"), Foreground = (Brush)FindResource("Ink"), ShowInTaskbar = false
        };
        WindowTheme.Attach(prompt);
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = Localizer.T("folderName"), Margin = new Thickness(0, 0, 0, 8) });
        var field = new TextBox { Text = initial };
        input = field;
        root.Children.Add(field);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = Localizer.T("cancel"), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => prompt.DialogResult = false;
        var save = new Button { Content = Localizer.T("save") };
        save.Click += (_, _) => prompt.DialogResult = true;
        controls.Children.Add(cancel);
        controls.Children.Add(save);
        root.Children.Add(controls);
        prompt.Content = root;
        prompt.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        return prompt;
    }
}
