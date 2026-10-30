using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Globalization;

namespace Mailcario;

public partial class MainWindow
{
    private readonly Dictionary<string, (uint Validity, uint Uid)> _inboxCursors =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Email, string Folder), (uint Validity, uint Uid)> _folderCursors = [];
    private readonly DispatcherTimer _mailTimer = new();
    private readonly CancellationTokenSource _monitorCancellation = new();
    private bool _monitorRunning;
    private bool _offlineMode;
    private readonly HashSet<string> _connectedAccounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _connectionErrors = new(StringComparer.OrdinalIgnoreCase);
    private int _activeSyncOperations;
    private string? _lastNotifiedAccount;
    private string _lastNotifiedFolder = "INBOX";
    private DateTimeOffset? _lastSuccessfulSync;
    private readonly Dictionary<string, int> _unreadInboxCounts = new(StringComparer.OrdinalIgnoreCase);
    private int _displayedUnreadCount = -1;
    private int _unreadInboxTotal;

    private void InitializeNotifications()
    {
        InitializeTrayEvents();
        TaskbarItemInfo = new TaskbarItemInfo();
        _mailTimer.Tick += async (_, _) => await PollAllAccountsAsync();
        Closed += (_, _) =>
        {
            _mailTimer.Stop();
            _monitorCancellation.Cancel();
        };
    }

    private void ConfigureMailTimer()
    {
        _mailTimer.Stop();
        if (_offlineMode) return;
        _mailTimer.Interval = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
        _mailTimer.Start();
    }

    private void BeginSync(string description)
    {
        _activeSyncOperations++;
        SyncProgress.Visibility = Visibility.Visible;
        SyncStatusText.Text = Localizer.T("syncing") + " " + description;
        UpdateConnectionStatus();
    }

    private void EndSync()
    {
        _activeSyncOperations = Math.Max(0, _activeSyncOperations - 1);
        if (_activeSyncOperations > 0) return;
        SyncProgress.Visibility = Visibility.Collapsed;
        SyncStatusText.Text = _lastSuccessfulSync is null ? "" :
            Localizer.T("lastSync") + " " + _lastSuccessfulSync.Value.LocalDateTime.ToString("HH:mm");
        UpdateConnectionStatus();
    }

    private void SetAccountConnected(string email, bool connected, string? reason = null)
    {
        if (connected)
        {
            _connectedAccounts.Add(email);
            _connectionErrors.Remove(email);
            _lastSuccessfulSync = DateTimeOffset.Now;
        }
        else
        {
            _connectedAccounts.Remove(email);
            if (!string.IsNullOrWhiteSpace(reason)) _connectionErrors[email] = reason;
        }
        UpdateConnectionStatus();
    }

    private void UpdateConnectionStatus()
    {
        int total = _accounts.Count;
        int online = _accounts.Count(a => _connectedAccounts.Contains(a.Email));
        ConnectionStatusText.Text = _offlineMode ? Localizer.T("offlineStatus") : total == 0
            ? Localizer.T("noAccounts") : online == total ? Localizer.T("connected") :
            online == 0 ? Localizer.T("disconnected") : string.Format(Localizer.T("accountsConnected"), online, total);
        ConnectionDot.Fill = _offlineMode || total == 0 ? (Brush)FindResource("Muted") :
            online == total ? (Brush)FindResource("Accent") : (Brush)FindResource("ErrorText");
        ConnectionStatusText.ToolTip = string.Join("\n", _accounts.Select(a =>
            a.Email + " — " + Localizer.T(_connectedAccounts.Contains(a.Email) ? "connected" : "disconnected")));
        if (ConnectionDetailsPopup.IsOpen) UpdateConnectionDetails();
    }

    private void ConnectionStatusButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateConnectionDetails();
        ConnectionDetailsPopup.IsOpen = !ConnectionDetailsPopup.IsOpen;
    }

    private void UpdateConnectionDetails()
    {
        ConnectionDetailsPanel.Children.Clear();
        ConnectionDetailsPanel.Children.Add(new TextBlock
        {
            Text = Localizer.T("connectionDetails"), FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Ink"), Margin = new Thickness(0, 0, 0, 10)
        });
        if (_accounts.Count == 0)
        {
            ConnectionDetailsPanel.Children.Add(new TextBlock { Text = Localizer.T("noAccounts"),
                Foreground = (Brush)FindResource("Muted") });
            return;
        }
        foreach (MailAccount account in _accounts)
        {
            bool connected = _connectedAccounts.Contains(account.Email) && !_offlineMode;
            var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            entry.Children.Add(new TextBlock { Text = account.Email, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Ink"), TextTrimming = TextTrimming.CharacterEllipsis });
            string detail = connected ? Localizer.T("connected") : _offlineMode
                ? Localizer.T("offlineStatus") : _connectionErrors.TryGetValue(account.Email, out string? error)
                ? Localizer.T("disconnected") + " — " + error : Localizer.T("connectionPending");
            entry.Children.Add(new TextBlock { Text = detail, Foreground = (Brush)FindResource(connected ? "Accent" : "Muted"),
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
            ConnectionDetailsPanel.Children.Add(entry);
        }
    }

    private async Task PollAllAccountsAsync()
    {
        if (_monitorRunning || _offlineMode || _monitorCancellation.IsCancellationRequested) return;
        _monitorRunning = true;
        var accounts = AccountStore.LoadAccounts();
        BeginSync(Localizer.T("allAccounts"));
        try
        {
            foreach (MailAccount account in accounts)
            {
                if (_offlineMode) break;
                SyncStatusText.Text = Localizer.T("syncing") + " " + account.Email;
                _monitorCancellation.Token.ThrowIfCancellationRequested();
                string? password = AccountStore.LoadPassword(account.Email);
                if (password is null) { SetAccountConnected(account.Email, false, Localizer.T("accountCredentialsMissing")); continue; }
                try
                {
                    await using var session = await ImapSession.ConnectAsync(account, password, _monitorCancellation.Token);
                    try
                    {
                        await MailRuleStore.ApplyAsync(session, account.Email, "INBOX", [], _monitorCancellation.Token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { StatusLabel.Text = Localizer.T("rules") + ": " + ex.Message; }
                    InboxSnapshot snapshot = await session.GetInboxSnapshotAsync(_monitorCancellation.Token);
                    SetAccountConnected(account.Email, true);
                    SetInboxUnreadCount(account.Email, snapshot.UnreadCount);
                    if (ObserveInbox(account.Email, snapshot.UidValidity, snapshot.Latest) && snapshot.Latest is not null)
                    {
                        bool activeInbox = IsActiveInbox(account.Email);
                        if (activeInbox) InsertIncomingInboxMessage(snapshot.Latest);
                        if (IsFolderNotificationEnabled(account.Email, "INBOX"))
                            await NotifyNewMailAsync(session, account.Email, "INBOX", snapshot.Latest, _monitorCancellation.Token);
                        if (activeInbox && IsActiveInbox(account.Email)) await RefreshAsync(preserveLoaded: true);
                    }
                    foreach (string folder in NotificationFoldersFor(account.Email).Where(name =>
                        !name.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                    {
                        _monitorCancellation.Token.ThrowIfCancellationRequested();
                        try
                        {
                            InboxSnapshot folderSnapshot = await session.GetFolderSnapshotAsync(folder,
                                token: _monitorCancellation.Token);
                            if (!ObserveFolder(account.Email, folder, folderSnapshot.UidValidity, folderSnapshot.Latest) ||
                                folderSnapshot.Latest is null) continue;
                            await NotifyNewMailAsync(session, account.Email, folder, folderSnapshot.Latest,
                                _monitorCancellation.Token);
                            if (IsActiveFolder(account.Email, folder)) await RefreshAsync(preserveLoaded: true);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + folder + ": " + ex.Message; }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SetAccountConnected(account.Email, false, ex.Message); }
            }
        }
        catch (OperationCanceledException) { }
        finally { _monitorRunning = false; EndSync(); }
    }

    private bool ObserveInbox(string email, uint validity, MailItem? latest) =>
        AdvanceCursor(_inboxCursors, email, validity, latest);

    private bool ObserveFolder(string email, string folder, uint validity, MailItem? latest) =>
        AdvanceCursor(_folderCursors, (email.ToLowerInvariant(), folder), validity, latest);

    private static bool AdvanceCursor<TKey>(Dictionary<TKey, (uint Validity, uint Uid)> cursors,
        TKey key, uint validity, MailItem? latest) where TKey : notnull
    {
        if (latest is null)
        {
            if (!cursors.TryGetValue(key, out var emptyPrevious) ||
                (validity != 0 && emptyPrevious.Validity != validity))
                cursors[key] = (validity, 0);
            return false;
        }
        if (!cursors.TryGetValue(key, out var previous))
        {
            cursors[key] = (validity, latest.Uid);
            return false;
        }
        if (previous.Validity != 0 && validity != 0 && previous.Validity != validity)
        {
            cursors[key] = (validity, latest.Uid);
            return false;
        }
        if (latest.Uid <= previous.Uid)
        {
            if (previous.Validity == 0 && validity != 0) cursors[key] = (validity, previous.Uid);
            return false;
        }
        cursors[key] = (validity == 0 ? previous.Validity : validity, latest.Uid);
        return true;
    }

    private bool IsActiveFolder(string email, string folder) => !_calendarMode &&
        email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase) &&
        folder.Equals(_folder, StringComparison.OrdinalIgnoreCase);

    private async Task PrimeFolderNotificationCursorAsync(MailAccount account, string folder)
    {
        try
        {
            string? password = AccountStore.LoadPassword(account.Email);
            if (password is null) return;
            await using var session = await ImapSession.ConnectAsync(account, password);
            InboxSnapshot snapshot = await session.GetFolderSnapshotAsync(folder);
            if (folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                _inboxCursors[account.Email] = (snapshot.UidValidity, snapshot.Latest?.Uid ?? 0);
            else _folderCursors[(account.Email.ToLowerInvariant(), folder)] =
                (snapshot.UidValidity, snapshot.Latest?.Uid ?? 0);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private MailItem? ObserveLoadedInbox()
    {
        if (!_folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase) || _account is null) return null;
        MailItem? latest = _messages.MaxBy(m => m.Uid);
        return ObserveInbox(_account.Email, 0, latest) ? latest : null;
    }

    private async Task NotifyNewMailAsync(ImapSession session, string email, string folder,
        MailItem item, CancellationToken token)
    {
        if (_settings.PlaySound) SystemSounds.Asterisk.Play();
        if (!_settings.NotifyNewMail) return;
        string preview = "";
        if (_settings.ShowMessagePreview)
        {
            try
            {
                string body = await session.LoadMessagePreviewAsync(folder, item.Uid, token);
                preview = Regex.Replace(body, @"\s+", " ").Trim();
                if (preview.Length > 150) preview = preview[..147] + "…";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* A missing preview must not suppress the new-mail alert. */ }
        }
        NotifyNewMail(email, folder, item, preview);
    }

    private void NotifyNewMail(string email, string folder, MailItem item, string preview)
    {
        _lastNotifiedAccount = email;
        _lastNotifiedFolder = folder;
        string folderName = (email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase)
            ? _allFolders.FirstOrDefault(value => value.ServerName.Equals(folder,
                StringComparison.OrdinalIgnoreCase))?.DisplayName : null)
            ?? new MailFolder { ServerName = folder }.DisplayName;
        string detail = _settings.ShowMessagePreview
            ? $"{email}\n{item.SenderName} · {item.Subject}" + (preview.Length > 0 ? "\n" + preview : "")
            : email;
        ShowNewMailBalloon(folderName, detail);
    }

    private void OpenFromMailNotice()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async void OpenFromNewMailNotice()
    {
        OpenFromMailNotice();
        if (_lastNotifiedAccount is null) return;
        MailAccount? account = _accounts.FirstOrDefault(value =>
            value.Email.Equals(_lastNotifiedAccount, StringComparison.OrdinalIgnoreCase));
        if (account is null) return;
        string folder = _lastNotifiedFolder;
        if (!account.Email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase))
        {
            _updatingAccounts = true;
            AccountList.SelectedItem = account;
            _updatingAccounts = false;
            if (!await SwitchAccountAsync(account)) return;
        }
        await NavigateToFolderAsync(folder);
    }

    private void SetInboxUnreadCount(string email, int count)
    {
        if (!_accounts.Any(account => account.Email.Equals(email, StringComparison.OrdinalIgnoreCase))) return;
        _unreadInboxCounts[email] = Math.Max(0, count);
        UpdateUnreadBadge();
    }

    private void UpdateUnreadBadge()
    {
        int total = _accounts.Sum(account => _unreadInboxCounts.GetValueOrDefault(account.Email));
        if (total == _displayedUnreadCount) return;
        _displayedUnreadCount = total;
        _unreadInboxTotal = total;
        if (TaskbarItemInfo is not null)
        {
            TaskbarItemInfo.Overlay = total > 0 ? CreateMailBadge(total) : null;
            TaskbarItemInfo.Description = total > 0
                ? string.Format(Localizer.T("unreadInboxCount"), total) : "Mailcario";
        }
        SetMailAttention(total > 0);
    }

    private static ImageSource CreateMailBadge(int count)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            // Windows scales taskbar overlays down to roughly 16 px. The Mailcario icon already
            // depicts an envelope, so the overlay uses its whole area for a readable count.
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(47, 111, 166)),
                new Pen(Brushes.White, 3), new Point(32, 32), 30, 30);
            string label = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
            var number = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                label.Length > 2 ? 23 : label.Length > 1 ? 31 : 40, Brushes.White, 1);
            drawing.DrawText(number, new Point(32 - number.Width / 2, 31 - number.Height / 2));
        }
        var image = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }
}
