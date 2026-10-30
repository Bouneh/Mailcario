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
    private Paragraph? _signatureParagraph;
    private Dictionary<DependencyProperty, object>? _formatPainter;
    public string? SentCopyError { get; private set; }

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
        MessageFontFamilyBox.SelectedIndex = 4;
        MessageFontSizeBox.SelectedIndex = 3;
        FontSizeBox.SelectedIndex = 2;
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
        SendButton.Content = Localizer.T("send");
        ComposeAttachButton.Content = ComposeInsertAttachment.Content = Localizer.T("attachFile");
        ComposeSignatureButton.Content = ComposeInsertSignature.Content = Localizer.T("signature");
        ComposeEditSignature.Content = Localizer.T("editSignature");
        ComposePasteButton.Content = Localizer.T("paste");
        ComposeCutButton.Content = ComposeRibbonIcons.Labeled("cut", Localizer.T("cut"));
        ComposeCopyButton.Content = ComposeRibbonIcons.Labeled("copy", Localizer.T("copy"));
        ComposePainterButton.Content = ComposeRibbonIcons.Labeled("painter", Localizer.T("formatPainterShort"));
        ComposePainterButton.ToolTip = Localizer.T("formatPainter");
        ComposeClipboardCaption.Text = Localizer.T("clipboard");
        ComposeTextCaption.Text = Localizer.T("simpleText");
        ComposeNamesCaption.Text = Localizer.T("names");
        ComposeAddressBook.Content = Localizer.T("addressBook");
        ComposeCheckNames.Content = Localizer.T("checkNames");
        ComposeGrowFont.ToolTip = Localizer.T("growFont");
        ComposeShrinkFont.ToolTip = Localizer.T("shrinkFont");
        ComposeGrowFont.Content = ComposeRibbonIcons.Create("grow");
        ComposeShrinkFont.Content = ComposeRibbonIcons.Create("shrink");
        ComposeBulletsButton.ToolTip = Localizer.T("bullets");
        ComposeNumberingButton.ToolTip = Localizer.T("numbering");
        ComposeClearFormat.ToolTip = Localizer.T("clearFormatting");
        ComposeBulletsButton.Content = ComposeRibbonIcons.Create("bullets");
        ComposeNumberingButton.Content = ComposeRibbonIcons.Create("numbering");
        ComposeClearFormat.Content = ComposeRibbonIcons.Create("clear");
        ComposeHighlightButton.ToolTip = Localizer.T("highlightColor");
        ComposeFontColorButton.ToolTip = Localizer.T("fontColor");
        ComposeHighlightDropDownButton.ToolTip = Localizer.T("highlightColor");
        ComposeFontColorDropDownButton.ToolTip = Localizer.T("fontColor");
        UpdateColorButtons();
        ComposeAlignLeft.ToolTip = Localizer.T("alignLeft");
        ComposeAlignCenter.ToolTip = Localizer.T("alignCenter");
        ComposeAlignRight.ToolTip = Localizer.T("alignRight");
        ComposeAlignJustify.ToolTip = Localizer.T("alignJustify");
        ComposeOutdent.ToolTip = Localizer.T("decreaseIndent");
        ComposeIndent.ToolTip = Localizer.T("increaseIndent");
        DropHint.Text = Localizer.T("dropAttachments");
        ComposeBoldButton.ToolTip = ComposeFormatBold.Content = Localizer.T("bold");
        ComposeItalicButton.ToolTip = ComposeFormatItalic.Content = Localizer.T("italic");
        ComposeUnderlineButton.ToolTip = ComposeFormatUnderline.Content = Localizer.T("underline");
        ComposeFormatBullets.Content = Localizer.T("bullets");
        ComposeFormatLeft.Content = Localizer.T("alignLeft");
        ComposeFormatCenter.Content = Localizer.T("alignCenter");
        ComposeFormatRight.Content = Localizer.T("alignRight");
        ComposeOptionsHigh.Content = Localizer.T("importanceHigh");
        ComposeOptionsLow.Content = Localizer.T("importanceLow");
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
        var signatures = SignatureStore.ListAll()
            .OrderBy(choice => !choice.AccountEmail.Equals(_account.Email, StringComparison.OrdinalIgnoreCase))
            .ThenBy(choice => choice.AccountEmail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Signature.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (signatures.Count == 0)
        {
            OpenSignatureManager();
            return;
        }
        CreateSignatureMenu(sender as Button, signatures).IsOpen = true;
    }

    private ContextMenu CreateSignatureMenu(Button? target, IReadOnlyList<SignatureChoice> signatures)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom,
            Style = (Style)FindResource("MailMessageContextMenuStyle"), MinWidth = 290, MaxHeight = 430 };
        var none = new MenuItem { Header = Localizer.T("noSignature"),
            Style = (Style)FindResource("MailMessageMenuItemStyle"),
            Icon = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                Foreground = (Brush)FindResource("Muted"), FontSize = 17 } };
        none.Click += (_, _) => ClearSignature();
        menu.Items.Add(none);
        menu.Items.Add(new Separator { Style = (Style)FindResource("MailMessageSeparatorStyle") });
        foreach (SignatureChoice choice in signatures)
        {
            var title = new StackPanel();
            title.Children.Add(new TextBlock { Text = choice.Signature.Name,
                FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Ink") });
            title.Children.Add(new TextBlock { Text = choice.AccountEmail, FontSize = 10,
                Foreground = (Brush)FindResource("Muted") });
            var item = new MenuItem { Header = title, Style = (Style)FindResource("MailMessageMenuItemStyle"),
                Icon = new TextBlock { Text = "\uE8D4", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    Foreground = (Brush)FindResource("IconFolder"), FontSize = 17 } };
            item.Click += (_, _) => InsertSignature(choice.Signature.Text);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator { Style = (Style)FindResource("MailMessageSeparatorStyle") });
        var manage = new MenuItem { Header = Localizer.T("manageSignatures"),
            Style = (Style)FindResource("MailMessageMenuItemStyle"),
            Icon = new TextBlock { Text = "\uE70F", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                Foreground = (Brush)FindResource("IconFolder"), FontSize = 17 } };
        manage.Click += (_, _) => OpenSignatureManager();
        menu.Items.Add(manage);
        return menu;
    }

    private void InsertSignature(string signature)
    {
        if (signature.Length == 0) { ClearSignature(); return; }
        if (_signatureParagraph is null || !BodyEditor.Document.Blocks.Contains(_signatureParagraph))
        {
            _signatureParagraph = new Paragraph { Margin = new Thickness(0, 10, 0, 0) };
            BodyEditor.Document.Blocks.Add(_signatureParagraph);
        }
        _signatureParagraph.Inlines.Clear();
        string[] lines = signature.Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0) _signatureParagraph.Inlines.Add(new LineBreak());
            _signatureParagraph.Inlines.Add(new Run(lines[index]));
        }
        BodyEditor.CaretPosition = _signatureParagraph.ContentEnd;
        BodyEditor.Focus();
    }

    private void ClearSignature()
    {
        if (_signatureParagraph is not null && BodyEditor.Document.Blocks.Contains(_signatureParagraph))
            BodyEditor.Document.Blocks.Remove(_signatureParagraph);
        _signatureParagraph = null;
        BodyEditor.Focus();
    }

    private void OpenSignatureManager() => new SignaturesWindow(_account.Email) { Owner = this }.ShowDialog();

    private void EditSignatureButton_Click(object sender, RoutedEventArgs e) => OpenSignatureManager();

    private void AddressBookButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ContactsWindow { Owner = this };
        if (window.ShowDialog() != true || window.PickedEmail is null) return;
        TextBox target = window.AddToCc ? CcBox : ToBox;
        string[] existing = target.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!existing.Contains(window.PickedEmail, StringComparer.OrdinalIgnoreCase))
            target.Text = target.Text.Trim().Length == 0 ? window.PickedEmail :
                target.Text.TrimEnd(' ', ',', ';') + "; " + window.PickedEmail;
        target.Focus();
        target.CaretIndex = target.Text.Length;
    }

    private void CheckNamesButton_Click(object sender, RoutedEventArgs e)
    {
        var contacts = ContactStore.Load();
        foreach (TextBox field in new[] { ToBox, CcBox, BccBox })
        {
            var verified = new List<string>();
            foreach (string item in field.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = contacts.FirstOrDefault(contact =>
                    contact.Name.Equals(item, StringComparison.CurrentCultureIgnoreCase))?.Email ?? item;
                try { verified.Add(new MailAddress(candidate).Address); }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    StatusLabel.Foreground = (Brush)FindResource("ErrorText");
                    StatusLabel.Text = string.Format(Localizer.T("invalidRecipient"), item);
                    field.Focus(); field.SelectAll();
                    return;
                }
            }
            field.Text = string.Join("; ", verified);
        }
        StatusLabel.Foreground = (Brush)FindResource("AccentDark");
        StatusLabel.Text = Localizer.T("namesVerified");
    }

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
        ComposeOptionsHigh.Background = _priority == MailPriority.High ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
        ComposeOptionsLow.Background = _priority == MailPriority.Low ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
    }

    private void BoldButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleBold);
    private void ItalicButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleItalic);
    private void UnderlineButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleUnderline);
    private void BulletsButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleBullets);
    private void NumberingButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.ToggleNumbering);
    private void AlignLeftButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignLeft);
    private void AlignCenterButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignCenter);
    private void AlignRightButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignRight);
    private void AlignJustifyButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(EditingCommands.AlignJustify);
    private void PasteButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(ApplicationCommands.Paste);
    private void CutButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(ApplicationCommands.Cut);
    private void CopyButton_Click(object sender, RoutedEventArgs e) => ExecuteEditorCommand(ApplicationCommands.Copy);

    private void GrowFontButton_Click(object sender, RoutedEventArgs e) => ChangeFontSize(2);
    private void ShrinkFontButton_Click(object sender, RoutedEventArgs e) => ChangeFontSize(-2);

    private void ChangeFontSize(int delta)
    {
        object selected = BodyEditor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        double current = selected is double size ? size : BodyEditor.FontSize;
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Clamp(current + delta, 8, 72));
        BodyEditor.Focus();
    }

    private void ClearFormatButton_Click(object sender, RoutedEventArgs e)
    {
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
        BodyEditor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14d);
        BodyEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (Brush)FindResource("Ink"));
        BodyEditor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
        BodyEditor.Focus();
    }

    private void IndentButton_Click(object sender, RoutedEventArgs e) => ChangeIndent(24);
    private void OutdentButton_Click(object sender, RoutedEventArgs e) => ChangeIndent(-24);

    private void ChangeIndent(int delta)
    {
        foreach (Paragraph paragraph in BodyEditor.Document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.ContentEnd.CompareTo(BodyEditor.Selection.Start) >= 0 &&
                                paragraph.ContentStart.CompareTo(BodyEditor.Selection.End) <= 0))
            paragraph.Margin = new Thickness(Math.Max(0, paragraph.Margin.Left + delta),
                paragraph.Margin.Top, paragraph.Margin.Right, paragraph.Margin.Bottom);
        BodyEditor.Focus();
    }

    private void FormatPainterButton_Click(object sender, RoutedEventArgs e)
    {
        DependencyProperty[] properties = [TextElement.FontFamilyProperty, TextElement.FontSizeProperty,
            TextElement.FontWeightProperty, TextElement.FontStyleProperty, TextElement.ForegroundProperty,
            TextElement.BackgroundProperty, Inline.TextDecorationsProperty];
        _formatPainter = properties.ToDictionary(property => property, property =>
        {
            object value = BodyEditor.Selection.GetPropertyValue(property);
            if (value != DependencyProperty.UnsetValue && value is not null) return value;
            if (property == Inline.TextDecorationsProperty) return new TextDecorationCollection();
            if (property == TextElement.BackgroundProperty) return Brushes.Transparent;
            return BodyEditor.GetValue(property) ?? DependencyProperty.UnsetValue;
        });
        StatusLabel.Foreground = (Brush)FindResource("AccentDark");
        StatusLabel.Text = Localizer.T("selectFormatTarget");
        BodyEditor.Focus();
    }

    private void BodyEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_formatPainter is null || BodyEditor.Selection.IsEmpty) return;
        var formatting = _formatPainter;
        _formatPainter = null;
        foreach (var (property, value) in formatting)
            if (value != DependencyProperty.UnsetValue) BodyEditor.Selection.ApplyPropertyValue(property, value);
        StatusLabel.Text = "";
    }

    private void ExecuteEditorCommand(RoutedCommand command)
    {
        BodyEditor.Focus();
        if (command.CanExecute(null, BodyEditor)) command.Execute(null, BodyEditor);
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || sender is not ComboBox { SelectedItem: ComboBoxItem selected } ||
            !double.TryParse(selected.Content?.ToString(), out double size)) return;
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        BodyEditor.Focus();
    }

    private void FontFamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || MessageFontFamilyBox.SelectedItem is not ComboBoxItem selected) return;
        BodyEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,
            new FontFamily(selected.Content?.ToString() ?? "Segoe UI"));
        BodyEditor.Focus();
    }

    private void SpellCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (BodyEditor is not null) SpellCheck.SetIsEnabled(BodyEditor, SpellCheckBox.IsChecked == true);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        StatusLabel.Foreground = (Brush)FindResource("ErrorText");
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
        SendButton.IsEnabled = false;
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
            var result = await SmtpService.SendAndSaveAsync(_account, _password, mail);
            SentCopyError = result.CopyError;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
