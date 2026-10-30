using System.Windows;
using System.Windows.Threading;

namespace Mailcario;

public partial class MainWindow
{
    private readonly ScheduledMailStore _scheduledStore;
    private readonly DispatcherTimer _scheduledTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _processingScheduled;
    private readonly Dictionary<string, ComposeDraft> _undoDrafts = [];

    internal void StartScheduledSending()
    {
        try { _scheduledStore.RecoverInterrupted(); }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("scheduledFailed") + ": " + ex.Message; }
        _scheduledTimer.Tick += async (_, _) => { UpdateUndoSendStatus(); await ProcessScheduledAsync(); };
        _scheduledTimer.Start();
        Closed += (_, _) => _scheduledTimer.Stop();
        UpdateUndoSendStatus();
        _ = ProcessScheduledAsync();
    }

    private void ShowUndoSend(string jobId, ComposeDraft draft)
    {
        _undoDrafts[jobId] = draft;
        UpdateUndoSendStatus();
    }

    private void UpdateUndoSendStatus()
    {
        ScheduledMail? pending;
        try { pending = _scheduledStore.List().Where(job => job.IsUndoSend && job.State == "pending")
            .OrderByDescending(job => job.DueUtc).FirstOrDefault(); }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("scheduledFailed") + ": " + ex.Message; return; }
        UndoSendPanel.Visibility = pending is null ? Visibility.Collapsed : Visibility.Visible;
        StatusLabel.Visibility = pending is null ? Visibility.Visible : Visibility.Collapsed;
        if (pending is null) return;
        int seconds = Math.Max(0, (int)Math.Ceiling((pending.DueUtc - DateTimeOffset.UtcNow).TotalSeconds));
        UndoSendStatus.Text = _offlineMode && seconds == 0 ? Localizer.T("undoSendOffline") :
            string.Format(Localizer.T("undoSendStatus"), seconds);
        UndoSendButton.Content = Localizer.T("undoSendShort");
        UndoSendButton.Tag = pending.Id;
    }

    private void UndoSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (UndoSendButton.Tag is not string id) return;
        if (!_scheduledStore.TryCancelPending(id))
        {
            StatusLabel.Text = Localizer.T("undoSendUnavailable");
            UpdateUndoSendStatus();
            return;
        }
        _undoDrafts.Remove(id, out ComposeDraft? draft);
        UpdateUndoSendStatus();
        StatusLabel.Text = Localizer.T("undoSendCancelled");
        if (draft is null) return;
        MailAccount? account = AccountStore.LoadAccounts().FirstOrDefault(value =>
            value.Email.Equals(draft.AccountEmail, StringComparison.OrdinalIgnoreCase));
        if (account is not null) OpenCompose(sourceAccount: account, draft: draft);
    }

    private async Task ProcessScheduledAsync()
    {
        if (_processingScheduled || _offlineMode) return;
        _processingScheduled = true;
        try
        {
            while (_scheduledStore.ClaimDue(DateTimeOffset.UtcNow) is { } job)
            {
                if (job.IsUndoSend)
                {
                    _undoDrafts.Remove(job.Id);
                    UpdateUndoSendStatus();
                    StatusLabel.Text = Localizer.T("sendingNow");
                }
                MailAccount? account = AccountStore.LoadAccounts().FirstOrDefault(value =>
                    value.Email.Equals(job.AccountEmail, StringComparison.OrdinalIgnoreCase));
                string? password = account is null ? null : AccountStore.LoadPassword(account.Email);
                if (account is null || password is null)
                {
                    _scheduledStore.SetState(job.Id, "failed", Localizer.T("accountCredentialsMissing"));
                    StatusLabel.Text = Localizer.T("scheduledFailed") + ": " + Localizer.T("accountCredentialsMissing");
                    continue;
                }
                try
                {
                    using var mail = ScheduledMailStore.BuildMessage(account, job);
                    SmtpService.SendAndSaveResult result = await SmtpService.SendAndSaveAsync(account, password, mail);
                    try { ContactStore.RememberSentRecipients(job.To, job.Cc, job.Bcc,
                        AccountStore.LoadAccounts().Select(value => value.Email)); }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
                    _scheduledStore.SetState(job.Id, "sent", result.CopyError ?? "");
                    ShowScheduledBalloon(Localizer.T("scheduledSent"), job.Subject);
                    if (!_unifiedMode && _account?.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase) == true)
                        await RefreshAsync(true);
                    StatusLabel.Text = result.CopyError is null ? Localizer.T("sent") :
                        Localizer.T("sentCopyFailed") + ": " + result.CopyError;
                }
                catch (Exception ex)
                {
                    _scheduledStore.SetState(job.Id, "failed", ex.Message);
                    StatusLabel.Text = Localizer.T("scheduledFailed") + ": " + ex.Message;
                    ShowScheduledBalloon(Localizer.T("scheduledFailed"), job.Subject + " · " + ex.Message);
                }
            }
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("scheduledFailed") + ": " + ex.Message; }
        finally { _processingScheduled = false; }
    }

    private void FileScheduled_Click(object sender, RoutedEventArgs e)
    {
        FileOverlay.Visibility = Visibility.Collapsed;
        new ScheduledQueueWindow { Owner = this }.ShowDialog();
    }

    private void ShowScheduledBalloon(string title, string detail)
    {
        if (_trayIcon is null) return;
        _lastBalloonWasScheduled = true;
        string caption = "Mailcario · " + title;
        _trayIcon.ShowBalloonTip(7000, caption.Length > 63 ? caption[..63] : caption,
            detail.Length > 230 ? detail[..227] + "…" : detail,
            System.Windows.Forms.ToolTipIcon.None);
    }
}
