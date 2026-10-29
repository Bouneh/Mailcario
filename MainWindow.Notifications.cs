using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

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
    private int _activeSyncOperations;
    private DateTimeOffset? _lastSuccessfulSync;
    private MailNotificationWindow? _mailNotice;
    private static readonly ImageSource MailBadge = CreateMailBadge();

    private void InitializeNotifications()
    {
        TaskbarItemInfo = new TaskbarItemInfo();
        Activated += (_, _) => ClearMailBadge();
        PreviewMouseDown += (_, _) => ClearMailBadge();
        _mailTimer.Tick += async (_, _) => await PollAllAccountsAsync();
        Closed += (_, _) =>
        {
            _mailTimer.Stop();
            _monitorCancellation.Cancel();
            if (_mailNotice?.IsVisible == true) _mailNotice.Close();
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

    private void SetAccountConnected(string email, bool connected)
    {
        if (connected)
        {
            _connectedAccounts.Add(email);
            _lastSuccessfulSync = DateTimeOffset.Now;
        }
        else _connectedAccounts.Remove(email);
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
    }

    private async Task PollAllAccountsAsync()
    {
        if (_monitorRunning || _offlineMode || _monitorCancellation.IsCancellationRequested) return;
        _monitorRunning = true;
        bool refreshActiveInbox = false;
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
                if (password is null) { SetAccountConnected(account.Email, false); continue; }
                try
                {
                    await using var session = await ImapSession.ConnectAsync(account, password, _monitorCancellation.Token);
                    InboxSnapshot snapshot = await session.GetInboxSnapshotAsync(_monitorCancellation.Token);
                    SetAccountConnected(account.Email, true);
                    if (ObserveInbox(account.Email, snapshot.UidValidity, snapshot.Latest) &&
                        account.Email.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase))
                        refreshActiveInbox = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { SetAccountConnected(account.Email, false); }
            }
            if (!_offlineMode && refreshActiveInbox && _folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase) && !_calendarMode)
                await RefreshAsync();
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
        NotifyNewMail(email, latest);
        return true;
    }

    private void ObserveLoadedInbox()
    {
        if (_folder != "INBOX" || _account is null) return;
        ObserveInbox(_account.Email, 0, _messages.MaxBy(m => m.Uid));
    }

    private void NotifyNewMail(string email, MailItem item)
    {
        if (_settings.PlaySound) SystemSounds.Asterisk.Play();
        if (!_settings.NotifyNewMail) return;
        TaskbarItemInfo.Overlay = MailBadge;
        if (_mailNotice?.IsVisible == true) _mailNotice.Close();
        string detail = _settings.ShowMessagePreview
            ? $"{item.SenderName} · {item.Subject}" : Localizer.T("newMailNotification");
        _mailNotice = new MailNotificationWindow(email, detail, OpenFromMailNotice);
        _mailNotice.Show();
    }

    private void OpenFromMailNotice()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        ClearMailBadge();
    }

    private void ClearMailBadge()
    {
        if (TaskbarItemInfo is not null) TaskbarItemInfo.Overlay = null;
        if (_mailNotice?.IsVisible == true) _mailNotice.Close();
        _mailNotice = null;
    }

    private static ImageSource CreateMailBadge()
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(67, 142, 199)),
                new Pen(Brushes.White, 0.8), new Rect(0.5, 0.5, 15, 15), 4, 4);
            var white = new Pen(Brushes.White, 1.5);
            drawing.DrawRectangle(null, white, new Rect(3, 5, 10, 6));
            drawing.DrawLine(white, new Point(3, 5), new Point(8, 8.5));
            drawing.DrawLine(white, new Point(13, 5), new Point(8, 8.5));
        }
        var image = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }
}
