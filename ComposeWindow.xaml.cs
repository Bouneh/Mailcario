using System.IO;
using System.Net.Mail;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Mailcario;

public sealed record AttachmentDisplayItem(string FullPath, string DisplayName);

public partial class ComposeWindow : Window
{
    private MailAccount _account;
    private string _password;
    private readonly string? _replyToId;
    private readonly List<string> _attachmentPaths = [];
    private MailPriority _priority = MailPriority.Normal;
    private string _activeTab = "message";
    private bool _sending;

    public ComposeWindow(MailAccount account, string password, string to = "", string subject = "", string body = "", string? replyToId = null)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        _account = account;
        _password = password;
        _replyToId = replyToId;
        var accounts = AccountStore.LoadAccounts().ToList();
        if (!accounts.Any(a => a.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase))) accounts.Add(account);
        foreach (MailAccount sender in accounts)
            FromAccountBox.Items.Add(new ComboBoxItem { Content = sender.Email, Tag = sender });
        FromAccountBox.SelectedItem = FromAccountBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(item => ((MailAccount)item.Tag).Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
        _password = password;
        ToBox.Text = to;
        SubjectBox.Text = subject;
        BodyEditor.Document.Blocks.Clear();
        BodyEditor.Document.Blocks.Add(new Paragraph(new Run(body)));
        Localizer.Changed += ApplyLanguage;
        Closed += (_, _) => Localizer.Changed -= ApplyLanguage;
        ApplyLanguage();
        SelectTab("message");
        ToBox.Focus();
    }

    private void ApplyLanguage()
    {
        ComposeFileTab.Content = Localizer.T("file");
        ComposeMessageTab.Content = Localizer.T("message");
        ComposeInsertTab.Content = Localizer.T("insert");
        ComposeOptionsTab.Content = Localizer.T("options");
        ComposeFormatTab.Content = Localizer.T("textFormat");
        ComposeReviewTab.Content = Localizer.T("review");
        ComposeCloseButton.Content = Localizer.T("close");
        ComposeSendRibbon.Content = SendButton.Content = Localizer.T("send");
        ComposeAttachButton.Content = ComposeInsertAttachment.Content = Localizer.T("attachFile");
        ComposeSignatureButton.Content = ComposeInsertSignature.Content = Localizer.T("signature");
        ComposeEditSignature.Content = Localizer.T("editSignature");
        DropHint.Text = Localizer.T("dropAttachments");
        ComposeBoldButton.Content = ComposeFormatBold.Content = Localizer.T("bold");
        ComposeItalicButton.Content = ComposeFormatItalic.Content = Localizer.T("italic");
        ComposeUnderlineButton.Content = ComposeFormatUnderline.Content = Localizer.T("underline");
        ComposeFormatBullets.Content = Localizer.T("bullets");
        ComposeFormatLeft.Content = Localizer.T("alignLeft");
        ComposeFormatCenter.Content = Localizer.T("alignCenter");
        ComposeFormatRight.Content = Localizer.T("alignRight");
        ComposeHighButton.Content = ComposeOptionsHigh.Content = Localizer.T("importanceHigh");
        ComposeLowButton.Content = ComposeOptionsLow.Content = Localizer.T("importanceLow");
        ComposeBccButton.Content = Localizer.T("showBcc");
        ReadReceiptCheck.Content = Localizer.T("readReceipt");
        SpellCheckBox.Content = Localizer.T("spellCheck");
        BodyEditor.Language = System.Windows.Markup.XmlLanguage.GetLanguage(
            Localizer.Language == "fr" ? "fr-FR" : "en-US");
        FromLabel.Text = Localizer.T("from");
        ToLabel.Text = Localizer.T("to");
        CcLabel.Text = Localizer.T("cc");
        BccLabel.Text = Localizer.T("bcc");
        SubjectLabel.Text = Localizer.T("subject");
        UpdateImportance();
    }

    private void ComposeTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) SelectTab(button.Tag?.ToString() ?? "message");
    }

    private void SelectTab(string tab)
    {
        _activeTab = tab;
        ComposeFileRibbon.Visibility = tab == "file" ? Visibility.Visible : Visibility.Collapsed;
        ComposeMessageRibbon.Visibility = tab == "message" ? Visibility.Visible : Visibility.Collapsed;
        ComposeInsertRibbon.Visibility = tab == "insert" ? Visibility.Visible : Visibility.Collapsed;
        ComposeOptionsRibbon.Visibility = tab == "options" ? Visibility.Visible : Visibility.Collapsed;
        ComposeFormatRibbon.Visibility = tab == "format" ? Visibility.Visible : Visibility.Collapsed;
        ComposeReviewRibbon.Visibility = tab == "review" ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button button in new[] { ComposeFileTab, ComposeMessageTab, ComposeInsertTab,
            ComposeOptionsTab, ComposeFormatTab, ComposeReviewTab })
        {
            bool selected = button.Tag?.ToString() == _activeTab;
            button.Background = selected ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
            button.Foreground = selected ? (Brush)FindResource("AccentDark") : (Brush)FindResource("Ink");
        }
    }

    private void FromAccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FromAccountBox.SelectedItem is not ComboBoxItem { Tag: MailAccount account }) return;
        _account = account;
        _password = AccountStore.LoadPassword(account.Email) ?? "";
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = Localizer.T("attachFile") };
        if (dialog.ShowDialog(this) != true) return;
        AddAttachments(dialog.FileNames);
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        _attachmentPaths.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
        UpdateAttachments();
    }

    private void ComposeWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ComposeWindow_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddAttachments(paths);
    }

    private void AddAttachments(IEnumerable<string> paths)
    {
        int added = 0;
        int ignored = 0;
        foreach (string path in paths)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) { ignored++; continue; }
                if (_attachmentPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) continue;
                _attachmentPaths.Add(fullPath);
                added++;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                or UnauthorizedAccessException or System.Security.SecurityException)
            { ignored++; }
        }
        UpdateAttachments();
        StatusLabel.Text = ignored > 0 ? Localizer.T("ignoredAttachments") : "";
        if (added > 0) BodyEditor.Focus();
    }

    private void UpdateAttachments()
    {
        AttachmentList.ItemsSource = null;
        AttachmentList.ItemsSource = _attachmentPaths.Select(path => new AttachmentDisplayItem(path,
            Path.GetFileName(path) + " · " + FormatFileSize(path))).ToList();
        AttachmentList.Visibility = _attachmentPaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DropHint.Visibility = _attachmentPaths.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatFileSize(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            return bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#") + " MB" :
                bytes >= 1024 ? (bytes / 1024d).ToString("0.#") + " KB" : bytes + " B";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "?"; }
    }

    private void SignatureButton_Click(object sender, RoutedEventArgs e)
    {
        var signatures = SignatureStore.List(_account.Email);
        if (signatures.Count == 0)
        {
            OpenSignatureManager();
            return;
        }
        var menu = new ContextMenu { PlacementTarget = sender as Button, Placement = PlacementMode.Bottom };
        foreach (SignatureEntry signature in signatures)
        {
            var item = new MenuItem { Header = signature.Name };
            item.Click += (_, _) => InsertSignature(signature.Text);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = Localizer.T("manageSignatures") };
        manage.Click += (_, _) => OpenSignatureManager();
        menu.Items.Add(manage);
        menu.IsOpen = true;
    }

    private void InsertSignature(string signature)
    {
        if (signature.Length == 0) return;
        BodyEditor.Focus();
        BodyEditor.Selection.Text = "\n\n" + signature;
    }

    private void OpenSignatureManager() => new SignaturesWindow(_account.Email) { Owner = this }.ShowDialog();

    private void EditSignatureButton_Click(object sender, RoutedEventArgs e) => OpenSignatureManager();

    private void BccButton_Click(object sender, RoutedEventArgs e)
    {
        bool show = BccBox.Visibility != Visibility.Visible;
        BccBox.Visibility = BccLabel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BccRow.Height = new GridLength(show ? 43 : 0);
    }

    private void HighImportanceButton_Click(object sender, RoutedEventArgs e)
    {
        _priority = _priority == MailPriority.High ? MailPriority.Normal : MailPriority.High;
        UpdateImportance();
    }

    private void LowImportanceButton_Click(object sender, RoutedEventArgs e)
    {
        _priority = _priority == MailPriority.Low ? MailPriority.Normal : MailPriority.Low;
        UpdateImportance();
    }

    private void UpdateImportance()
    {
        foreach (Button button in new[] { ComposeHighButton, ComposeOptionsHigh })
            button.Background = _priority == MailPriority.High ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
        foreach (Button button in new[] { ComposeLowButton, ComposeOptionsLow })
            button.Background = _priority == MailPriority.Low ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleBold);
    private void ItalicButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleItalic);
    private void UnderlineButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleUnderline);
    private void BulletsButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleBullets);
    private void AlignLeftButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignLeft);
    private void AlignCenterButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignCenter);
    private void AlignRightButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignRight);

    private void ExecuteEditorCommand(RoutedCommand command)
    {
        BodyEditor.Focus();
        if (command.CanExecute(null, BodyEditor)) command.Execute(null, BodyEditor);
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || FontSizeBox.SelectedItem is not ComboBoxItem selected ||
            !double.TryParse(selected.Content?.ToString(), out double size)) return;
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        BodyEditor.Focus();
    }

    private void SpellCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (BodyEditor is not null) SpellCheck.SetIsEnabled(BodyEditor, SpellCheckBox.IsChecked == true);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        StatusLabel.Text = "";
        if (string.IsNullOrWhiteSpace(ToBox.Text))
        {
            StatusLabel.Text = Localizer.T("to") + " ?";
            ToBox.Focus();
            return;
        }
        if (_password.Length == 0)
        {
            StatusLabel.Text = Localizer.T("accountCredentialsMissing");
            return;
        }
        _sending = true;
        SendButton.IsEnabled = ComposeSendRibbon.IsEnabled = false;
        try
        {
            string plain = new TextRange(BodyEditor.Document.ContentStart, BodyEditor.Document.ContentEnd).Text.TrimEnd('\r', '\n');
            string html = ComposeDocument.ToHtml(BodyEditor.Document);
            using MailMessage mail = SmtpService.CreateMessage(_account, ToBox.Text, CcBox.Text, SubjectBox.Text, plain, _replyToId);
            foreach (string address in BccBox.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mail.Bcc.Add(new MailAddress(address));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
            mail.Priority = _priority;
            if (ReadReceiptCheck.IsChecked == true) mail.Headers.Add("Disposition-Notification-To", _account.Email);
            foreach (string path in _attachmentPaths) mail.Attachments.Add(new Attachment(path));
            await SmtpService.SendAsync(_account, _password, mail);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = ComposeSendRibbon.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
