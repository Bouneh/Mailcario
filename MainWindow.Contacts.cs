namespace Mailcario;

public partial class MainWindow
{
    private readonly CancellationTokenSource _contactIndexCts = new();
    private readonly HashSet<string> _contactIndexStarted = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContactHistoryIndexStore _contactHistory = new();

    private void StartContactHistoryIndex()
    {
        foreach (MailAccount account in _accounts)
        {
            if (!_contactIndexStarted.Add(account.Email)) continue;
            MailAccount candidate = account;
            _ = Task.Run(() => IndexAccountContactsAsync(candidate, _contactIndexCts.Token));
        }
    }

    private async Task IndexAccountContactsAsync(MailAccount account, CancellationToken token)
    {
        try
        {
            string? password = AccountStore.LoadPassword(account.Email);
            if (password is null) return;
            await using var session = await ImapSession.ConnectAsync(account, password, token);
            IReadOnlyList<MailFolder> folders = await session.ListFoldersAsync(token);
            string[] ownAddresses = _accounts.Select(value => value.Email).ToArray();
            foreach (MailFolder folder in folders.Where(value => value.CanSelect && !SkipContactFolder(value)))
            {
                token.ThrowIfCancellationRequested();
                InboxSnapshot snapshot = await session.GetFolderSnapshotAsync(folder.ServerName, token: token);
                if (snapshot.Latest is null || snapshot.UidValidity == 0) continue;
                ContactHistoryCursor previous = _contactHistory.Get(account.Email, folder.ServerName);
                uint boundary = previous.UidValidity == snapshot.UidValidity ? previous.HighestUid : 0;
                if (snapshot.Latest.Uid <= boundary) continue;
                int? before = null;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    MailPage page = await session.LoadPageAsync(folder.ServerName, before, 50, token, folder.IsSent);
                    foreach (MailItem item in page.Messages) item.IsSent = folder.IsSent;
                    ContactStore.RememberFromMessages(page.Messages, ownAddresses);
                    if (page.NextSequence == 0 || page.Messages.Any(item => boundary > 0 && item.Uid <= boundary)) break;
                    before = page.NextSequence;
                    await Task.Delay(80, token);
                }
                _contactHistory.Set(account.Email, folder.ServerName,
                    new ContactHistoryCursor { UidValidity = snapshot.UidValidity, HighestUid = snapshot.Latest.Uid });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Contact indexing must never interrupt the mailbox. */ }
    }

    private static bool SkipContactFolder(MailFolder folder)
    {
        string name = folder.DisplayName;
        return name.Equals("Spam", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Junk", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Junk Email", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Courrier indésirable", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Trash", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Deleted Items", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Éléments supprimés", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Drafts", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Brouillons", StringComparison.OrdinalIgnoreCase);
    }
}
