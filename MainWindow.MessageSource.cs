using System.Windows;

namespace Mailcario;

public partial class MainWindow
{
    private void MessageSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is { } mail) ShowMessageSource(mail);
    }

    private void ShowMessageSource(MailItem mail)
    {
        var viewer = new MessageSourceWindow(mail.Subject, async token =>
        {
            if (mail.LocalId.Length > 0)
                return await Task.Run(() => _localMailStore.LoadRawMessage(mail.LocalId), token);

            MailAccount account = AccountFor(mail) ??
                throw new InvalidOperationException(Localizer.T("sourceMissingAccount"));
            string password = AccountStore.LoadPassword(account.Email) ??
                throw new InvalidOperationException(Localizer.T("sourceOffline"));
            await using var session = await ImapSession.ConnectAsync(account, password);
            return await session.LoadRawMessageAsync(FolderFor(mail), mail.Uid, token);
        }) { Owner = this };
        viewer.Show();
    }
}
