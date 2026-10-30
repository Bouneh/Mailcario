using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mailcario;

public partial class ComposeWindow
{
    private readonly MailTemplateStore _templateStore = new();
    private readonly ScheduledMailStore _scheduledStore;

    private void TemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("MailMessageContextMenuStyle"),
            PlacementTarget = ComposeTemplatesButton, Placement = PlacementMode.Bottom };
        var save = TemplateMenuItem(Localizer.T("saveTemplate"));
        save.Click += (_, _) => SaveCurrentTemplate();
        menu.Items.Add(save);
        foreach (MailTemplate template in _templateStore.List().OrderBy(item => item.Name,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            var item = TemplateMenuItem(template.Name);
            item.Click += (_, _) => ApplyTemplate(template);
            menu.Items.Add(item);
        }
        var remove = TemplateMenuItem(Localizer.T("deleteTemplate"));
        foreach (MailTemplate template in _templateStore.List())
        {
            var item = TemplateMenuItem(template.Name);
            item.Click += (_, _) => { _templateStore.Delete(template.Id); StatusLabel.Text = template.Name; };
            remove.Items.Add(item);
        }
        remove.IsEnabled = remove.Items.Count > 0;
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    private static MenuItem TemplateMenuItem(string title) => new()
    {
        Header = title,
        Style = (Style)Application.Current.FindResource("MailMessageMenuItemStyle")
    };

    private void SaveCurrentTemplate()
    {
        string? name = PromptText(Localizer.T("templateName"));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            name = name.Trim();
            MailTemplate? previous = _templateStore.List().FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            _templateStore.Save(new MailTemplate
            {
                Id = previous?.Id ?? Guid.NewGuid().ToString("N"),
                Name = name,
                Subject = SubjectBox.Text,
                Body = new TextRange(BodyEditor.Document.ContentStart, BodyEditor.Document.ContentEnd).Text.TrimEnd('\r', '\n'),
                Html = ComposeDocument.ToHtml(BodyEditor.Document)
            });
            StatusLabel.Text = name;
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void ApplyTemplate(MailTemplate template)
    {
        SubjectBox.Text = template.Subject;
        try
        {
            if (template.Html.Length > 0)
                BodyEditor.Document = SafeHtmlRenderer.Render(template.Html).Document;
            else
            {
                BodyEditor.Document.Blocks.Clear();
                BodyEditor.Document.Blocks.Add(new Paragraph(new Run(template.Body)));
            }
        }
        catch (Exception) { BodyEditor.Document = new FlowDocument(new Paragraph(new Run(template.Body))); }
        _signatureParagraph = null;
        BodyEditor.Focus();
    }

    private string? PromptText(string title)
    {
        var window = new Window { Title = title, Owner = this, Width = 400, MinHeight = 210,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (Brush)FindResource("Canvas"), Foreground = (Brush)FindResource("Ink") };
        WindowTheme.Attach(window);
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 10) });
        var field = new TextBox { Padding = new Thickness(10, 7, 10, 7) };
        root.Children.Add(field);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = Localizer.T("cancel"), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = Localizer.T("save") };
        cancel.Click += (_, _) => window.DialogResult = false;
        save.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(save); root.Children.Add(buttons);
        window.Content = root;
        window.Loaded += (_, _) => field.Focus();
        return window.ShowDialog() == true ? field.Text : null;
    }

    private void ScheduleSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ToBox.Text)) { StatusLabel.Text = Localizer.T("to") + " ?"; return; }
        var window = new Window { Title = Localizer.T("scheduleSend"), Owner = this, Width = 470,
            MinHeight = 310, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (Brush)FindResource("Canvas"), Foreground = (Brush)FindResource("Ink") };
        WindowTheme.Attach(window);
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = Localizer.T("scheduleDate"), FontWeight = FontWeights.SemiBold });
        var date = new DatePicker { SelectedDate = DateTime.Today.AddDays(1),
            Margin = new Thickness(0, 8, 0, 15) };
        root.Children.Add(date);
        root.Children.Add(new TextBlock { Text = Localizer.T("scheduleTime"), FontWeight = FontWeights.SemiBold });
        var time = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
        var hours = new ComboBox { Width = 75, ItemsSource = Enumerable.Range(0, 24)
            .Select(number => number.ToString("00")).ToArray(), SelectedIndex = 9 };
        var minutes = new ComboBox { Width = 75, ItemsSource = Enumerable.Range(0, 60)
            .Select(number => number.ToString("00")).ToArray(), SelectedIndex = 0 };
        time.Children.Add(hours); time.Children.Add(new TextBlock { Text = ":", FontSize = 20,
            Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        time.Children.Add(minutes); root.Children.Add(time);
        root.Children.Add(new TextBlock { Text = Localizer.T("scheduleHint"), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 16) });
        var error = new TextBlock { Foreground = (Brush)FindResource("ErrorText"),
            Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Localizer.T("cancel"), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = Localizer.T("scheduleSend") };
        cancel.Click += (_, _) => window.DialogResult = false;
        save.Click += (_, _) =>
        {
            DateTime selected = (date.SelectedDate ?? DateTime.Today).Date
                .AddHours(hours.SelectedIndex).AddMinutes(minutes.SelectedIndex);
            if (new DateTimeOffset(selected) <= DateTimeOffset.Now)
            { error.Text = Localizer.T("scheduleFuture"); return; }
            window.DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); root.Children.Add(buttons);
        window.Content = root;
        if (window.ShowDialog() != true) return;
        DateTime local = (date.SelectedDate ?? DateTime.Today).Date
            .AddHours(hours.SelectedIndex).AddMinutes(minutes.SelectedIndex);
        DateTimeOffset due = new(local);
        if (due <= DateTimeOffset.Now) { StatusLabel.Text = Localizer.T("scheduleFuture"); return; }
        try
        {
            string plain = new TextRange(BodyEditor.Document.ContentStart,
                BodyEditor.Document.ContentEnd).Text.TrimEnd('\r', '\n');
            string html = ComposeDocument.ToHtml(BodyEditor.Document);
            // Validate recipient syntax before persisting a job.
            using (MailMessage check = SmtpService.CreateMessage(_account, ToBox.Text, CcBox.Text,
                       SubjectBox.Text, plain, _replyToId))
                foreach (string address in BccBox.Text.Split([',', ';'],
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    check.Bcc.Add(new MailAddress(address));
            var job = new ScheduledMail { AccountEmail = _account.Email, To = ToBox.Text,
                Cc = CcBox.Text, Bcc = BccBox.Text, Subject = SubjectBox.Text, PlainText = plain,
                Html = html, ReplyToId = _replyToId, Priority = _priority,
                ReadReceipt = ReadReceiptCheck.IsChecked == true, DueUtc = due.ToUniversalTime() };
            _scheduledStore.Enqueue(job, _attachmentPaths);
            WasScheduled = true;
            DialogResult = true;
        }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }

    private void ScheduledQueueButton_Click(object sender, RoutedEventArgs e) =>
        new ScheduledQueueWindow { Owner = this }.ShowDialog();
}
