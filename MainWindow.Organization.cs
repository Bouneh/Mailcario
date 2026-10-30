using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

public partial class MainWindow
{
    private async Task RunMessageActionAsync(MailItem mail, Func<ImapSession, Task> action)
    {
        if (_offlineMode) { StatusLabel.Text = Localizer.T("offlineStatus"); return; }
        MailAccount? account = AccountFor(mail);
        string? password = account is null ? null : AccountStore.LoadPassword(account.Email);
        if (account is null || password is null) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            await action(session);
            await RefreshAsync();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private async Task MoveToSpecialFolderAsync(MailItem mail, string kind)
    {
        MailAccount? account = AccountFor(mail);
        string? password = account is null ? null : AccountStore.LoadPassword(account.Email);
        if (account is null || password is null || _offlineMode) return;
        try
        {
            await using var session = await ImapSession.ConnectAsync(account, password);
            IReadOnlyList<MailFolder> folders = await session.ListFoldersAsync();
            string[] names = kind == "archive" ? ["archive", "archives", "all mail", "tous les messages"] :
                ["trash", "deleted", "corbeille", "éléments supprimés"];
            MailFolder? target = folders.FirstOrDefault(folder => folder.CanSelect && names.Any(name =>
                folder.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                folder.ServerName.Contains(name, StringComparison.OrdinalIgnoreCase)));
            if (target is null) { StatusLabel.Text = Localizer.T("folderNotFound"); return; }
            await session.MoveAsync(FolderFor(mail), mail.Uid, target.ServerName);
            await RefreshAsync();
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void AddOrganizationMenu(ContextMenu menu, MailItem mail)
    {
        var categories = AddSubmenu(menu, "categories");
        foreach (var entry in new[]
        {
            (Key: "categoryBlue", Color: "#287AB7"), (Key: "categoryGreen", Color: "#37845C"),
            (Key: "categoryOrange", Color: "#C17B26"), (Key: "categoryRed", Color: "#B75050"),
            (Key: "categoryPurple", Color: "#8657AB")
        })
        {
            var item = NewMenuItem(entry.Key);
            item.Icon = new System.Windows.Shapes.Ellipse { Width = 15, Height = 15,
                Fill = (Brush)new BrushConverter().ConvertFromString(entry.Color)! };
            item.Click += (_, _) => { _organization.SetCategory(mail, entry.Key, entry.Color); FilterMessages(); };
            categories.Items.Add(item);
        }
        AddAction(categories, "clearCategory", () => { _organization.SetCategory(mail, "", "#4B9CD3"); FilterMessages(); });
        AddAction(categories, "customCategory", () =>
        {
            string? name = FolderNamePrompt(Localizer.T("customCategory"), mail.CategoryName);
            if (string.IsNullOrWhiteSpace(name)) return;
            _organization.SetCategory(mail, name.Trim(), "#287AB7");
            FilterMessages();
        });
        var snooze = AddSubmenu(menu, "snooze");
        AddAction(snooze, "snoozeHour", () => Snooze(mail, DateTimeOffset.Now.AddHours(1)));
        AddAction(snooze, "snoozeTomorrow", () => Snooze(mail,
            new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(9))));
        AddAction(snooze, "snoozeWeek", () => Snooze(mail, DateTimeOffset.Now.AddDays(7)));
        AddAction(snooze, "snoozeCustom", () =>
        {
            DateTimeOffset? until = PromptSnoozeDate();
            if (until is not null) Snooze(mail, until);
        });
        AddAction(snooze, "clearSnooze", () => Snooze(mail, null));
    }

    private void Snooze(MailItem mail, DateTimeOffset? until)
    {
        _organization.SetSnooze(mail, until);
        FilterMessages();
    }

    private DateTimeOffset? PromptSnoozeDate()
    {
        var prompt = new Window { Title = Localizer.T("snoozeCustom"), Owner = this,
            Width = 410, MinHeight = 260, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Background = (Brush)FindResource("Canvas"),
            Foreground = (Brush)FindResource("Ink") };
        WindowTheme.Attach(prompt);
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = Localizer.T("scheduleDate"),
            FontWeight = FontWeights.SemiBold });
        var date = new DatePicker { SelectedDate = DateTime.Today.AddDays(1),
            Margin = new Thickness(0, 7, 0, 12) };
        root.Children.Add(date);
        root.Children.Add(new TextBlock { Text = Localizer.T("scheduleTime"),
            FontWeight = FontWeights.SemiBold });
        var time = new TextBox { Text = "09:00", Width = 80, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 7, 0, 10) };
        root.Children.Add(time);
        var error = new TextBlock { Foreground = (Brush)FindResource("ErrorText"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(error);
        DateTimeOffset? result = null;
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Localizer.T("cancel"), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = Localizer.T("save") };
        cancel.Click += (_, _) => prompt.DialogResult = false;
        save.Click += (_, _) =>
        {
            if (!TimeSpan.TryParseExact(time.Text.Trim(), @"hh\:mm",
                    System.Globalization.CultureInfo.InvariantCulture, out TimeSpan parsed) ||
                parsed >= TimeSpan.FromDays(1))
            { error.Text = Localizer.T("scheduleTime") + " : HH:mm"; return; }
            var value = new DateTimeOffset((date.SelectedDate ?? DateTime.Today).Date + parsed);
            if (value <= DateTimeOffset.Now) { error.Text = Localizer.T("scheduleFuture"); return; }
            result = value;
            prompt.DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(save); root.Children.Add(actions);
        prompt.Content = root;
        return prompt.ShowDialog() == true ? result : null;
    }
}
