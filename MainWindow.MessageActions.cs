using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Mailcario;

public partial class MainWindow
{
    private void MessageItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: MailItem mail } row) return;
        if (!row.IsSelected)
        {
            MessageList.SelectedItems.Clear();
            row.IsSelected = true;
        }
        string sourceFolder = FolderFor(mail);
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("MailMessageContextMenuStyle")
        };
        row.ContextMenu = menu;
        if (mail.LocalId.Length > 0)
        {
            AddAction(menu, "copy", async () => await CopyMessageAsync(mail));
            AddAction(menu, "quickPrint", async () => await PrintMessageAsync(mail));
            menu.Items.Add(NewSeparator());
            AddAction(menu, "reply", async () => await ComposeReplyAsync(mail, false));
            AddAction(menu, "forward", async () => await ComposeForwardAsync(mail));
            AddAction(menu, mail.IsRead ? "markUnread" : "markRead", () => SetLocalRead(mail, !mail.IsRead));
            AddOrganizationMenu(menu, mail);
            menu.Items.Add(NewSeparator());
            AddAction(menu, "deleteLocalMessage", () => DeleteLocalMessage(mail));
            menu.PlacementTarget = row;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        AddAction(menu, "copy", async () => await CopyMessageAsync(mail));
        AddAction(menu, "quickPrint", async () => await PrintMessageAsync(mail));
        menu.Items.Add(NewSeparator());
        AddAction(menu, "reply", async () => await ComposeReplyAsync(mail, false));
        AddAction(menu, "replyAll", async () => await ComposeReplyAsync(mail, true));
        AddAction(menu, "forward", async () => await ComposeForwardAsync(mail));
        menu.Items.Add(NewSeparator());
        AddAction(menu, mail.IsRead ? "markUnread" : "markRead",
            async () => await SetMessageReadAsync(mail, !mail.IsRead));
        var follow = AddSubmenu(menu, "followUp");
        AddAction(follow, mail.IsFlagged ? "clearFollowUp" : "followUp",
            async () => await RunMessageActionAsync(mail, s => s.SetFlaggedAsync(sourceFolder, mail.Uid, !mail.IsFlagged)));
        var similar = AddSubmenu(menu, "findSimilar");
        AddAction(similar, "fromSender", async () => { SearchBox.Text = mail.FromAddress; await RunSearchAsync(); });
        AddAction(similar, "sameSubject", async () => { SearchBox.Text = mail.Subject; await RunSearchAsync(); });
        var quick = AddSubmenu(menu, "quickActions");
        AddAction(quick, "markRead", async () => await SetMessageReadAsync(mail, true));
        AddAction(quick, "markReadArchive", async () =>
        {
            await SetMessageReadAsync(mail, true);
            await MoveToSpecialFolderAsync(mail, "archive");
        });
        var archive = FindSpecialFolder("archive");
        AddAction(quick, "archive", async () => await MoveToSpecialFolderAsync(mail, "archive"));
        if (_unifiedMode || (mail.AccountEmail.Length > 0 &&
            !mail.AccountEmail.Equals(_account?.Email, StringComparison.OrdinalIgnoreCase)))
        {
            AddOrganizationMenu(menu, mail);
            AddAction(menu, "delete", async () => await MoveToSpecialFolderAsync(mail, "trash"));
            AddAction(menu, "archive", async () => await MoveToSpecialFolderAsync(mail, "archive"));
            menu.PlacementTarget = row;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        var rules = AddSubmenu(menu, "rules");
        AddAction(rules, "createRule", async () => await OpenRulesWindowAsync(mail));
        AddAction(rules, "manageRules", async () => await OpenRulesWindowAsync());
        var move = AddSubmenu(menu, "move");
        foreach (MailFolder folder in _allFolders.Where(f => f.CanSelect && f.ServerName != sourceFolder))
        {
            string target = folder.ServerName;
            var item = NewMenuItem("move", folder.DisplayName);
            item.Click += async (_, _) => await MoveMessageAsync(mail, sourceFolder, target);
            move.Items.Add(item);
        }
        move.IsEnabled = move.Items.Count > 0;
        var local = AddSubmenu(menu, "copyToLocalFolder");
        var account = _account;
        var selected = MessageList.SelectedItems.OfType<MailItem>().Where(item => item.LocalId.Length == 0 &&
            item.AccountEmail.Equals(mail.AccountEmail, StringComparison.OrdinalIgnoreCase) &&
            FolderFor(item) == sourceFolder).ToList();
        if (selected.Count == 0) selected.Add(mail);
        foreach (var target in _localMailStore.ListFolders())
        {
            var choice = NewMenuItem("copyToLocalFolder", target.Name);
            choice.Click += async (_, _) =>
            {
                if (account is not null) await CopyToLocalFolderAsync(selected, sourceFolder, account, target.Id);
            };
            local.Items.Add(choice);
        }
        local.Items.Add(NewSeparator());
        AddAction(local, "newLocalFolder", async () =>
        {
            string? name = FolderNamePrompt(Localizer.T("newLocalFolder"), "");
            if (string.IsNullOrWhiteSpace(name) || account is null) return;
            try
            {
                var target = _localMailStore.CreateFolder(name);
                RefreshLocalFolders();
                await CopyToLocalFolderAsync(selected, sourceFolder, account, target.Id);
            }
            catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        });
        menu.Items.Add(NewSeparator());
        AddOrganizationMenu(menu, mail);
        AddUnavailable(menu, "ignore");
        var junk = FindSpecialFolder("junk");
        var junkMenu = AddSubmenu(menu, "junk");
        if (junk is not null)
        {
            AddAction(junkMenu, "moveToJunk", async () => await MoveMessageAsync(mail, sourceFolder, junk.ServerName));
            AddAction(junkMenu, "blockSender", async () => await OpenRulesWindowAsync(mail, junk.ServerName));
        }
        else AddUnavailable(junkMenu, "moveToJunk");
        if (IsCurrentFolder("trash") && _allFolders.FirstOrDefault(f => f.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) is { } inbox)
            AddAction(menu, "restore", async () => await MoveMessageAsync(mail, sourceFolder, inbox.ServerName));
        else AddUnavailable(menu, "restore");
        var trash = FindSpecialFolder("trash");
        if (trash is not null && !IsCurrentFolder("trash"))
            AddAction(menu, "delete", async () => await MoveMessageAsync(mail, sourceFolder, trash.ServerName));
        else AddUnavailable(menu, "delete");
        if (archive is not null && !IsCurrentFolder("archive"))
            AddAction(menu, "archive", async () => await MoveMessageAsync(mail, sourceFolder, archive.ServerName));
        else AddUnavailable(menu, "archive");
        menu.PlacementTarget = row;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem AddSubmenu(ItemsControl parent, string key)
    {
        var item = NewMenuItem(key);
        parent.Items.Add(item);
        return item;
    }

    private static void AddAction(ItemsControl parent, string key, Action action)
    {
        var item = NewMenuItem(key);
        item.Click += (_, _) => action();
        parent.Items.Add(item);
    }

    private static void AddAction(ItemsControl parent, string key, Func<Task> action)
    {
        var item = NewMenuItem(key);
        item.Click += async (_, _) => await action();
        parent.Items.Add(item);
    }

    private static void AddUnavailable(ItemsControl parent, string key) =>
        parent.Items.Add(new MenuItem { Header = Localizer.T(key), Icon = MenuIcon(key), IsEnabled = false,
            Style = (Style)Application.Current.FindResource("MailMessageMenuItemStyle"),
            ToolTip = Localizer.T("unsupportedAction") });

    private static MenuItem NewMenuItem(string key, string? label = null) => new()
    {
        Header = label ?? Localizer.T(key),
        Icon = MenuIcon(key),
        InputGestureText = key switch
        {
            "reply" => "Ctrl+R", "replyAll" => "Ctrl+Shift+R", "forward" => "Ctrl+F",
            "markRead" => "Ctrl+Q", "markUnread" => "Ctrl+U", "delete" => "Del", _ => ""
        },
        Style = (Style)Application.Current.FindResource("MailMessageMenuItemStyle")
    };

    private static Separator NewSeparator() => new()
    {
        Style = (Style)Application.Current.FindResource("MailMessageSeparatorStyle")
    };

    private static System.Windows.Shapes.Path MenuIcon(string key)
    {
        string data = key switch
        {
            "copy" => "M7,5 L17,5 17,18 7,18 Z M4,15 L3,15 3,2 13,2 13,4",
            "quickPrint" => "M5,3 L15,3 15,7 M5,14 L2,14 2,8 18,8 18,14 15,14 M5,12 L15,12 15,18 5,18 Z",
            "reply" => "M9,4 L3,10 9,16 M3,10 L12,10 C16,10 18,13 18,17",
            "replyAll" => "M7,4 L2,10 7,16 M2,10 L11,10 C15,10 18,13 18,17 M11,4 L6,9",
            "forward" => "M11,4 L17,10 11,16 M17,10 L8,10 C4,10 2,13 2,17",
            "markRead" or "markUnread" => "M2,5 L18,5 18,16 2,16 Z M2,6 L10,11 18,6",
            "followUp" or "clearFollowUp" => "M4,2 L4,18 M4,3 L15,3 13,7 16,10 4,10",
            "findSimilar" or "fromSender" or "sameSubject" => "M9,3 A6,6 0 1 1 8.9,3 M14,14 L19,19",
            "quickActions" => "M11,2 L4,11 9,11 7,18 16,8 11,8 Z",
            "rules" or "createRule" or "manageRules" => "M2,3 L18,3 12,10 12,17 8,19 8,10 Z",
            "move" or "copyToLocalFolder" or "newLocalFolder" => "M2,5 L8,5 10,7 17,7 17,16 2,16 Z M11,11 L18,11 M15,8 L18,11 15,14",
            "ignore" => "M5,5 L15,15 M15,5 L5,15",
            "junk" or "moveToJunk" or "blockSender" => "M10,2 L19,18 1,18 Z M10,7 L10,12 M10,15 L10,15.2",
            "restore" => "M4,9 A7,7 0 1 1 5,15 M4,4 L4,9 9,9",
            "delete" or "deleteLocalMessage" => "M4,5 L16,5 M6,5 L7,18 13,18 14,5 M8,3 L12,3 M9,8 L9,15 M11,8 L11,15",
            "archive" => "M2,4 L18,4 18,8 2,8 Z M4,8 L4,17 16,17 16,8 M8,11 L12,11",
            "markReadArchive" => "M2,4 L18,4 18,8 2,8 Z M4,8 L4,17 16,17 16,8 M6,12 L9,15 14,10",
            "categories" or "customCategory" or "clearCategory" => "M2,3 L11,3 18,10 10,18 2,10 Z M6,7 L6.1,7.1",
            "snooze" or "snoozeHour" or "snoozeTomorrow" or "snoozeWeek" or "snoozeCustom" or "clearSnooze" =>
                "M10,2 A8,8 0 1 1 9.9,2 M10,5 L10,10 14,12",
            "enableFolderNotifications" or "disableFolderNotifications" => "M4,12 L4,8 C4,4 12,4 12,8 L12,12 14,14 2,14 Z M7,15 C7,17 9,17 9,15",
            _ => "M3,3 L17,3 17,17 3,17 Z"
        };
        string brush = key switch
        {
            "reply" or "replyAll" => "IconReply",
            "forward" => "IconForward",
            "delete" or "deleteLocalMessage" or "junk" or "moveToJunk" or "blockSender" => "IconDanger",
            "archive" or "markReadArchive" or "snooze" or "snoozeHour" or "snoozeTomorrow" or "snoozeWeek" or "snoozeCustom" => "IconSync",
            "move" or "copyToLocalFolder" or "newLocalFolder" or "rules" or "createRule" or "manageRules" => "IconFolder",
            "enableFolderNotifications" or "disableFolderNotifications" => "IconSync",
            _ => "IconNeutral"
        };
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), Width = 19, Height = 19, Stretch = Stretch.Uniform,
            StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brush);
        return icon;
    }

    private MailFolder? FindSpecialFolder(string kind)
    {
        string[] names = kind switch
        {
            "archive" => ["archive", "archives", "all mail", "tous les messages"],
            "junk" => ["junk", "spam", "indésirable"],
            "trash" => ["trash", "deleted", "corbeille", "éléments supprimés"],
            _ => []
        };
        return _allFolders.FirstOrDefault(f => f.CanSelect && names.Any(name =>
            f.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
            f.ServerName.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private bool IsCurrentFolder(string kind) => FindSpecialFolder(kind)?.ServerName == _folder;

    private Task MoveMessageAsync(MailItem mail, string source, string target) =>
        RunMessageActionAsync(mail, s => s.MoveAsync(source, mail.Uid, target));

    private async Task OpenRulesWindowAsync(MailItem? mail = null, string? preferredTarget = null)
    {
        if (_account is null) return;
        uint minimum = _inboxCursors.GetValueOrDefault(_account.Email).Uid;
        uint validity = _inboxCursors.GetValueOrDefault(_account.Email).Validity;
        if (!_offlineMode && _password is not null)
        {
            try
            {
                await using var session = await ImapSession.ConnectAsync(_account, _password);
                InboxSnapshot snapshot = await session.GetInboxSnapshotAsync();
                minimum = snapshot.Latest?.Uid ?? 0;
                validity = snapshot.UidValidity;
            }
            catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        }
        var window = new RulesWindow(_account, _allFolders, minimum, validity, mail, preferredTarget)
        { Owner = this };
        window.ShowDialog();
    }

    private async void RibbonRules_Click(object sender, RoutedEventArgs e) => await OpenRulesWindowAsync();

    private async Task<string> MessagePlainTextAsync(MailItem mail)
    {
        if (mail == _selected && _selectedContent is not null) return _selectedContent.PlainText;
        if (mail.LocalId.Length > 0) return (await Task.Run(() => _localMailStore.LoadMessage(mail.LocalId))).PlainText;
        MailAccount? account = AccountFor(mail);
        string? password = account is null ? null : AccountStore.LoadPassword(account.Email);
        if (account is null || password is null) return "";
        await using var session = await ImapSession.ConnectAsync(account, password);
        return (await session.LoadMessageAsync(FolderFor(mail), mail.Uid)).PlainText;
    }

    private async Task CopyMessageAsync(MailItem mail)
    {
        try
        {
            string content = await MessagePlainTextAsync(mail);
            Clipboard.SetText($"{mail.From}\r\n{mail.To}\r\n{mail.Subject}\r\n{mail.DateLabel}\r\n\r\n{content}");
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task ComposeReplyAsync(MailItem mail, bool all)
    {
        try
        {
            string recipients = mail.IsSent ? mail.To : mail.FromAddress;
            if (all)
            {
                var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!mail.IsSent && mail.FromAddress.Length > 0) addresses.Add(mail.FromAddress);
                try
                {
                    var collection = new MailAddressCollection();
                    collection.Add(mail.To);
                    if (!string.IsNullOrWhiteSpace(mail.Cc)) collection.Add(mail.Cc);
                    foreach (MailAddress address in collection)
                        if (!address.Address.Equals(AccountFor(mail)?.Email, StringComparison.OrdinalIgnoreCase)) addresses.Add(address.Address);
                }
                catch (FormatException) { }
                recipients = string.Join("; ", addresses);
            }
            string subject = mail.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                ? mail.Subject : Localizer.T("replyPrefix") + mail.Subject;
            OpenCompose(recipients, subject, "\n\n-----\n" + mail.From + "\n" + await MessagePlainTextAsync(mail), mail.MessageId, AccountFor(mail));
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task ComposeForwardAsync(MailItem mail)
    {
        try
        {
            string subject = mail.Subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase)
                ? mail.Subject : Localizer.T("forwardPrefix") + mail.Subject;
            OpenCompose(subject: subject, body: "\n\n-----\n" + mail.From + "\n" + await MessagePlainTextAsync(mail), sourceAccount: AccountFor(mail));
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task PrintMessageAsync(MailItem mail)
    {
        try
        {
            string body = await MessagePlainTextAsync(mail);
            var document = new FlowDocument();
            document.Blocks.Add(new Paragraph(new Run(mail.Subject)) { FontSize = 17, FontWeight = FontWeights.SemiBold });
            document.Blocks.Add(new Paragraph(new Run(mail.From + "\n" + mail.DateLabel)));
            document.Blocks.Add(new Paragraph(new Run(body)));
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() == true)
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, mail.Subject);
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }
}
