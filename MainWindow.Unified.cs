using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mailcario;

public partial class MainWindow
{
    private readonly Dictionary<string, int> _unifiedInboxCursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Email, string Folder), int> _unifiedSentCursors = [];
    private IReadOnlyList<MailItem> _unifiedSentCandidates = [];
    private bool _preferUnifiedSentPage;

    private void StampMessages(IEnumerable<MailItem> messages, string email, string folder,
        bool sent = false, bool showContext = false)
    {
        foreach (MailItem item in messages)
        {
            item.AccountEmail = email;
            item.FolderName = folder;
            item.IsSent = sent;
            item.ShowAccountContext = showContext;
        }
        if (!SkipContactFolder(new MailFolder { ServerName = folder, Delimiter = _folderDelimiter, IsSent = sent }))
            try { ContactStore.RememberFromMessages(messages, _accounts.Select(account => account.Email)); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
    }

    private MailAccount? AccountFor(MailItem item) => _accounts.FirstOrDefault(account =>
        account.Email.Equals(item.AccountEmail.Length > 0 ? item.AccountEmail : _account?.Email,
            StringComparison.OrdinalIgnoreCase));

    private string FolderFor(MailItem item) => item.FolderName.Length > 0 ? item.FolderName : _folder;

    private async void UnifiedButton_Click(object sender, RoutedEventArgs e)
    {
        _unifiedMode = true;
        _localFolderId = null;
        _updatingFolders = true;
        FolderList.SelectedItem = null;
        _updatingFolders = false;
        _updatingLocalFolders = true;
        LocalFolderList.SelectedItem = null;
        _updatingLocalFolders = false;
        _searchCts?.Cancel();
        _mailLoadCts?.Cancel();
        _searchResults = null;
        SearchBox.Clear();
        SearchHint.Text = Localizer.T("searchEverywhere");
        SearchBox.ToolTip = SearchButton.ToolTip = Localizer.T("searchEverywhereHelp");
        _calendarLoaded = false;
        ShowInbox();
        FolderTitle.Text = CurrentFolderLabel();
        await RefreshUnifiedAsync();
    }

    private async Task RefreshUnifiedAsync(bool preserveLoaded = false)
    {
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        _mailLoadCts?.Cancel();
        _olderLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _mailLoadCts = cts;
        RefreshButton.IsEnabled = false;
        BeginSync(Localizer.T("allInboxes"));
        var combined = new List<MailItem>();
        var errors = new List<string>();
        var previousInbox = preserveLoaded ? _messages.Where(item => !item.IsSent).ToList() : [];
        var previousSent = preserveLoaded ? _unifiedSentCandidates : [];
        var previousInboxCursors = preserveLoaded
            ? new Dictionary<string, int>(_unifiedInboxCursors, StringComparer.OrdinalIgnoreCase) :
              new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousSentCursors = preserveLoaded
            ? new Dictionary<(string Email, string Folder), int>(_unifiedSentCursors) : [];
        bool previousPreference = _preferUnifiedSentPage;
        _unifiedInboxCursors.Clear();
        _unifiedSentCursors.Clear();
        _unifiedSentCandidates = [];
        _preferUnifiedSentPage = preserveLoaded && previousPreference;
        try
        {
            foreach (MailAccount account in _accounts)
            {
                cts.Token.ThrowIfCancellationRequested();
                string? password = AccountStore.LoadPassword(account.Email);
                if (password is null) { errors.Add(account.Email); continue; }
                try
                {
                    await using var session = await ImapSession.ConnectAsync(account, password, cts.Token);
                    MailPage page = await session.LoadPageAsync("INBOX", token: cts.Token);
                    StampMessages(page.Messages, account.Email, "INBOX", showContext: true);
                    IReadOnlyList<MailItem> inbox = page.Messages;
                    try { inbox = await MailRuleStore.ApplyAsync(session, account.Email, "INBOX", inbox, cts.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { errors.Add(account.Email + " · " + Localizer.T("rules") + ": " + ex.Message); }
                    if (preserveLoaded && page.Messages.Count > 0)
                        inbox = MergeRefreshedPage(inbox, page.Messages, previousInbox.Where(item =>
                            item.AccountEmail.Equals(account.Email, StringComparison.OrdinalIgnoreCase)).ToList());
                    combined.AddRange(inbox);
                    _unifiedInboxCursors[account.Email] = previousInboxCursors.TryGetValue(account.Email, out int oldInboxCursor)
                        ? Math.Min(oldInboxCursor, page.NextSequence) : page.NextSequence;
                    SetAccountConnected(account.Email, true);
                    SetInboxUnreadCount(account.Email, await session.GetUnreadCountAsync("INBOX", cts.Token));
                    if (_settings.ConversationView && (inbox.Count > 0 || page.NextSequence > 0))
                    {
                        var sent = (await session.ListFoldersAsync(cts.Token)).FirstOrDefault(folder => folder.CanSelect && folder.IsSent);
                        if (sent is not null)
                        {
                            MailPage sentPage = await session.LoadPageAsync(sent.ServerName, token: cts.Token, sentFolder: true);
                            StampMessages(sentPage.Messages, account.Email, sent.ServerName, true, true);
                            var sentKey = (account.Email, sent.ServerName);
                            IReadOnlyList<MailItem> candidates = preserveLoaded && sentPage.Messages.Count > 0
                                ? MergeRefreshedPage(sentPage.Messages, sentPage.Messages,
                                    previousSent.Where(item => item.AccountEmail.Equals(account.Email,
                                        StringComparison.OrdinalIgnoreCase) && item.FolderName == sent.ServerName).ToList())
                                : sentPage.Messages;
                            _unifiedSentCursors[sentKey] = previousSentCursors.TryGetValue(sentKey, out int oldSentCursor)
                                ? Math.Min(oldSentCursor, sentPage.NextSequence) : sentPage.NextSequence;
                            _unifiedSentCandidates = _unifiedSentCandidates.Concat(candidates).ToList();
                            combined.AddRange(ConversationSupport.RelatedSent(inbox, candidates));
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(account.Email + ": " + ex.Message); SetAccountConnected(account.Email, false, ex.Message); }
            }
            if (!_unifiedMode || cts.IsCancellationRequested) return;
            _messages = combined;
            _folderTotalCount = 0;
            _searchTotalCount = 0;
            _searchResults = null;
            FilterMessages();
            FolderTitle.Text = CurrentFolderLabel();
            StatusLabel.Text = errors.Count == 0 ? MessageCount(combined.Count) :
                MessageCount(combined.Count) + " · " + string.Join("; ", errors);
            if (SearchBox.Text.Trim().Length > 0) await RunGlobalSearchAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cts.IsCancellationRequested) StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        finally
        {
            if (_mailLoadCts == cts) { _mailLoadCts = null; RefreshButton.IsEnabled = true; UpdatePaginationUi(); }
            cts.Dispose();
            EndSync();
        }
    }

    private async Task RunGlobalSearchAsync()
    {
        string query = SearchBox.Text.Trim();
        _searchCts?.Cancel();
        if (query.Length == 0)
        {
            _globalSearchCursors.Clear();
            _searchResults = null;
            FilterMessages();
            return;
        }
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        SearchButton.IsEnabled = false;
        StatusLabel.Text = Localizer.T("searchingEverywhere");
        var results = new List<MailItem>();
        var errors = new List<string>();
        _globalSearchCursors.Clear();
        try
        {
            foreach (MailAccount account in _accounts)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (_offlineMode) continue;
                string? password = AccountStore.LoadPassword(account.Email);
                if (password is null) { errors.Add(account.Email); continue; }
                try
                {
                    await using var session = await ImapSession.ConnectAsync(account, password, cts.Token);
                    foreach (MailFolder folder in await session.ListFoldersAsync(cts.Token))
                    {
                        if (!folder.CanSelect) continue;
                        try
                        {
                            MailSearchPage page = await session.SearchPageAsync(folder.ServerName, query,
                                token: cts.Token, sentFolder: folder.IsSent);
                            StampMessages(page.Messages, account.Email, folder.DisplayName, folder.IsSent, true);
                            // The IMAP name is required for follow-up actions, not the display label.
                            foreach (MailItem item in page.Messages) item.FolderName = folder.ServerName;
                            results.AddRange(page.Messages);
                            _globalSearchCursors[(account.Email, folder.ServerName)] = page.NextIndex;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { errors.Add(account.Email + "/" + folder.DisplayName + ": " + ex.Message); }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(account.Email + ": " + ex.Message); }
            }
            var localResults = await Task.Run(() =>
            {
                var found = new List<MailItem>();
                foreach (LocalMailFolder local in _localMailStore.ListFolders())
                    foreach (MailItem item in _localMailStore.ListMessages(local.Id).Select(record => record.ToMailItem()))
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        if (!MatchesGlobalLocalQuery(item, query))
                        {
                            try
                            {
                                MailContent content = _localMailStore.LoadMessage(item.LocalId);
                                if (!content.PlainText.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                                    !content.HtmlText.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
                            }
                            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException)
                            { continue; }
                        }
                        item.FolderName = local.Name;
                        item.ShowAccountContext = true;
                        found.Add(item);
                    }
                return found;
            }, cts.Token);
            results.AddRange(localResults);
            if (cts.IsCancellationRequested || !_unifiedMode || SearchBox.Text.Trim() != query) return;
            _searchResults = results;
            _loadedSearchQuery = query;
            FilterMessages();
            StatusLabel.Text = MessageCount(results.Count) + (errors.Count == 0 ? "" : " · " + string.Join("; ", errors));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cts.IsCancellationRequested) StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        finally
        {
            if (_searchCts == cts) { _searchCts = null; SearchButton.IsEnabled = true; UpdatePaginationUi(); }
            cts.Dispose();
        }
    }

    private static bool MatchesGlobalLocalQuery(MailItem item, string query) =>
        (item.Subject + " " + item.From + " " + item.To + " " + item.Cc)
        .Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private async Task LoadOlderGlobalSearchAsync()
    {
        if (_olderLoadCts is not null || _mailLoadCts is not null || _offlineMode) return;
        string query = SearchBox.Text.Trim();
        bool searching = query.Length > 0;
        bool loadSent = !searching && _settings.ConversationView &&
            _unifiedSentCursors.Values.Any(value => value > 0) &&
            (_preferUnifiedSentPage || !_unifiedInboxCursors.Values.Any(value => value > 0));
        var next = searching ? _globalSearchCursors.FirstOrDefault(kv => kv.Value > 0) :
            loadSent ? _unifiedSentCursors.FirstOrDefault(kv => kv.Value > 0) :
            _unifiedInboxCursors.Select(kv => new KeyValuePair<(string Email, string Folder), int>(
                (kv.Key, "INBOX"), kv.Value)).FirstOrDefault(kv => kv.Value > 0);
        if (next.Value < 1) return;
        MailAccount? account = _accounts.FirstOrDefault(a => a.Email.Equals(next.Key.Item1, StringComparison.OrdinalIgnoreCase));
        string? password = account is null ? null : AccountStore.LoadPassword(account.Email);
        if (account is null || password is null) return;
        var cts = new CancellationTokenSource();
        _olderLoadCts = cts;
        LoadOlderButton.IsEnabled = false;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password, cts.Token);
            IReadOnlyList<MailItem> pageItems;
            bool isSent = loadSent;
            int cursor;
            if (searching)
            {
                isSent = (await session.ListFoldersAsync(cts.Token)).Any(folder => folder.IsSent &&
                    folder.ServerName.Equals(next.Key.Item2, StringComparison.OrdinalIgnoreCase));
                MailSearchPage page = await session.SearchPageAsync(next.Key.Item2, query, next.Value,
                    token: cts.Token, sentFolder: isSent);
                pageItems = page.Messages; cursor = page.NextIndex;
            }
            else
            {
                MailPage page = await session.LoadPageAsync(next.Key.Item2, next.Value,
                    token: cts.Token, sentFolder: loadSent);
                pageItems = page.Messages; cursor = page.NextSequence;
            }
            StampMessages(pageItems, account.Email, next.Key.Item2, isSent, true);
            if (!searching && !loadSent)
            {
                try { pageItems = await MailRuleStore.ApplyAsync(session, account.Email, "INBOX", pageItems, cts.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { StatusLabel.Text = Localizer.T("rules") + ": " + ex.Message; }
            }
            if (cts.IsCancellationRequested || !_unifiedMode || SearchBox.Text.Trim() != query) return;
            if (searching)
            {
                _globalSearchCursors[next.Key] = cursor;
                var current = _searchResults ?? [];
                var known = current.Select(item => (item.AccountEmail, item.FolderName, item.Uid)).ToHashSet();
                _searchResults = current.Concat(pageItems.Where(item =>
                    known.Add((item.AccountEmail, item.FolderName, item.Uid)))).ToList();
            }
            else
            {
                if (loadSent)
                {
                    _unifiedSentCursors[next.Key] = cursor;
                    var knownSent = _unifiedSentCandidates.Select(item =>
                        (item.AccountEmail, item.FolderName, item.Uid)).ToHashSet();
                    _unifiedSentCandidates = _unifiedSentCandidates.Concat(pageItems.Where(item =>
                        knownSent.Add((item.AccountEmail, item.FolderName, item.Uid)))).ToList();
                }
                else _unifiedInboxCursors[account.Email] = cursor;
                _preferUnifiedSentPage = !loadSent;
                var inbox = _messages.Where(item => !item.IsSent).ToList();
                if (!loadSent)
                {
                    var knownInbox = inbox.Select(item => (item.AccountEmail, item.Uid)).ToHashSet();
                    inbox.AddRange(pageItems.Where(item => knownInbox.Add((item.AccountEmail, item.Uid))));
                }
                _messages = inbox.Concat(ConversationSupport.RelatedSent(inbox, _unifiedSentCandidates)).ToList();
            }
            SetAccountConnected(account.Email, true);
            FilterMessages();
            StatusLabel.Text = MessageCount(_visibleMessages.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        finally { _olderLoadCts = null; cts.Dispose(); UpdatePaginationUi(); }
    }

    private async void ConversationButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ConversationView = !_settings.ConversationView;
        _settings.Save();
        UpdateConversationButton();
        await RefreshAsync();
    }

    private void UpdateConversationButton()
    {
        ConversationButton.Content = Localizer.T("conversationView");
        ConversationButton.Foreground = (System.Windows.Media.Brush)FindResource(
            _settings.ConversationView ? "AccentDark" : "Muted");
    }

    private async Task SetMessageReadAsync(MailItem item, bool read)
    {
        if (item.LocalId.Length > 0) { SetLocalRead(item, read); return; }
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        MailAccount? account = AccountFor(item);
        string? password = account is null ? null : AccountStore.LoadPassword(account.Email);
        if (account is null || password is null) return;
        string folder = FolderFor(item);
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            await session.SetReadAsync(folder, item.Uid, read);
            foreach (MailItem message in _messages.Concat(_searchResults ?? []))
                if (message.Uid == item.Uid && message.AccountEmail.Equals(account.Email,
                    StringComparison.OrdinalIgnoreCase) && FolderFor(message) == folder) message.IsRead = read;
            item.IsRead = read;
            if (folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                SetInboxUnreadCount(account.Email, await session.GetUnreadCountAsync("INBOX"));
            FilterMessages();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && MessageList.IsKeyboardFocusWithin && _selected is { } selected)
        {
            if (selected.LocalId.Length > 0) DeleteLocalMessage(selected);
            else _ = MoveToSpecialFolderAsync(selected, "trash");
            e.Handled = true;
            return;
        }
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (!control) return;
        switch (e.Key)
        {
            case Key.N: OpenCompose(); e.Handled = true; break;
            case Key.E: SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; break;
            case Key.R when _selected is not null:
                _ = ComposeReplyAsync(_selected, shift); e.Handled = true; break;
            case Key.F when _selected is not null:
                _ = ComposeForwardAsync(_selected); e.Handled = true; break;
            case Key.Q when _selected is not null:
                _ = SetMessageReadAsync(_selected, true); e.Handled = true; break;
            case Key.U when _selected is not null:
                _ = SetMessageReadAsync(_selected, false); e.Handled = true; break;
        }
    }
}
