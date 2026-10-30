using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Mailcario;

public partial class MainWindow
{
    private readonly LocalMailStore _localMailStore;
    private string? _localFolderId;
    private bool _updatingLocalFolders;
    private MailItem? _dragCandidate;
    private IReadOnlyList<MailItem> _dragSelection = [];
    private Point _dragStart;
    private LocalMailDragPayload? _activeLocalDrag;
    private string? _activeLocalDragToken;
    private const string LocalMailDragFormat = "Mailcario.LocalMailCopy";

    private void MessageItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: MailItem item } row && item.LocalId.Length == 0)
        {
            _dragCandidate = item;
            _dragSelection = row.IsSelected
                ? MessageList.SelectedItems.OfType<MailItem>().ToList() : [];
        }
        else
        {
            _dragCandidate = null;
            _dragSelection = [];
        }
        _dragStart = e.GetPosition(MessageList);
    }

    private void MessageList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        _dragSelection = [];
    }

    private void MessageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidate = null;
            _dragSelection = [];
            return;
        }
        if (_dragCandidate is not { } candidate || _localFolderId is not null || _offlineMode ||
            _account is null || _password is null) return;
        Point position = e.GetPosition(MessageList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragCandidate = null;
        var payload = LocalMailDragPayload.Create(_account, _folder, candidate,
            _dragSelection.Count > 1 ? _dragSelection : MessageList.SelectedItems.OfType<MailItem>());
        _dragSelection = [];
        if (payload is null) return;
        _activeLocalDrag = payload;
        _activeLocalDragToken = Guid.NewGuid().ToString("N");
        var data = new DataObject();
        data.SetData(LocalMailDragFormat, _activeLocalDragToken, false);
        try
        {
            DragDrop.DoDragDrop(MessageList, data, DragDropEffects.Copy);
            e.Handled = true;
        }
        finally
        {
            _activeLocalDrag = null;
            _activeLocalDragToken = null;
        }
    }

    private LocalMailDragPayload? DragPayload(DragEventArgs e)
    {
        if (_activeLocalDrag is null || _activeLocalDragToken is null ||
            !e.Data.GetDataPresent(LocalMailDragFormat, false)) return null;
        return e.Data.GetData(LocalMailDragFormat, false) is string token &&
            token == _activeLocalDragToken ? _activeLocalDrag : null;
    }

    private LocalMailFolder? LocalDropFolder(DependencyObject? source)
    {
        for (DependencyObject? current = source; current is not null && current != LocalFolderList;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is ListBoxItem { DataContext: LocalMailFolder folder }) return folder;
        return null;
    }

    private void LocalFolderList_DragOver(object sender, DragEventArgs e)
    {
        var payload = DragPayload(e);
        e.Effects = payload is not null && LocalDropFolder(e.OriginalSource as DependencyObject) is not null &&
            !_offlineMode && _account == payload.Account && _password is not null
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void LocalFolderList_Drop(object sender, DragEventArgs e)
    {
        var payload = DragPayload(e);
        var target = LocalDropFolder(e.OriginalSource as DependencyObject);
        if (payload is null || target is null || _offlineMode || _account != payload.Account || _password is null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        await CopyToLocalFolderAsync(payload.Messages, payload.SourceFolder, payload.Account, target.Id);
    }

    private void RefreshLocalFolders()
    {
        _updatingLocalFolders = true;
        try
        {
            var folders = _localMailStore.ListFolders();
            LocalFolderList.ItemsSource = folders;
            LocalFolderList.SelectedItem = folders.FirstOrDefault(f => f.Id == _localFolderId);
        }
        finally { _updatingLocalFolders = false; }
    }

    private void AddLocalFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string? name = FolderNamePrompt(Localizer.T("newLocalFolder"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var folder = _localMailStore.CreateFolder(name);
            RefreshLocalFolders();
            NavigateToLocalFolder(folder.Id);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void LocalFolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLocalFolders || LocalFolderList.SelectedItem is not LocalMailFolder folder ||
            folder.Id == _localFolderId) return;
        NavigateToLocalFolder(folder.Id);
    }

    private void LocalFolderItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: LocalMailFolder folder } row) return;
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("MailMessageContextMenuStyle"),
            PlacementTarget = row, Placement = PlacementMode.MousePoint
        };
        AddAction(menu, "renameLocalFolder", () =>
        {
            string? name = FolderNamePrompt(Localizer.T("renameLocalFolder"), folder.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                _localMailStore.RenameFolder(folder.Id, name);
                RefreshLocalFolders();
                FolderTitle.Text = CurrentFolderLabel();
            }
            catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        });
        AddAction(menu, "deleteLocalFolder", async () =>
        {
            if (MessageBox.Show(this, string.Format(Localizer.T("deleteLocalFolderConfirm"), folder.Name),
                    Localizer.T("deleteLocalFolder"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                _localMailStore.DeleteFolder(folder.Id);
                if (_localFolderId == folder.Id)
                {
                    ResetMailbox();
                    await RefreshAsync(true);
                }
                RefreshLocalFolders();
            }
            catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        });
        row.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void NavigateToLocalFolder(string folderId)
    {
        _mailLoadCts?.Cancel();
        _olderLoadCts?.Cancel();
        _searchCts?.Cancel();
        _localFolderId = folderId;
        _searchResults = null;
        SearchBox.Clear();
        _calendarLoaded = false;
        _updatingFolders = true;
        FolderList.SelectedItem = null;
        _updatingFolders = false;
        RefreshLocalFolders();
        ShowInbox();
        RefreshLocalMessages();
    }

    private void RefreshLocalMessages()
    {
        if (_localFolderId is null) return;
        try
        {
            _messages = _localMailStore.ListMessages(_localFolderId).Select(record => record.ToMailItem()).ToList();
            _folderTotalCount = _messages.Count;
            _nextOlderSequence = 0;
            _calendarLoaded = false;
            string query = SearchBox.Text.Trim();
            _searchResults = query.Length == 0 ? null : _messages.Where(item =>
                item.Subject.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.From.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.To.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Cc.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            FolderTitle.Text = CurrentFolderLabel();
            FilterMessages();
            StatusLabel.Text = MessageCount(_messages.Count);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task CopyToLocalFolderAsync(IReadOnlyList<MailItem> messages, string sourceFolder,
        MailAccount sourceAccount, string folderId)
    {
        if (_offlineMode || _password is null)
        {
            StatusLabel.Text = Localizer.T("offlineStatus");
            return;
        }
        int copied = 0;
        string password = _password;
        try
        {
            StatusLabel.Text = Localizer.T("copyingLocalMessages");
            await using var session = await ImapSession.ConnectAsync(sourceAccount, password);
            foreach (var message in messages)
            {
                byte[] raw = await session.LoadRawMessageAsync(sourceFolder, message.Uid);
                await Task.Run(() => _localMailStore.CopyMessage(folderId, sourceAccount.Email,
                    sourceFolder, message, raw));
                copied++;
            }
            StatusLabel.Text = string.Format(Localizer.T("copiedLocalMessages"), copied);
            if (_localFolderId == folderId) RefreshLocalMessages();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = copied > 0
                ? string.Format(Localizer.T("partialLocalCopy"), copied, ex.Message)
                : Localizer.T("failed") + ": " + ex.Message;
        }
    }

    private void DeleteLocalMessage(MailItem message)
    {
        if (MessageBox.Show(this, Localizer.T("deleteLocalMessageConfirm"),
                Localizer.T("deleteLocalMessage"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _localMailStore.DeleteMessage(message.LocalId);
            RefreshLocalMessages();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void SetLocalRead(MailItem message, bool read)
    {
        try
        {
            _localMailStore.SetRead(message.LocalId, read);
            foreach (MailItem item in _messages.Concat(_searchResults ?? []))
                if (item.LocalId == message.LocalId) item.IsRead = read;
            message.IsRead = read;
            if (_unreadOnly) FilterMessages();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }
}

public sealed record LocalMailDragPayload(MailAccount Account, string SourceFolder,
    IReadOnlyList<MailItem> Messages)
{
    public static LocalMailDragPayload? Create(MailAccount account, string sourceFolder,
        MailItem candidate, IEnumerable<MailItem> selected)
    {
        if (candidate.LocalId.Length > 0) return null;
        var messages = selected.Where(item => item.LocalId.Length == 0).Distinct().ToList();
        return messages.Contains(candidate) ? new LocalMailDragPayload(account, sourceFolder, messages) : null;
    }
}
