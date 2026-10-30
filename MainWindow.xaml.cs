using System.Collections.ObjectModel;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Mailcario;

public partial class MainWindow : Window
{
    private IReadOnlyList<MailAccount> _accounts = [];
    private MailAccount? _account;
    private string? _password;
    private readonly ObservableCollection<MailItem> _visibleMessages = [];
    private IReadOnlyList<MailItem> _messages = [];
    private IReadOnlyList<MailItem>? _searchResults;
    private MailItem? _selected;
    private MailContent? _selectedContent;
    private UnsubscribeAction? _unsubscribeAction;
    private CalendarInvite? _invite;
    private string _folder = "INBOX";
    private char? _folderDelimiter = '/';
    private bool _updatingFolders;
    private bool _updatingAccounts;
    private bool _calendarLoaded;
    private bool _calendarMode;
    private int _blockedImages;
    private int _trackingPixels;
    private IReadOnlyList<Uri> _downloadableImages = [];
    private IReadOnlyList<string> _availableInlineImages = [];
    private ConcurrentDictionary<string, BitmapSource> _loadedImages = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, string> _loadedImageData = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attemptedImages = new(StringComparer.Ordinal);
    private GridLength _savedMessageWidth = new(510);
    private CancellationTokenSource? _mailLoadCts;
    private CancellationTokenSource? _olderLoadCts;
    private int _nextOlderSequence;
    private int _folderTotalCount;
    private string? _loadedFolderAccount;
    private string? _loadedFolderName;
    private int _nextOlderSearchIndex;
    private int _searchTotalCount;
    private string? _loadedSearchQuery;
    private CancellationTokenSource? _searchCts;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private CancellationTokenSource? _imageLoadCts;
    private Task? _webInitTask;
    private string _currentHtml = "";
    private int _webDocumentId;
    private ClientSettings _settings = ClientSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        AccountPaneRow.Height = new GridLength(_settings.AccountPaneHeight);
        WindowTheme.Attach(this);
        InitializeNotifications();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await RunSearchAsync(); };
        InitializeRibbon();
        MessageList.ItemsSource = _visibleMessages;
        ApplyLanguage();
        Localizer.Changed += ApplyLanguage;
        ThemeManager.Changed += ApplyTheme;
        Closed += (_, _) =>
        {
            Localizer.Changed -= ApplyLanguage;
            ThemeManager.Changed -= ApplyTheme;
            _mailLoadCts?.Cancel();
            _olderLoadCts?.Cancel();
            _searchCts?.Cancel();
            _searchTimer.Stop();
            _imageLoadCts?.Cancel();
        };
        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
    }

    private void ApplyTheme(bool _)
    {
        ApplyLanguage();
        ApplyRibbonPreferences();
        if (HtmlBodyBox.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(_selectedContent?.HtmlText))
            HtmlBodyBox.Document = SafeHtmlRenderer.Render(_selectedContent.HtmlText, _loadedImages).Document;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _accounts = AccountStore.LoadAccounts();
        string? active = AccountStore.LoadActiveEmail();
        _account = _accounts.FirstOrDefault(x => x.Email.Equals(active, StringComparison.OrdinalIgnoreCase)) ?? _accounts.FirstOrDefault();
        _password = _account is null ? null : AccountStore.LoadPassword(_account.Email);
        RestoreFolderView();
        RefreshAccountList();
        UpdateConnectionStatus();
        if (_account is null || _password is null) ShowAccountWindow(_account);
        if (_account is not null && _password is not null) await RefreshAsync(true);
        ConfigureMailTimer();
        await PollAllAccountsAsync();
    }

    private void ApplyLanguage()
    {
        ApplyRibbonLanguage();
        AccountLabel.Text = Localizer.T("accounts").ToUpperInvariant();
        AddAccountButton.Content = "+  " + Localizer.T("addAccount");
        AccountPaneSplitter.ToolTip = Localizer.T("resizeAccounts");
        NavigationLabel.Text = Localizer.T("favorites");
        InboxButtonLabel.Text = Localizer.T("inbox");
        CalendarButtonLabel.Text = Localizer.T("calendar");
        NewButtonLabel.Text = Localizer.T("newMail");
        RefreshButtonLabel.Text = Localizer.T("refresh");
        AccountButtonLabel.Text = Localizer.T("account");
        SearchBox.ToolTip = SearchButton.ToolTip = Localizer.T("searchHelp");
        ClearSearchButton.ToolTip = Localizer.T("clearSearch");
        SearchHint.Text = Localizer.T("search");
        FolderOptionsButton.ToolTip = Localizer.T("chooseFolders");
        ApplyAccountFolderState();
        UpdateConnectionStatus();
        LinkPreviewCaption.Text = Localizer.T("linkTarget");
        RailMailButton.ToolTip = Localizer.T("inbox");
        RailCalendarButton.ToolTip = Localizer.T("calendar");
        RailSettingsButton.ToolTip = Localizer.T("settings");
        UpdateMessageFilterButtons();
        ImagePrivacyHint.Text = Localizer.T("imagePrivacyHint");
        EmailLabel.Text = _account?.Email ?? Localizer.T("account");
        FolderTitle.Text = CurrentFolderLabel();
        FolderList.Items.Refresh();
        CalendarTitle.Text = Localizer.T("calendar");
        CalendarHint.Text = Localizer.T("localCalendar");
        ReplyButtonLabel.Text = Localizer.T("reply");
        ForwardButtonLabel.Text = Localizer.T("forward");
        InviteHeading.Text = Localizer.T("invite");
        UnsubscribeLabel.Text = Localizer.T("newsletterNotice");
        UnsubscribeButton.Content = Localizer.T("unsubscribe");
        ConnectionStatusButton.ToolTip = Localizer.T("connectionDetails");
        AcceptButton.Content = Localizer.T("accept");
        TentativeButton.Content = Localizer.T("tentative");
        DeclineButton.Content = Localizer.T("decline");
        if (_selected is null) EmptyTitle.Text = Localizer.T("selectMail");
        EmptyDescription.Text = Localizer.T("emptyDescription");
        UpdatePaginationUi();
        MessageEmptyLabel.Text = Localizer.T("noMessages");
        UpdateImageNotices();
        if (_account is null) StatusLabel.Text = Localizer.T("statusConnect");
        FrenchButton.Background = Localizer.Language == "fr" ? (System.Windows.Media.Brush)FindResource("Accent") : System.Windows.Media.Brushes.Transparent;
        FrenchButton.Foreground = Localizer.Language == "fr" ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("Muted");
        EnglishButton.Background = Localizer.Language == "en" ? (System.Windows.Media.Brush)FindResource("Accent") : System.Windows.Media.Brushes.Transparent;
        EnglishButton.Foreground = Localizer.Language == "en" ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("Muted");
        UpdateTrayLanguage();
        if (TaskbarItemInfo is not null)
            TaskbarItemInfo.Description = _unreadInboxTotal > 0
                ? string.Format(Localizer.T("unreadInboxCount"), _unreadInboxTotal) : "Mailcario";
    }

    private string CurrentFolderLabel() => new MailFolder { ServerName = _folder, Delimiter = _folderDelimiter,
        IsSent = _allFolders.Any(folder => folder.IsSent && folder.ServerName.Equals(_folder,
            StringComparison.OrdinalIgnoreCase)) }.DisplayName;

    private void RefreshAccountList()
    {
        _updatingAccounts = true;
        AccountList.ItemsSource = _accounts;
        AccountList.SelectedItem = _accounts.FirstOrDefault(a => a.Email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase));
        AccountLabel.Visibility = AccountList.Visibility = Visibility.Visible;
        AddAccountButton.Visibility = Visibility.Visible;
        EmailLabel.Text = _account?.Email ?? Localizer.T("account");
        FileAccounts.ItemsSource = _accounts;
        FileAccounts.SelectedItem = _account;
        UpdateRibbonSelection();
        UpdateConnectionStatus();
        _updatingAccounts = false;
    }

    private void AccountPaneSplitter_DragCompleted(object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.AccountPaneHeight = AccountPaneRow.ActualHeight;
        try { _settings.Save(); }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private bool ShowAccountWindow(MailAccount? existing)
    {
        var window = new AccountWindow(existing) { Owner = this };
        if (window.ShowDialog() != true || window.Account is null) return false;
        _account = window.Account;
        _password = window.Password;
        _accounts = AccountStore.LoadAccounts();
        ResetMailbox();
        RefreshAccountList();
        return true;
    }

    private void ResetMailbox()
    {
        _mailLoadCts?.Cancel();
        _olderLoadCts?.Cancel();
        _searchCts?.Cancel();
        CancelImageLoad();
        _folder = "INBOX";
        _folderDelimiter = '/';
        _calendarLoaded = false;
        _messages = [];
        _nextOlderSequence = 0;
        _folderTotalCount = 0;
        _loadedFolderAccount = _loadedFolderName = null;
        _searchResults = null;
        _nextOlderSearchIndex = _searchTotalCount = 0;
        _loadedSearchQuery = null;
        SearchBox.Clear();
        _visibleMessages.Clear();
        _allFolders = [];
        RestoreFolderView();
        ApplyFolderList();
        EmailLabel.Text = _account?.Email ?? Localizer.T("account");
        FolderTitle.Text = CurrentFolderLabel();
        ShowInbox();
    }

    private async Task RefreshAsync(bool includeFolders = false, bool preserveLoaded = false)
    {
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        if (_account is null || _password is null) return;
        _mailLoadCts?.Cancel();
        _olderLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _mailLoadCts = cts;
        var account = _account;
        string password = _password;
        string folder = _folder;
        StatusLabel.Text = Localizer.T("loading");
        BeginSync(account.Email);
        RefreshButton.IsEnabled = false;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password, cts.Token);
            if (includeFolders)
            {
                var folders = await session.ListFoldersAsync(cts.Token);
                if (cts.IsCancellationRequested) return;
                _allFolders = folders;
                var selectedFolder = folders.FirstOrDefault(x => x.ServerName.Equals(folder, StringComparison.OrdinalIgnoreCase)) ?? folders.FirstOrDefault();
                _folderDelimiter = selectedFolder?.Delimiter;
                ApplyFolderList();
            }
            bool sentFolder = _allFolders.Any(value => value.IsSent && value.ServerName.Equals(folder,
                StringComparison.OrdinalIgnoreCase));
            MailPage page = await session.LoadPageAsync(folder, token: cts.Token, sentFolder: sentFolder);
            IReadOnlyList<MailItem> messages = page.Messages;
            string? ruleError = null;
            try { messages = await MailRuleStore.ApplyAsync(session, account.Email, folder, messages, cts.Token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { ruleError = ex.Message; }
            if (cts.IsCancellationRequested || _account != account || _folder != folder) return;
            bool sameFolder = preserveLoaded && string.Equals(_loadedFolderAccount, account.Email,
                StringComparison.OrdinalIgnoreCase) && string.Equals(_loadedFolderName, folder,
                StringComparison.OrdinalIgnoreCase);
            if (sameFolder && _messages.Count > 0 && page.Messages.Count > 0)
                messages = MergeRefreshedPage(messages, page.Messages, _messages);
            _messages = messages;
            _folderTotalCount = page.TotalCount;
            _nextOlderSequence = sameFolder
                ? Math.Min(_nextOlderSequence, page.NextSequence) : page.NextSequence;
            _loadedFolderAccount = account.Email;
            _loadedFolderName = folder;
            SetAccountConnected(account.Email, true);
            if (folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                SetInboxUnreadCount(account.Email, await session.GetUnreadCountAsync("INBOX", cts.Token));
            _searchResults = null;
            FilterMessages();
            MailItem? newMail = ObserveLoadedInbox();
            if (newMail is not null) await NotifyNewMailAsync(session, account.Email, newMail, cts.Token);
            FolderTitle.Text = CurrentFolderLabel();
            StatusLabel.Text = ruleError is not null ? Localizer.T("rules") + ": " + ruleError :
                _messages.Count == 0 ? Localizer.T("noMessages") : $"{_messages.Count} · {Localizer.T("ready")}";
            _calendarLoaded = false;
            if (SearchBox.Text.Trim().Length > 0) await RunSearchAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cts.IsCancellationRequested) { SetAccountConnected(account.Email, false, ex.Message); StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; } }
        finally
        {
            if (_mailLoadCts == cts)
            {
                _mailLoadCts = null;
                RefreshButton.IsEnabled = true;
                UpdatePaginationUi();
            }
            _updatingFolders = false;
            cts.Dispose();
            EndSync();
        }
    }

    internal static IReadOnlyList<MailItem> MergeRefreshedPage(IReadOnlyList<MailItem> fresh,
        IReadOnlyList<MailItem> fetched, IReadOnlyList<MailItem> previous)
    {
        if (fetched.Count == 0) return fresh;
        uint oldestFreshUid = fetched.Min(item => item.Uid);
        var freshUids = fresh.Select(item => item.Uid).ToHashSet();
        return fresh.Concat(previous.Where(item => item.Uid < oldestFreshUid &&
            !freshUids.Contains(item.Uid))).ToList();
    }

    private async Task LoadOlderMessagesAsync()
    {
        string? searchQuery = _loadedSearchQuery;
        bool searchPage = _searchResults is not null && searchQuery == SearchBox.Text.Trim();
        int before = searchPage ? _nextOlderSearchIndex : _nextOlderSequence;
        if (_olderLoadCts is not null || _mailLoadCts is not null || before < 1 ||
            (!searchPage && SearchBox.Text.Trim().Length > 0) ||
            _offlineMode || _account is null || _password is null) return;
        var cts = new CancellationTokenSource();
        _olderLoadCts = cts;
        var account = _account;
        string folder = _folder;
        LoadOlderButton.IsEnabled = false;
        LoadOlderButton.Content = Localizer.T("loadingOlderMessages");
        BeginSync(account.Email);
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, _password, cts.Token);
            bool sentFolder = _allFolders.Any(value => value.IsSent && value.ServerName.Equals(folder,
                StringComparison.OrdinalIgnoreCase));
            if (searchPage)
            {
                MailSearchPage page = await session.SearchPageAsync(folder, searchQuery!, before,
                    token: cts.Token, sentFolder: sentFolder);
                if (cts.IsCancellationRequested || _account != account || _folder != folder ||
                    _loadedSearchQuery != searchQuery || SearchBox.Text.Trim() != searchQuery) return;
                var currentResults = _searchResults!;
                var knownUids = currentResults.Select(item => item.Uid).ToHashSet();
                _searchResults = currentResults.Concat(page.Messages.Where(item => knownUids.Add(item.Uid))).ToList();
                _searchTotalCount = page.TotalCount;
                _nextOlderSearchIndex = page.NextIndex;
            }
            else
            {
                MailPage page = await session.LoadPageAsync(folder, before, token: cts.Token, sentFolder: sentFolder);
                IReadOnlyList<MailItem> older = page.Messages;
                try { older = await MailRuleStore.ApplyAsync(session, account.Email, folder, older, cts.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { StatusLabel.Text = Localizer.T("rules") + ": " + ex.Message; }
                if (cts.IsCancellationRequested || _account != account || _folder != folder) return;
                var knownUids = _messages.Select(item => item.Uid).ToHashSet();
                _messages = _messages.Concat(older.Where(item => knownUids.Add(item.Uid))).ToList();
                _folderTotalCount = page.TotalCount;
                _nextOlderSequence = page.NextSequence;
            }
            SetAccountConnected(account.Email, true);
            FilterMessages();
            StatusLabel.Text = $"{(searchPage ? _searchResults!.Count : _messages.Count)} · {Localizer.T("ready")}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
                StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message;
        }
        finally
        {
            if (_olderLoadCts == cts)
            {
                _olderLoadCts = null;
                UpdatePaginationUi();
            }
            cts.Dispose();
            EndSync();
        }
    }

    private async void LoadOlderButton_Click(object sender, RoutedEventArgs e) => await LoadOlderMessagesAsync();

    private async void MessageList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer || e.VerticalChange <= 0 ||
            e.VerticalOffset < e.ExtentHeight - e.ViewportHeight - 3) return;
        await LoadOlderMessagesAsync();
    }

    private void UpdatePaginationUi()
    {
        CountLabel.Text = MessageCount(_visibleMessages.Count);
        if (_searchResults is not null && _loadedSearchQuery == SearchBox.Text.Trim() &&
            _searchTotalCount > _searchResults.Count)
            CountLabel.Text += $" · {_searchResults.Count}/{_searchTotalCount}";
        else if (_folderTotalCount > _messages.Count && _searchResults is null)
            CountLabel.Text += $" · {_messages.Count}/{_folderTotalCount}";
        bool canLoad = !_offlineMode && (_searchResults is not null
            ? _nextOlderSearchIndex > 0 && _loadedSearchQuery == SearchBox.Text.Trim()
            : _nextOlderSequence > 0 && SearchBox.Text.Trim().Length == 0);
        LoadOlderButton.Visibility = canLoad ? Visibility.Visible : Visibility.Collapsed;
        LoadOlderButton.IsEnabled = canLoad && _olderLoadCts is null && _mailLoadCts is null;
        if (_olderLoadCts is null) LoadOlderButton.Content = Localizer.T("loadOlderMessages");
    }

    private async Task RunSearchAsync()
    {
        _searchTimer.Stop();
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        string query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            _searchResults = null;
            _nextOlderSearchIndex = _searchTotalCount = 0;
            _loadedSearchQuery = null;
            FilterMessages();
            return;
        }
        if (_account is null || _password is null) return;
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var account = _account;
        string folder = _folder;
        StatusLabel.Text = Localizer.T("searching");
        SearchButton.IsEnabled = false;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, _password, cts.Token);
            bool sentFolder = _allFolders.Any(value => value.IsSent && value.ServerName.Equals(folder,
                StringComparison.OrdinalIgnoreCase));
            MailSearchPage page = await session.SearchPageAsync(folder, query, token: cts.Token, sentFolder: sentFolder);
            if (cts.IsCancellationRequested || _account != account || _folder != folder || SearchBox.Text.Trim() != query) return;
            _searchResults = page.Messages;
            _searchTotalCount = page.TotalCount;
            _nextOlderSearchIndex = page.NextIndex;
            _loadedSearchQuery = query;
            FilterMessages();
            StatusLabel.Text = MessageCount(page.TotalCount);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cts.IsCancellationRequested) StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        finally
        {
            if (_searchCts == cts)
            {
                _searchCts = null;
                SearchButton.IsEnabled = true;
            }
            cts.Dispose();
        }
    }

    private void FilterMessages()
    {
        IEnumerable<MailItem> source = _searchResults ?? _messages;
        if (_unreadOnly) source = source.Where(x => !x.IsRead);
        var currentByUid = _visibleMessages.GroupBy(item => item.Uid)
            .ToDictionary(group => group.Key, group => group.First());
        var desired = SortMessages(source).Select(item =>
        {
            if (!currentByUid.TryGetValue(item.Uid, out MailItem? current)) return item;
            current.IsRead = item.IsRead;
            current.IsFlagged = item.IsFlagged;
            return current;
        }).ToList();
        for (int index = 0; index < desired.Count; index++)
        {
            if (index < _visibleMessages.Count && ReferenceEquals(_visibleMessages[index], desired[index])) continue;
            int existingIndex = _visibleMessages.IndexOf(desired[index]);
            if (existingIndex >= 0) _visibleMessages.Move(existingIndex, index);
            else _visibleMessages.Insert(index, desired[index]);
        }
        while (_visibleMessages.Count > desired.Count) _visibleMessages.RemoveAt(_visibleMessages.Count - 1);
        var view = CollectionViewSource.GetDefaultView(_visibleMessages);
        bool groupByDate = _settings.SortBy == "date";
        if (view.GroupDescriptions is { } groups)
        {
            if (!groupByDate) groups.Clear();
            else if (groups.Count == 0) groups.Add(new PropertyGroupDescription(nameof(MailItem.DayGroup)));
        }
        UpdatePaginationUi();
        MessageEmptyLabel.Visibility = _visibleMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IEnumerable<MailItem> SortMessages(IEnumerable<MailItem> source) => _settings.SortBy switch
        {
            "sender" => _settings.SortDescending ? source.OrderByDescending(x => x.SenderName, StringComparer.CurrentCultureIgnoreCase) : source.OrderBy(x => x.SenderName, StringComparer.CurrentCultureIgnoreCase),
            "subject" => _settings.SortDescending ? source.OrderByDescending(x => x.Subject, StringComparer.CurrentCultureIgnoreCase) : source.OrderBy(x => x.Subject, StringComparer.CurrentCultureIgnoreCase),
            _ => _settings.SortDescending ? source.OrderByDescending(x => x.Date) : source.OrderBy(x => x.Date)
        };

    private bool IsActiveInbox(string email) => !_calendarMode &&
        _folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase) &&
        email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase);

    private void InsertIncomingInboxMessage(MailItem incoming)
    {
        if (_messages.Any(message => message.Uid == incoming.Uid)) return;
        _messages = _messages.Prepend(incoming).ToList();
        if (_searchResults is not null || (_unreadOnly && incoming.IsRead)) return;
        int position = SortMessages(_visibleMessages.Append(incoming)).ToList()
            .FindIndex(message => ReferenceEquals(message, incoming));
        _visibleMessages.Insert(Math.Max(0, position), incoming);
        UpdatePaginationUi();
        MessageEmptyLabel.Visibility = Visibility.Collapsed;
    }

    private async void MessageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = MessageList.SelectedItem as MailItem;
        ClearLinkTarget();
        UpdateRibbonSelection();
        _selectedContent = null;
        _invite = null;
        CancelImageLoad();
        _loadedImages = new ConcurrentDictionary<string, BitmapSource>(StringComparer.Ordinal);
        _loadedImageData = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        _attemptedImages.Clear();
        _downloadableImages = [];
        _availableInlineImages = [];
        _blockedImages = 0;
        _trackingPixels = 0;
        _unsubscribeAction = null;
        UnsubscribePanel.Visibility = InvitePanel.Visibility = BlockedImagesPanel.Visibility = TrackingNoticePanel.Visibility = Visibility.Collapsed;
        HtmlBodyBox.Visibility = Visibility.Collapsed;
        HtmlWebView.Visibility = Visibility.Collapsed;
        PlainBodyScroller.Visibility = Visibility.Visible;
        ReplyButton.IsEnabled = ForwardButton.IsEnabled = _selected is not null;
        if (_selected is null)
        {
            ReaderEmptyState.Visibility = Visibility.Visible;
            ReaderContent.Visibility = Visibility.Collapsed;
            FromLabel.Text = SenderAddressLabel.Text = ToLabel.Text = BodyLabel.Text = AttachmentsLabel.Text = "";
            return;
        }
        ReaderEmptyState.Visibility = Visibility.Collapsed;
        ReaderContent.Visibility = Visibility.Visible;
        var item = _selected;
        var account = _account;
        string folder = _folder;
        SubjectLabel.Text = item.Subject;
        ReaderAvatar.Text = item.AvatarLetter;
        FromLabel.Text = item.SenderName;
        SenderAddressLabel.Text = item.FromAddress;
        ToLabel.Text = Localizer.T("to") + " : " + item.To + "  ·  " + item.DateLabel;
        BodyLabel.Text = Localizer.T("loading");
        AttachmentsLabel.Text = "";
        if (account is null || _password is null) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, _password);
            var content = await session.LoadMessageAsync(folder, item.Uid);
            if (_selected != item || _account != account || _folder != folder) return;
            _selectedContent = content;
            BodyLabel.Text = content.PlainText;
            _unsubscribeAction = content.Unsubscribe;
            UnsubscribePanel.Visibility = content.Unsubscribe is null ? Visibility.Collapsed : Visibility.Visible;
            UnsubscribeButton.ToolTip = content.Unsubscribe?.Url.AbsoluteUri;
            if (!item.IsRead)
            {
                try
                {
                    await session.SetReadAsync(folder, item.Uid, true);
                    if (folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                        SetInboxUnreadCount(account.Email, await session.GetUnreadCountAsync("INBOX"));
                    if (_account == account && _folder == folder)
                    {
                        foreach (var message in _messages.Concat(_searchResults ?? []))
                            if (message.Uid == item.Uid) message.IsRead = true;
                        item.IsRead = true;
                    }
                }
                catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
            }
            if (_selected != item || _account != account || _folder != folder) return;
            if (!string.IsNullOrWhiteSpace(content.HtmlText))
            {
                try
                {
                    var rendered = SafeHtmlRenderer.Render(content.HtmlText);
                    ApplyRenderedMail(rendered);
                    bool automaticImages = _settings.AutoLoadImages.TryGetValue(account.Email, out bool enabled) && enabled;
                    await ShowTemplateAsync(content.HtmlText, item);
                    if (automaticImages) await LoadImagesAsync();
                }
                catch (RegexMatchTimeoutException) { /* Plain text remains available. */ }
            }
            if (content.AttachmentNames.Count > 0)
                AttachmentsLabel.Text = Localizer.T("attachments") + ": " + string.Join(", ", content.AttachmentNames);
            _invite = CalendarSupport.Parse(content.CalendarText);
            if (_invite is not null)
            {
                InviteSummary.Text = _invite.Summary;
                InviteDate.Text = _invite.DateLabel + (string.IsNullOrEmpty(_invite.Location) ? "" : " · " + _invite.Location);
                InvitePanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { if (_selected == item) BodyLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void ApplyRenderedMail(SafeMailDocument rendered)
    {
        ClearLinkTarget();
        HtmlBodyBox.Document = rendered.Document;
        HtmlBodyBox.Visibility = Visibility.Visible;
        HtmlWebView.Visibility = Visibility.Collapsed;
        PlainBodyScroller.Visibility = Visibility.Collapsed;
        _blockedImages = rendered.BlockedImages;
        _trackingPixels = rendered.TrackingPixels;
        _downloadableImages = rendered.DownloadableImages;
        _availableInlineImages = rendered.InlineImageIds.Where(id =>
            _selectedContent?.InlineImages.ContainsKey(id) == true).ToArray();
        UpdateImageNotices();
    }

    private async Task ShowTemplateAsync(string html, MailItem item)
    {
        try
        {
            _webInitTask ??= InitializeWebViewAsync();
            await _webInitTask;
            if (_selected != item || HtmlWebView.CoreWebView2 is null) return;
            _currentHtml = HtmlTemplateRenderer.Build(html, _loadedImageData, _downloadableImages);
            _currentLinkTargets = HtmlTemplateRenderer.SafeLinkTargets(_currentHtml);
            ClearLinkTarget();
            HtmlWebView.CoreWebView2.Navigate("https://mailcario.invalid/message/" + (++_webDocumentId));
            HtmlWebView.Visibility = Visibility.Visible;
            HtmlBodyBox.Visibility = Visibility.Collapsed;
        }
        catch (Exception) { StatusLabel.Text = Localizer.T("htmlFallback"); }
    }

    private async Task InitializeWebViewAsync()
    {
        string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailcario", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        await HtmlWebView.EnsureCoreWebView2Async(environment);
        var core = HtmlWebView.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.StatusBarTextChanged += (_, _) => ShowLinkTarget(core.StatusBarText, true);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            bool document = IsInternalMailUrl(e.Request.Uri) && e.ResourceContext == CoreWebView2WebResourceContext.Document;
            byte[] bytes = document ? Encoding.UTF8.GetBytes(_currentHtml) : [];
            string headers = document
                ? "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'; object-src 'none'"
                : "Content-Type: text/plain\r\nCache-Control: no-store";
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(bytes),
                document ? 200 : 403, document ? "OK" : "Blocked", headers);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (IsInternalMailUrl(e.Uri) || e.Uri == "about:blank") return;
            e.Cancel = true;
            if (e.IsUserInitiated) OpenSafeExternalLink(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.IsUserInitiated) OpenSafeExternalLink(e.Uri);
        };
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
    }

    private static bool IsInternalMailUrl(string? uri) =>
        uri?.StartsWith("https://mailcario.invalid/message/", StringComparison.OrdinalIgnoreCase) == true;

    private void OpenSafeExternalLink(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 ||
            IsInternalMailUrl(value) || !_currentLinkTargets.Contains(uri.AbsoluteUri)) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void CancelImageLoad()
    {
        _imageLoadCts?.Cancel();
        _imageLoadCts = null;
        LoadImagesButton.IsEnabled = true;
        LoadImagesButton.Content = Localizer.T("loadImages");
    }

    private void UpdateImageNotices()
    {
        BlockedImagesPanel.Visibility = _blockedImages > 0 ? Visibility.Visible : Visibility.Collapsed;
        BlockedImagesLabel.Text = string.Format(Localizer.T("blockedImages"), _blockedImages);
        bool hasRemoteImages = _downloadableImages.Any(uri => !_loadedImages.ContainsKey(uri.AbsoluteUri));
        bool hasInlineImages = _availableInlineImages.Any(id => !_loadedImages.ContainsKey(id));
        LoadImagesButton.Visibility = hasRemoteImages || hasInlineImages ? Visibility.Visible : Visibility.Collapsed;
        ImagePrivacyHint.Visibility = hasRemoteImages ? Visibility.Visible : Visibility.Collapsed;
        if (_imageLoadCts is null) LoadImagesButton.Content = Localizer.T(hasRemoteImages && hasInlineImages
            ? "loadAllImages" : hasInlineImages ? "showEmbeddedImages" : "loadImages");
        TrackingNoticePanel.Visibility = _trackingPixels > 0 ? Visibility.Visible : Visibility.Collapsed;
        TrackingNoticeLabel.Text = string.Format(Localizer.T("trackingBlocked"), _trackingPixels);
    }

    private async void LoadImagesButton_Click(object sender, RoutedEventArgs e) => await LoadImagesAsync();

    private async Task LoadImagesAsync()
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_selectedContent?.HtmlText)) return;
        var item = _selected;
        MailContent content = _selectedContent;
        string html = content.HtmlText;
        var pending = _downloadableImages.Where(uri => !_loadedImages.ContainsKey(uri.AbsoluteUri) &&
            !_attemptedImages.Contains(uri.AbsoluteUri)).Take(20).ToList();
        if (pending.Count == 0)
        {
            _attemptedImages.Clear();
            pending = _downloadableImages.Where(uri => !_loadedImages.ContainsKey(uri.AbsoluteUri)).Take(20).ToList();
        }
        var pendingInline = _availableInlineImages.Where(id => !_loadedImages.ContainsKey(id)).Take(20).ToList();
        if (pending.Count == 0 && pendingInline.Count == 0) return;
        foreach (Uri uri in pending) _attemptedImages.Add(uri.AbsoluteUri);
        _imageLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _imageLoadCts = cts;
        var loaded = _loadedImages;
        var loadedData = _loadedImageData;
        LoadImagesButton.IsEnabled = false;
        LoadImagesButton.Content = Localizer.T("loadingImages");
        try
        {
            foreach (string id in pendingInline)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (!content.InlineImages.TryGetValue(id, out InlineMailImage? embedded)) continue;
                try
                {
                    RemoteMailImage? image = await Task.Run(() => RemoteImageLoader.LoadEmbedded(embedded), cts.Token);
                    if (image is not null)
                    {
                        loaded.TryAdd(id, image.Bitmap);
                        loadedData.TryAdd(id, image.DataUri);
                    }
                }
                catch (Exception) { /* Unsupported embedded images remain blocked. */ }
            }
            await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token },
                async (uri, token) =>
                {
                    try
                    {
                        RemoteMailImage? image = await RemoteImageLoader.LoadFullAsync(uri, token);
                        if (image is not null)
                        {
                            loaded.TryAdd(uri.AbsoluteUri, image.Bitmap);
                            loadedData.TryAdd(uri.AbsoluteUri, image.DataUri);
                        }
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                    catch (Exception) { /* Keep this image blocked and allow retry. */ }
                });
            if (cts.IsCancellationRequested || _selected != item || _loadedImages != loaded) return;
            ApplyRenderedMail(SafeHtmlRenderer.Render(html, loaded));
            await ShowTemplateAsync(html, item);
            if (pending.Any(uri => !loaded.ContainsKey(uri.AbsoluteUri)) ||
                pendingInline.Any(id => !loaded.ContainsKey(id))) StatusLabel.Text = Localizer.T("imageLoadError");
        }
        catch (OperationCanceledException) { }
        catch (RegexMatchTimeoutException) { StatusLabel.Text = Localizer.T("imageLoadError"); }
        finally
        {
            if (_imageLoadCts == cts)
            {
                _imageLoadCts = null;
                LoadImagesButton.IsEnabled = true;
                UpdateImageNotices();
            }
            cts.Dispose();
        }
    }

    private async Task ShowCalendarAsync()
    {
        if (!_calendarMode && MessageColumn.Width.IsAbsolute) _savedMessageWidth = MessageColumn.Width;
        _calendarMode = true;
        UpdateNavigation();
        MessagesPanel.Visibility = ReaderPanel.Visibility = MessageSplitter.Visibility = Visibility.Collapsed;
        MessageColumn.MinWidth = 0;
        MessageColumn.Width = new GridLength(0);
        MessageSplitterColumn.Width = new GridLength(0);
        CalendarPanel.Visibility = Visibility.Visible;
        if (_calendarLoaded || _account is null || _password is null) return;
        CalendarHint.Text = Localizer.T("loading");
        try
        {
            var account = _account;
            string folder = _folder;
            await using var session = await ImapSession.ConnectAsync(account, _password);
            var invites = await session.LoadInvitesAsync(folder, _messages.Select(x => x.Uid));
            if (_account != account || _folder != folder) return;
            EventList.ItemsSource = invites;
            CalendarHint.Text = invites.Count == 0 ? Localizer.T("noEvents") : Localizer.T("localCalendar");
            _calendarLoaded = true;
        }
        catch (Exception ex) { CalendarHint.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void ShowInbox()
    {
        bool wasCalendar = _calendarMode;
        if (!wasCalendar && _settings.ShowReadingPane && MessageColumn.Width.IsAbsolute && MessageColumn.Width.Value > 0)
            _savedMessageWidth = MessageColumn.Width;
        _calendarMode = false;
        UpdateNavigation();
        CalendarPanel.Visibility = Visibility.Collapsed;
        ApplyReadingPane();
    }

    private void UpdateNavigation()
    {
        InboxButton.Style = (Style)FindResource(_calendarMode ? "NavButton" : "NavActiveButton");
        CalendarButton.Style = (Style)FindResource(_calendarMode ? "NavActiveButton" : "NavButton");
        RailMailButton.Style = (Style)FindResource(_calendarMode ? "NavButton" : "NavActiveButton");
        RailCalendarButton.Style = (Style)FindResource(_calendarMode ? "NavActiveButton" : "NavButton");
    }

    private async Task SendInviteReplyAsync(string partStat)
    {
        if (_account is null || _password is null || _invite is null) return;
        try
        {
            using MailMessage response = CalendarSupport.CreateReply(_account, _invite, partStat);
            var result = await SmtpService.SendAndSaveAsync(_account, _password, response);
            StatusLabel.Text = result.CopySaved ? Localizer.T("responseSent") :
                Localizer.T("sentCopyFailed") + ": " + result.CopyError;
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async void OpenCompose(string to = "", string subject = "", string body = "", string? replyToId = null)
    {
        if (_account is null || _password is null)
        {
            if (!ShowAccountWindow(_account)) return;
        }
        if (_account is null || _password is null) return;
        var window = new ComposeWindow(_account, _password, to, subject, body, replyToId) { Owner = this };
        if (window.ShowDialog() == true)
        {
            await RefreshAsync(true);
            StatusLabel.Text = window.SentCopyError is null ? Localizer.T("sent") :
                Localizer.T("sentCopyFailed") + ": " + window.SentCopyError;
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e) => OpenCompose();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        MailAccount? target = FileOverlay.Visibility == Visibility.Visible
            ? FileAccounts.SelectedItem as MailAccount ?? _account : _account;
        FileOverlay.Visibility = Visibility.Collapsed;
        if (ShowAccountWindow(target)) await RefreshAsync(true);
    }
    private async void AddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        FileOverlay.Visibility = Visibility.Collapsed;
        if (ShowAccountWindow(null)) await RefreshAsync(true);
    }
    private async void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAccounts || AccountList.SelectedItem is not MailAccount account || account.Email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase)) return;
        _account = account;
        _password = AccountStore.LoadPassword(account.Email);
        AccountStore.SaveActiveEmail(account.Email);
        ResetMailbox();
        if (_password is null && !ShowAccountWindow(account))
        {
            StatusLabel.Text = Localizer.T("statusConnect");
            return;
        }
        await RefreshAsync(true);
    }
    private async void InboxButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, RailMailButton)) { ShowInbox(); return; }
        await NavigateToInboxAsync();
    }

    private async Task NavigateToInboxAsync()
    {
        if (!_folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            _folder = "INBOX";
            _folderDelimiter = _allFolders.FirstOrDefault(folder =>
                folder.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))?.Delimiter ?? '/';
            _searchCts?.Cancel();
            _searchResults = null;
            SearchBox.Clear();
            _calendarLoaded = false;
            ApplyFolderList();
        }
        ShowInbox();
        await RefreshAsync();
    }
    private async void CalendarButton_Click(object sender, RoutedEventArgs e) => await ShowCalendarAsync();
    private async void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFolders || FolderList.SelectedItem is not MailFolder folder || folder.ServerName == _folder) return;
        if (!folder.CanSelect)
        {
            _updatingFolders = true;
            FolderList.SelectedItem = FolderList.Items.OfType<MailFolder>().FirstOrDefault(x =>
                x.ServerName.Equals(_folder, StringComparison.OrdinalIgnoreCase));
            _updatingFolders = false;
            return;
        }
        _folder = folder.ServerName;
        _folderDelimiter = folder.Delimiter;
        _searchCts?.Cancel();
        _searchResults = null;
        SearchBox.Clear();
        _calendarLoaded = false;
        ShowInbox();
        await RefreshAsync();
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        _searchTimer.Stop();
        _searchCts?.Cancel();
        _olderLoadCts?.Cancel();
        _nextOlderSearchIndex = _searchTotalCount = 0;
        _loadedSearchQuery = null;
        string query = SearchBox.Text.Trim();
        UpdateSearchHint();
        ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _searchResults = query.Length == 0 ? null : _messages.Where(item =>
            item.Subject.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.From.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.To.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.Cc.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        FilterMessages();
        if (query.Length > 0)
        {
            StatusLabel.Text = Localizer.T("searching");
            _searchTimer.Start();
        }
    }
    private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateSearchHint();
    private void UpdateSearchHint() => SearchHint.Visibility =
        SearchBox.Text.Length == 0 && !SearchBox.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RunSearchAsync();
    }
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Text.Trim().Length == 0) { SearchBox.Focus(); return; }
        await RunSearchAsync();
    }
    private void ClearSearchButton_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    private void ReplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        string subject = _selected.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? _selected.Subject : Localizer.T("replyPrefix") + _selected.Subject;
        string body = "\n\n-----\n" + _selected.From + "\n" + (_selectedContent?.PlainText ?? "");
        OpenCompose(_selected.FromAddress, subject, body, _selected.MessageId);
    }
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        string subject = _selected.Subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase) ? _selected.Subject : Localizer.T("forwardPrefix") + _selected.Subject;
        string body = "\n\n-----\n" + _selected.From + "\n" + (_selectedContent?.PlainText ?? "");
        OpenCompose(subject: subject, body: body);
    }
    private async void AcceptButton_Click(object sender, RoutedEventArgs e) => await SendInviteReplyAsync("ACCEPTED");
    private async void TentativeButton_Click(object sender, RoutedEventArgs e) => await SendInviteReplyAsync("TENTATIVE");
    private async void DeclineButton_Click(object sender, RoutedEventArgs e) => await SendInviteReplyAsync("DECLINED");
    private void FrenchButton_Click(object sender, RoutedEventArgs e) => SetLanguage("fr");
    private void EnglishButton_Click(object sender, RoutedEventArgs e) => SetLanguage("en");
    private void SetLanguage(string language)
    {
        Localizer.SetLanguage(language);
        AccountStore.SaveLanguage(language);
    }
    private static string MessageCount(int count) => string.Format(Localizer.T(count == 1 ? "messageCountSingle" : "messageCountPlural"), count);
}
