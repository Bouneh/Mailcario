using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

public partial class MainWindow
{
    private static string ParentPath(MailFolder folder)
    {
        if (folder.Delimiter is not char separator) return "";
        int index = folder.ServerName.LastIndexOf(separator);
        return index > 0 ? folder.ServerName[..index] : "";
    }

    private void OpenFolderWindow(MailFolder folder)
    {
        if (_account is null || !folder.CanSelect) return;
        new MainWindow(null, null, secondaryWindow: true,
            initialAccountEmail: _account.Email, initialFolderName: folder.ServerName).Show();
    }

    private async Task CreateChildFolderAsync(MailFolder parent)
    {
        if (_account is null) return;
        char separator = parent.Delimiter ?? _folderDelimiter ?? '/';
        string? name = FolderNamePrompt(Localizer.T("newFolder"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Contains(separator)) { StatusLabel.Text = Localizer.T("folderNameInvalid"); return; }
        string target = parent.ServerName + separator + ImapFolderName.Encode(name.Trim());
        if (_allFolders.Any(folder => folder.ServerName.Equals(target, StringComparison.OrdinalIgnoreCase)))
        { StatusLabel.Text = Localizer.T("folderAlreadyExists"); return; }
        await RunImapActionAsync(session => session.CreateFolderAsync(target), true);
    }

    private async Task RenameServerFolderAsync(MailFolder folder)
    {
        if (_account is null || folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return;
        char separator = folder.Delimiter ?? _folderDelimiter ?? '/';
        string? name = FolderNamePrompt(Localizer.T("renameFolder"), folder.DisplayName);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Contains(separator)) { StatusLabel.Text = Localizer.T("folderNameInvalid"); return; }
        string parent = ParentPath(folder);
        string target = (parent.Length == 0 ? "" : parent + separator) + ImapFolderName.Encode(name.Trim());
        if (target.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase)) return;
        await RenameOrMoveFolderAsync(folder, target);
    }

    private async Task MoveServerFolderAsync(MailFolder folder)
    {
        if (_account is null || folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return;
        string? target = PromptFolderDestination(folder, copy: false);
        if (target is null || target.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase)) return;
        await RenameOrMoveFolderAsync(folder, target);
    }

