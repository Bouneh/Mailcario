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
    private readonly DispatcherTimer _mailTimer = new();
    private readonly CancellationTokenSource _monitorCancellation = new();
    private bool _monitorRunning;
    private bool _offlineMode;
    private readonly HashSet<string> _connectedAccounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _connectionErrors = new(StringComparer.OrdinalIgnoreCase);
    private int _activeSyncOperations;
    private string? _lastNotifiedAccount;
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
                    InboxSnapshot snapshot = await session.GetInboxSnapshotAsync(_monitorCancellation.Token);
                    SetAccountConnected(account.Email, true);
                    SetInboxUnreadCount(account.Email, snapshot.UnreadCount);
                    if (ObserveInbox(account.Email, snapshot.UidValidity, snapshot.Latest) && snapshot.Latest is not null)
                    {
                        bool activeInbox = IsActiveInbox(account.Email);
                        if (activeInbox) InsertIncomingInboxMessage(snapshot.Latest);
                        await NotifyNewMailAsync(session, account.Email, snapshot.Latest, _monitorCancellation.Token);
                        if (activeInbox && IsActiveInbox(account.Email)) await RefreshAsync();
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SetAccountConnected(account.Email, false, ex.Message); }
            }
        }
        catch (OperationCanceledException) { }
        finally { _monitorRunning = false; EndSync(); }
    }

    private bool ObserveInbox(string email, uint validity, MailItem? latest)
    {
        if (latest is null)
        {
            if (_inboxCursors.TryGetValue(email, out var emptyPrevious) && validity != 0 && emptyPrevious.Validity != validity)
                _inboxCursors[email] = (validity, 0);
            return false;
        }
        if (!_inboxCursors.TryGetValue(email, out var previous))
        {
            _inboxCursors[email] = (validity, latest.Uid);
            return false;
        }
        if (previous.Validity != 0 && validity != 0 && previous.Validity != validity)
        {
            _inboxCursors[email] = (validity, latest.Uid);
            return false;
        }
        if (latest.Uid <= previous.Uid)
        {
            if (previous.Validity == 0 && validity != 0) _inboxCursors[email] = (validity, previous.Uid);
            return false;
        }
        _inboxCursors[email] = (validity == 0 ? previous.Validity : validity, latest.Uid);
        return true;
    }

    private MailItem? ObserveLoadedInbox()
    {
        if (!_folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase) || _account is null) return null;
        MailItem? latest = _messages.MaxBy(m => m.Uid);
        return ObserveInbox(_account.Email, 0, latest) ? latest : null;
    }

    private async Task NotifyNewMailAsync(ImapSession session, string email, MailItem item, CancellationToken token)
    {
        if (_settings.PlaySound) SystemSounds.Asterisk.Play();
        if (!_settings.NotifyNewMail) return;
        string preview = "";
        if (_settings.ShowMessagePreview)
        {
            try
            {
                string body = await session.LoadMessagePreviewAsync("INBOX", item.Uid, token);
                preview = Regex.Replace(body, @"\s+", " ").Trim();
                if (preview.Length > 150) preview = preview[..147] + "…";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* A missing preview must not suppress the new-mail alert. */ }
        }
        NotifyNewMail(email, item, preview);
    }

    private void NotifyNewMail(string email, MailItem item, string preview)
    {
        _lastNotifiedAccount = email;
        string detail = _settings.ShowMessagePreview
            ? $"{item.SenderName} · {item.Subject}" + (preview.Length > 0 ? "\n" + preview : "")
            : Localizer.T("newMailNotification");
        ShowNewMailBalloon(email, detail);
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
        if (!account.Email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase))
        {
            AccountList.SelectedItem = account;
            return;
        }
        await NavigateToInboxAsync();
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