    private async Task RenameOrMoveFolderAsync(MailFolder folder, string target)
    {
        MailAccount? account = _account;
        if (account is null || _offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        string? password = AccountStore.LoadPassword(account.Email);
        if (password is null) return;
        if (_allFolders.Any(existing => existing.ServerName.Equals(target, StringComparison.OrdinalIgnoreCase)))
        { StatusLabel.Text = Localizer.T("folderAlreadyExists"); return; }
        char separator = folder.Delimiter ?? _folderDelimiter ?? '/';
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            await session.RenameFolderAsync(folder.ServerName, target);
            if (_account != account) return;
            RenameFolderNotificationPreference(folder.ServerName, target, separator);
            if (_folder.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase) ||
                _folder.StartsWith(folder.ServerName + separator, StringComparison.OrdinalIgnoreCase))
                _folder = target + _folder[folder.ServerName.Length..];
            await RefreshAsync(true);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task CopyServerFolderAsync(MailFolder folder)
    {
        MailAccount? account = _account;
        if (account is null || _offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        string? password = AccountStore.LoadPassword(account.Email);
        if (password is null) return;
        string? target = PromptFolderDestination(folder, copy: true);
        if (target is null) return;
        char separator = folder.Delimiter ?? _folderDelimiter ?? '/';
        var sourceTree = _allFolders.Where(candidate =>
            candidate.ServerName.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase) ||
            candidate.ServerName.StartsWith(folder.ServerName + separator, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.ServerName.Length).ToList();
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            foreach (MailFolder source in sourceTree)
            {
                string destination = target + source.ServerName[folder.ServerName.Length..];
                await session.CreateFolderAsync(destination);
                if (source.CanSelect) await session.CopyAllMessagesAsync(source.ServerName, destination);
            }
            if (_account == account) await RefreshAsync(true);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message;
            if (_account == account) await RefreshAsync(true);
        }
    }

    private string? PromptFolderDestination(MailFolder folder, bool copy)
    {
        if (_account is null) return null;
        char separator = folder.Delimiter ?? _folderDelimiter ?? '/';
        string title = Localizer.T(copy ? "copyFolder" : "moveFolder");
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 470, MinHeight = 280, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("Canvas"), ShowInTaskbar = false };
        WindowTheme.Attach(dialog);
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Ink"), Margin = new Thickness(0, 0, 0, 18) });
        root.Children.Add(new TextBlock { Text = Localizer.T("folderDestination"),
            Foreground = (Brush)FindResource("Ink"), Margin = new Thickness(0, 0, 0, 7) });
        var parents = new ComboBox { Margin = new Thickness(0, 0, 0, 15) };
        parents.Items.Add(new ComboBoxItem { Content = Localizer.T("folderRoot"), Tag = "" });
        foreach (MailFolder candidate in OrderedFolders().Where(candidate =>
            !candidate.ServerName.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase) &&
            !candidate.ServerName.StartsWith(folder.ServerName + separator, StringComparison.OrdinalIgnoreCase)))
            parents.Items.Add(new ComboBoxItem { Content = ImapFolderName.Decode(candidate.ServerName), Tag = candidate.ServerName });
        string currentParent = ParentPath(folder);
        parents.SelectedItem = parents.Items.Cast<ComboBoxItem>().FirstOrDefault(item =>
            item.Tag?.ToString()?.Equals(currentParent, StringComparison.OrdinalIgnoreCase) == true) ?? parents.Items[0];
        root.Children.Add(parents);
        root.Children.Add(new TextBlock { Text = Localizer.T("folderName"),
            Foreground = (Brush)FindResource("Ink"), Margin = new Thickness(0, 0, 0, 7) });
        var name = new TextBox { Text = folder.DisplayName + (copy ? Localizer.T("folderCopySuffix") : ""),
            Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(name);
        var validation = new TextBlock { Foreground = (Brush)FindResource("ErrorText"),
            Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(validation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Localizer.T("cancel"), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var accept = new Button { Content = Localizer.T("ok"), Style = (Style)FindResource("AccentButton") };
        string? result = null;
        accept.Click += (_, _) =>
        {
            string value = name.Text.Trim();
            if (value.Length is < 1 or > 100 || value.Any(char.IsControl) || value.Contains(separator))
            { validation.Text = Localizer.T("folderNameInvalid"); return; }
            string parent = (parents.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            string target = (parent.Length == 0 ? "" : parent + separator) + ImapFolderName.Encode(value);
            if (_allFolders.Any(candidate => candidate.ServerName.Equals(target, StringComparison.OrdinalIgnoreCase)))
            { validation.Text = Localizer.T("folderAlreadyExists"); return; }
            result = target;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        root.Children.Add(buttons);
        dialog.Content = root;
        name.SelectAll();
        name.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private async Task DeleteServerFolderAsync(MailFolder folder)
    {
        MailAccount? account = _account;
        if (account is null || folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return;
        if (MessageBox.Show(this, string.Format(Localizer.T("deleteFolderConfirm"), folder.DisplayName),
            Localizer.T("deleteFolder"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        string? password = AccountStore.LoadPassword(account.Email);
        if (password is null) return;
        char separator = folder.Delimiter ?? _folderDelimiter ?? '/';
        var tree = _allFolders.Where(candidate =>
            candidate.ServerName.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase) ||
            candidate.ServerName.StartsWith(folder.ServerName + separator, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.ServerName.Length).ToList();
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            foreach (MailFolder item in tree) await session.DeleteFolderAsync(item.ServerName);
            if (_account != account) return;
            RemoveFolderPreferences(folder.ServerName, separator);
            if (_folder.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase) ||
                _folder.StartsWith(folder.ServerName + separator, StringComparison.OrdinalIgnoreCase)) _folder = "INBOX";
            await RefreshAsync(true);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message;
            if (_account == account) await RefreshAsync(true);
        }
    }

    private void RemoveFolderPreferences(string source, char separator)
    {
        bool Keep(string name) => !name.Equals(source, StringComparison.OrdinalIgnoreCase) &&
            !name.StartsWith(source + separator, StringComparison.OrdinalIgnoreCase);
        _folderView.HiddenFolders.RemoveAll(name => !Keep(name));
        _folderView.CollapsedFolders.RemoveAll(name => !Keep(name));
        _folderView.NotificationFolders.RemoveAll(name => !Keep(name));
        _folderView.FavoriteFolders.RemoveAll(name => !Keep(name));
        _folderView.ManualFolderOrder.RemoveAll(name => !Keep(name));
        _folderView.AlphabeticallySortedFolders.RemoveAll(name => !Keep(name));
        SaveFolderView();
    }

    private async Task MarkServerFolderReadAsync(MailFolder folder)
    {
        MailAccount? account = _account;
        if (account is null || !folder.CanSelect || _offlineMode) return;
        string? password = AccountStore.LoadPassword(account.Email);
        if (password is null) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            await session.MarkAllReadAsync(folder.ServerName);
            if (folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                SetInboxUnreadCount(account.Email, 0);
            if (_account == account && _folder.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase))
                await RefreshAsync();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task DeleteServerFolderContentsAsync(MailFolder folder)
    {
        MailAccount? account = _account;
        if (account is null || !folder.CanSelect || _offlineMode) return;
        MailFolder? trash = FindSpecialFolder("trash");
        if (trash is null) { StatusLabel.Text = Localizer.T("folderNotFound"); return; }
        bool permanent = trash.ServerName.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase);
        string prompt = string.Format(Localizer.T(permanent ? "emptyTrashConfirm" : "deleteFolderContentsConfirm"), folder.DisplayName);
        if (MessageBox.Show(this, prompt, Localizer.T("deleteFolderContents"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        string? password = AccountStore.LoadPassword(account.Email);
        if (password is null) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            if (permanent) await session.ExpungeAllMessagesAsync(folder.ServerName);
            else await session.MoveAllMessagesAsync(folder.ServerName, trash.ServerName);
            if (folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                SetInboxUnreadCount(account.Email, 0);
            if (_account == account && _folder.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase))
                await RefreshAsync();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void ToggleFavoriteFolder(MailFolder folder)
    {
        if (_folderView.FavoriteFolders.Contains(folder.ServerName, StringComparer.OrdinalIgnoreCase))
            _folderView.FavoriteFolders.RemoveAll(name => name.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase));
        else _folderView.FavoriteFolders.Add(folder.ServerName);
        SaveFolderView();
        RefreshFavoriteFolders();
    }

    private void ToggleAlphabeticalFolders(MailFolder folder)
    {
        if (_folderView.AlphabeticallySortedFolders.Contains(folder.ServerName,
            StringComparer.OrdinalIgnoreCase))
            _folderView.AlphabeticallySortedFolders.RemoveAll(name => name.Equals(folder.ServerName,
                StringComparison.OrdinalIgnoreCase));
        else _folderView.AlphabeticallySortedFolders.Add(folder.ServerName);
        SaveFolderView();
        ApplyFolderList();
    }

    private void MoveFolderOrder(MailFolder folder, int direction)
    {
        List<MailFolder> ordered = OrderedFolders().ToList();
        string parent = ParentPath(folder);
        List<MailFolder> siblings = ordered.Where(item => ParentPath(item).Equals(parent,
            StringComparison.OrdinalIgnoreCase)).ToList();
        int index = siblings.FindIndex(item => item.ServerName.Equals(folder.ServerName, StringComparison.OrdinalIgnoreCase));
        int neighbor = index + direction;
        if (index < 0 || neighbor < 0 || neighbor >= siblings.Count) return;
        _folderView.ManualFolderOrder = ordered.Select(item => item.ServerName).ToList();
        _folderView.AlphabeticallySortedFolders.RemoveAll(name => name.Equals(parent,
            StringComparison.OrdinalIgnoreCase));
        int first = _folderView.ManualFolderOrder.FindIndex(name => name.Equals(siblings[index].ServerName, StringComparison.OrdinalIgnoreCase));
        int second = _folderView.ManualFolderOrder.FindIndex(name => name.Equals(siblings[neighbor].ServerName, StringComparison.OrdinalIgnoreCase));
        (_folderView.ManualFolderOrder[first], _folderView.ManualFolderOrder[second]) =
            (_folderView.ManualFolderOrder[second], _folderView.ManualFolderOrder[first]);
        SaveFolderView();
        ApplyFolderList();
    }

    private async Task ShowFolderPropertiesAsync(MailFolder folder)
    {
        MailAccount? account = _account;
        if (account is null) return;
        string email = account.Email;
        string unread = "—";
        if (folder.CanSelect && !_offlineMode && AccountStore.LoadPassword(email) is string password)
            try
            {
                await using var session = await ImapSession.ConnectAsync(account, password);
                unread = (await session.GetUnreadCountAsync(folder.ServerName)).ToString("N0");
            }
            catch (Exception) { }
        var dialog = new Window { Title = Localizer.T("folderProperties"), Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 500, MinHeight = 310,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("Canvas"), ShowInTaskbar = false };
        WindowTheme.Attach(dialog);
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = folder.DisplayName, Foreground = (Brush)FindResource("Ink"),
            FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        void AddDetail(string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Muted"), FontSize = 11 });
            root.Children.Add(new TextBlock { Text = value, Foreground = (Brush)FindResource("Ink"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) });
        }
        AddDetail(Localizer.T("account"), email);
        AddDetail(Localizer.T("folderServerPath"), folder.ServerName);
        AddDetail(Localizer.T("folderSelectable"), Localizer.T(folder.CanSelect ? "yes" : "no"));
        AddDetail(Localizer.T("folderUnreadCount"), unread);
        var close = new Button { Content = Localizer.T("close"), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        root.Children.Add(close);
        dialog.Content = root;
        dialog.ShowDialog();
    }
}
