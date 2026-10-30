using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using Mailcario;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--single-instance-check")
        {
            string name = "Preview" + Guid.NewGuid().ToString("N");
            using var activated = new ManualResetEventSlim();
            using var first = new SingleInstanceCoordinator(name, () => activated.Set());
            bool secondWasPrimary = Task.Run(() =>
            {
                using var second = new SingleInstanceCoordinator(name, () => { });
                return second.IsPrimary;
            }).GetAwaiter().GetResult();
            bool passed = first.IsPrimary && !secondWasPrimary && activated.Wait(TimeSpan.FromSeconds(5));
            Console.WriteLine(passed ? "Single instance activation check passed" : "Single instance activation check failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--unread-badge-check")
        {
            var checkApp = new App();
            checkApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var window = new MainWindow();
            var accounts = new[]
            {
                new MailAccount { Email = "first@example.invalid" },
                new MailAccount { Email = "second@example.invalid" }
            };
            typeof(MainWindow).GetField("_accounts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, accounts);
            var setCount = typeof(MainWindow).GetMethod("SetInboxUnreadCount",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            setCount.Invoke(window, [accounts[0].Email, 2]);
            setCount.Invoke(window, [accounts[1].Email, 3]);
            bool passed = window.TaskbarItemInfo.Overlay is not null &&
                window.TaskbarItemInfo.Description.Contains('5');
            setCount.Invoke(window, [accounts[0].Email, 0]);
            passed &= window.TaskbarItemInfo.Overlay is not null &&
                window.TaskbarItemInfo.Description.Contains('3');
            setCount.Invoke(window, [accounts[1].Email, 0]);
            passed &= window.TaskbarItemInfo.Overlay is null;
            string? previewDirectory = Environment.GetEnvironmentVariable("MAILCARIO_BADGE_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                var createBadge = typeof(MainWindow).GetMethod("CreateMailBadge",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                foreach (int count in new[] { 1, 12, 100 })
                {
                    var badge = (BitmapSource)createBadge.Invoke(null, [count])!;
                    foreach ((string suffix, BitmapSource image) in new[]
                    {
                        ("64", badge),
                        ("16", new TransformedBitmap(badge, new ScaleTransform(0.25, 0.25)))
                    })
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        using var stream = File.Create(Path.Combine(previewDirectory, $"badge-{count}-{suffix}.png"));
                        encoder.Save(stream);
                    }
                }
            }
            window.Close();
            ThemeManager.Shutdown();
            Console.WriteLine(passed ? "Unread inbox badge check passed" : "Unread inbox badge check failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--inbox-sync-check")
        {
            var checkApp = new App();
            checkApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var window = new MainWindow();
            var previous = new MailItem { Uid = 10, From = "Previous <previous@example.invalid>",
                Subject = "Previous message", Date = DateTimeOffset.UtcNow.AddHours(-1) };
            typeof(MainWindow).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, new[] { previous });
            typeof(MainWindow).GetMethod("FilterMessages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);
            var syncList = (ListBox)window.FindName("MessageList");
            syncList.SelectedItem = previous;
            var incoming = new MailItem { Uid = 11, From = "New <new@example.invalid>",
                Subject = "New message", Date = DateTimeOffset.UtcNow };
            var insert = typeof(MainWindow).GetMethod("InsertIncomingInboxMessage",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            insert.Invoke(window, [incoming]);
            insert.Invoke(window, [incoming]);
            bool passed = syncList.Items.Count == 2 && ReferenceEquals(syncList.Items[0], incoming) &&
                ReferenceEquals(syncList.SelectedItem, previous) &&
                ((TextBlock)window.FindName("CountLabel")).Text.Contains('2');
            typeof(MainWindow).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, new[]
                {
                    new MailItem { Uid = 11, From = incoming.From, Subject = incoming.Subject, Date = incoming.Date },
                    new MailItem { Uid = 10, From = previous.From, Subject = previous.Subject, Date = previous.Date }
                });
            typeof(MainWindow).GetMethod("FilterMessages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);
            passed &= ReferenceEquals(syncList.SelectedItem, previous) && syncList.Items.Count == 2;
            typeof(MainWindow).GetField("_folder", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, "Archive");
            ((Button)window.FindName("InboxButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed &= (string)typeof(MainWindow).GetField("_folder", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(window)! == "INBOX";
            passed &= MailDateParser.Parse("Thu, 29 Oct 2026 21:52:28 +0000 (UTC)", null)
                == new DateTimeOffset(2026, 10, 29, 21, 52, 28, TimeSpan.Zero);
            passed &= MailDateParser.Parse("Thu, 29 Oct 2026 08:31:52 +0000 (GMT)", null)
                == new DateTimeOffset(2026, 10, 29, 8, 31, 52, TimeSpan.Zero);
            passed &= MailDateParser.Parse("invalid", "29-Oct-2026 21:52:28 +0000")
                == new DateTimeOffset(2026, 10, 29, 21, 52, 28, TimeSpan.Zero);
            passed &= MailDateParser.Parse("Fri, 30 Oct 2026 11:55:28 +0100", "30-Oct-2026 14:15:22 +0100")
                == new DateTimeOffset(2026, 10, 30, 14, 15, 22, TimeSpan.FromHours(1));
            passed &= MailDateParser.Parse("Fri, 30 Oct 2026 11:55:28 +0100", "30-Oct-2026 14:15:22 +0100", true)
                == new DateTimeOffset(2026, 10, 30, 11, 55, 28, TimeSpan.FromHours(1));
            window.Close();
            ThemeManager.Shutdown();
            Console.WriteLine(passed ? "Incoming mail and date parsing checks passed" :
                "Incoming mail or date parsing check failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--mail-pagination-check")
        {
            var bounds = typeof(ImapSession).GetMethod("PageBounds",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            bool passed = ((ValueTuple<int, int, int>)bounds.Invoke(null, [105, null, 50])!) == (56, 105, 55) &&
                ((ValueTuple<int, int, int>)bounds.Invoke(null, [105, 55, 50])!) == (6, 55, 5) &&
                ((ValueTuple<int, int, int>)bounds.Invoke(null, [105, 5, 50])!) == (1, 5, 0) &&
                ((ValueTuple<int, int, int>)bounds.Invoke(null, [0, null, 50])!) == (1, 0, 0);
            var merge = typeof(MainWindow).GetMethod("MergeRefreshedPage",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var fetched = Enumerable.Range(56, 50).Select(uid => new MailItem { Uid = (uint)uid }).ToArray();
            var fresh = fetched.Where(item => item.Uid != 104).ToArray();
            var previous = Enumerable.Range(1, 105).Select(uid => new MailItem { Uid = (uint)uid }).ToArray();
            var merged = (IReadOnlyList<MailItem>)merge.Invoke(null, [fresh, fetched, previous])!;
            passed &= merged.Count == 104 && merged.Any(item => item.Uid == 1) &&
                !merged.Any(item => item.Uid == 104);
            var checkApp = new App();
            checkApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var window = new MainWindow();
            var messages = Enumerable.Range(1, 60).Select(uid => new MailItem { Uid = (uint)uid,
                Subject = "Message " + uid, Date = DateTimeOffset.UtcNow.AddMinutes(-uid) }).ToArray();
            typeof(MainWindow).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, messages);
            typeof(MainWindow).GetField("_folderTotalCount", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, 105);
            typeof(MainWindow).GetField("_nextOlderSequence", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, 45);
            typeof(MainWindow).GetMethod("FilterMessages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);
            var paginationList = (ListBox)window.FindName("MessageList");
            var more = (Button)window.FindName("LoadOlderButton");
            passed &= paginationList.Items.Count == 60 && more.Visibility == Visibility.Visible &&
                ((TextBlock)window.FindName("CountLabel")).Text.Contains("60/105");
            var incoming = new MailItem { Uid = 106, Subject = "Latest", Date = DateTimeOffset.UtcNow };
            typeof(MainWindow).GetMethod("InsertIncomingInboxMessage",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [incoming]);
            passed &= paginationList.Items.Count == 61 && paginationList.Items.Cast<MailItem>().Any(item => item.Uid == 60);
            ((TextBox)window.FindName("SearchBox")).Text = "message";
            typeof(MainWindow).GetField("_searchResults", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, messages.Take(3).ToArray());
            typeof(MainWindow).GetField("_searchTotalCount", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, 80);
            typeof(MainWindow).GetField("_nextOlderSearchIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, 77);
            typeof(MainWindow).GetField("_loadedSearchQuery", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, "message");
            typeof(MainWindow).GetMethod("FilterMessages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);
            passed &= paginationList.Items.Count == 3 && more.Visibility == Visibility.Visible &&
                ((TextBlock)window.FindName("CountLabel")).Text.Contains("3/80");
            window.Close();
            ThemeManager.Shutdown();
            Console.WriteLine(passed ? "Mail pagination check passed" : "Mail pagination check failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--thunderbird-import-check")
        {
            string prefsPath = Path.Combine(Path.GetTempPath(), "Mailcario-Thunderbird-" + Guid.NewGuid().ToString("N") + ".js");
            try
            {
                File.WriteAllText(prefsPath, """
                    user_pref("mail.account.account1.server", "server1");
                    user_pref("mail.account.account1.identities", "id1");
                    user_pref("mail.server.server1.type", "imap");
                    user_pref("mail.server.server1.hostname", "imap.gmail.com");
                    user_pref("mail.server.server1.userName", "test@example.invalid");
                    user_pref("mail.server.server1.port", 993);
                    user_pref("mail.server.server1.socketType", 3);
                    user_pref("mail.identity.id1.useremail", "test@example.invalid");
                    user_pref("mail.identity.id1.fullName", "Test User");
                    user_pref("mail.identity.id1.smtpServer", "smtp1");
                    user_pref("mail.smtpserver.smtp1.hostname", "smtp.gmail.com");
                    user_pref("mail.smtpserver.smtp1.port", 465);
                    user_pref("mail.smtpserver.smtp1.username", "test@example.invalid");
                    user_pref("mail.account.account2.server", "server2");
                    user_pref("mail.server.server2.type", "pop3");
                    """);
                var imported = ThunderbirdImporter.ReadProfile(prefsPath, "Test profile");
                bool passed = imported.Count == 1 && imported[0].Account.Email == "test@example.invalid"
                    && imported[0].Account.ImapHost == "imap.gmail.com"
                    && imported[0].Account.SmtpPort == 587
                    && imported[0].Account.DisplayName == "Test User";
                Console.WriteLine(passed ? "Thunderbird IMAP import check passed" : "Thunderbird IMAP import check failed");
                if (!passed) Environment.ExitCode = 1;
            }
            finally { File.Delete(prefsPath); }
            return;
        }
        if (args.Length > 0 && args[0] is "--context-menu-check" or
            "--context-menu-preview" or "--context-menu-preview-light")
        {
            var checkApp = new App();
            checkApp.InitializeComponent();
            ThemeManager.Initialize(args[0] == "--context-menu-preview-light" ? "light" : "dark");
            var main = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000, Top = -4000, Width = 1100, Height = 750,
                WindowState = WindowState.Normal, ShowInTaskbar = false, ShowActivated = false
            };
            var loaded = typeof(MainWindow).GetMethod("MainWindow_Loaded",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, loaded);
            main.Show();
            var mail = new MailItem { Uid = 42, From = "Test <test@example.invalid>",
                FromAddress = "test@example.invalid", Subject = "Context menu check" };
            var items = (System.Collections.ObjectModel.ObservableCollection<MailItem>)
                typeof(MainWindow).GetField("_visibleMessages", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(main)!;
            items.Add(mail);
            main.UpdateLayout();
            var messageList = (ListBox)main.FindName("MessageList");
            var row = (ListBoxItem?)messageList.ItemContainerGenerator.ContainerFromItem(mail);
            if (row is null) throw new InvalidOperationException("The message row did not render.");
            var rightClick = new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent };
            row.RaiseEvent(rightClick);
            bool passed = rightClick.Handled && row.ContextMenu?.IsOpen == true &&
                row.ContextMenu.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == Localizer.T("rules"));
            if ((args[0] is "--context-menu-preview" or "--context-menu-preview-light") &&
                row.ContextMenu is { } previewMenu)
            {
                previewMenu.Measure(new Size(348, 620));
                int width = Math.Max(348, (int)Math.Ceiling(previewMenu.DesiredSize.Width));
                int height = Math.Max(100, (int)Math.Ceiling(previewMenu.DesiredSize.Height));
                previewMenu.Arrange(new Rect(0, 0, width, height));
                previewMenu.UpdateLayout();
                var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                image.Render(previewMenu);
                string path = args.Length > 1 ? args[1] :
                    Path.Combine(Path.GetTempPath(), "Mailcario-context-menu-preview.png");
                using var previewStream = File.Create(path);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(previewStream);
                Console.WriteLine(path);
            }
            if (row.ContextMenu?.Items.OfType<MenuItem>()
                .FirstOrDefault(item => item.Header?.ToString() == Localizer.T("rules")) is { } rulesItem)
            {
                rulesItem.ApplyTemplate();
                rulesItem.IsSubmenuOpen = true;
                var popup = (System.Windows.Controls.Primitives.Popup?)
                    rulesItem.Template.FindName("PART_Popup", rulesItem);
                passed &= popup?.IsOpen == true && rulesItem.Items.Count == 2;
                rulesItem.IsSubmenuOpen = false;
            }
            else passed = false;
            if (row.ContextMenu is not null) row.ContextMenu.IsOpen = false;
            main.Close();
            ThemeManager.Shutdown();
            Console.WriteLine(passed ? "Message context menu opened on first right click" :
                "Message context menu failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--signatures-preview")
        {
            var previewApp = new App();
            previewApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var managerType = typeof(SmtpService).Assembly.GetType("Mailcario.SignaturesWindow")!;
            var manager = (Window)Activator.CreateInstance(managerType, ["preview@example.invalid"])!;
            Render(manager, 730, 490, args.Length > 1 ? args[1] :
                Path.Combine(Path.GetTempPath(), "Mailcario-signatures-preview.png"));
            ThemeManager.Shutdown();
            return;
        }
        if (args.Length > 0 && args[0] == "--contacts-preview")
        {
            var previewApp = new App();
            previewApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var window = (Window)Activator.CreateInstance(
                typeof(SmtpService).Assembly.GetType("Mailcario.ContactsWindow")!)!;
            Render(window, 720, 490, args.Length > 1 ? args[1] :
                Path.Combine(Path.GetTempPath(), "Mailcario-contacts-preview.png"));
            ThemeManager.Shutdown();
            return;
        }
        if (args.Length > 0 && args[0] == "--color-palette-preview")
        {
            var previewApp = new App();
            previewApp.InitializeComponent();
            ThemeManager.Initialize(args.Length > 2 && args[2] == "light" ? "light" : "dark");
            var account = new MailAccount { Email = "preview@example.invalid" };
            var compose = new ComposeWindow(account, "unused");
            var palette = (FrameworkElement)typeof(ComposeWindow).GetMethod("BuildColorPalette",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(compose, [false])!;
            var wrapper = new Window { Content = palette, Background = (Brush)Application.Current.FindResource("Canvas") };
            Render(wrapper, 283, 395, args.Length > 1 ? args[1] :
                Path.Combine(Path.GetTempPath(), "Mailcario-palette-preview.png"));
            compose.Close();
            ThemeManager.Shutdown();
            return;
        }
        if (args.Length > 0 && args[0] == "--rules-compose-check")
        {
            var checkApp = new App();
            checkApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var sample = new MailAccount { Email = "sender@example.invalid", DisplayName = "Sender" };
            var compose = new ComposeWindow(sample, "unused", "recipient@example.invalid", "Test subject", "Hello");
            bool passed = ((TextBox)compose.FindName("ToBox")).Text == "recipient@example.invalid" &&
                ((TextBox)compose.FindName("SubjectBox")).Text == "Test subject" &&
                ((ComboBox)compose.FindName("FromAccountBox")).Items.Cast<ComboBoxItem>()
                    .Any(item => item.Content?.ToString() == sample.Email);
            var editor = (RichTextBox)compose.FindName("BodyEditor");
            var sourceRun = new Run("Source") { FontWeight = FontWeights.Bold };
            var targetRun = new Run("Target");
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(sourceRun));
            editor.Document.Blocks.Add(new Paragraph(targetRun));
            editor.Selection.Select(sourceRun.ContentStart, sourceRun.ContentEnd);
            typeof(ComposeWindow).GetMethod("FormatPainterButton_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(compose, [new Button(), new RoutedEventArgs(Button.ClickEvent)]);
            editor.Selection.Select(targetRun.ContentStart, targetRun.ContentEnd);
            passed &= new TextRange(targetRun.ContentStart, targetRun.ContentEnd)
                .GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight && weight >= FontWeights.Bold;
            var palette = (Border)typeof(ComposeWindow).GetMethod("BuildColorPalette",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(compose, [false])!;
            var paletteRows = ((StackPanel)palette.Child).Children
                .OfType<System.Windows.Controls.Primitives.UniformGrid>().ToArray();
            passed &= paletteRows.Length == 2 && paletteRows[0].Children.Count == 60 &&
                paletteRows[1].Children.Count == 10;
            var contrastToggle = ((StackPanel)palette.Child).Children.OfType<CheckBox>().First();
            contrastToggle.IsChecked = true;
            passed &= paletteRows[0].Children.OfType<Button>().Any(button => !button.IsEnabled);
            var pickColor = typeof(ComposeWindow).GetMethod("SelectPaletteColor",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            pickColor.Invoke(compose, [false, Color.FromRgb(255, 0, 0)]);
            passed &= (Color?)typeof(ComposeWindow).GetField("_selectedFontColor",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compose) == Color.FromRgb(255, 0, 0);
            passed &= new TextRange(targetRun.ContentStart, targetRun.ContentEnd)
                .GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush selectedFontBrush &&
                selectedFontBrush.Color == Color.FromRgb(255, 0, 0);
            pickColor.Invoke(compose, [false, null]);
            passed &= typeof(ComposeWindow).GetField("_selectedFontColor",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compose) is null;
            ((Button)compose.FindName("ComposeBccButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed &= ((TextBox)compose.FindName("BccBox")).Visibility == Visibility.Visible;
            string attachmentPath = Path.Combine(Path.GetTempPath(), "mailcario-attach-check-" +
                Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(attachmentPath, "sample attachment");
                typeof(ComposeWindow).GetMethod("AddAttachments", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(compose, [new[] { attachmentPath, attachmentPath, Path.GetTempPath() }]);
                passed &= ((ItemsControl)compose.FindName("AttachmentList")).Items.Count == 1;
                typeof(ComposeWindow).GetMethod("RemoveAttachment_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(compose, [new Button { Tag = attachmentPath }, new RoutedEventArgs(Button.ClickEvent)]);
                passed &= ((ItemsControl)compose.FindName("AttachmentList")).Items.Count == 0;
            }
            finally { File.Delete(attachmentPath); }
            var signatureType = typeof(SmtpService).Assembly.GetType("Mailcario.SignatureStore")!;
            var parseSignatures = signatureType.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
            object catalog = parseSignatures.Invoke(null, ["{\"sender@example.invalid\":\"-- Existing signature\"}"])!;
            var accounts = (System.Collections.IDictionary)catalog.GetType().GetProperty("Accounts")!.GetValue(catalog)!;
            var signatureEntries = (System.Collections.IList)accounts["sender@example.invalid"]!;
            passed &= signatureEntries.Count == 1 &&
                signatureEntries[0]!.GetType().GetProperty("Text")!.GetValue(signatureEntries[0])!.ToString() == "-- Existing signature";
            object another = Activator.CreateInstance(signatureEntries[0]!.GetType())!;
            another.GetType().GetProperty("Name")!.SetValue(another, "Work");
            another.GetType().GetProperty("Text")!.SetValue(another, "-- Work");
            signatureEntries.Add(another);
            string serializedCatalog = JsonSerializer.Serialize(catalog, catalog.GetType());
            object restored = parseSignatures.Invoke(null, [serializedCatalog])!;
            var restoredAccounts = (System.Collections.IDictionary)restored.GetType().GetProperty("Accounts")!.GetValue(restored)!;
            passed &= ((System.Collections.IList)restoredAccounts["sender@example.invalid"]!).Count == 2;
            var otherSignatures = (System.Collections.IList)Activator.CreateInstance(signatureEntries.GetType())!;
            object otherSignature = Activator.CreateInstance(signatureEntries[0]!.GetType())!;
            otherSignature.GetType().GetProperty("Name")!.SetValue(otherSignature, "Other account");
            otherSignature.GetType().GetProperty("Text")!.SetValue(otherSignature, "-- Other account");
            otherSignatures.Add(otherSignature);
            restoredAccounts["other@example.invalid"] = otherSignatures;
            var choices = (System.Collections.IList)signatureType.GetMethod("Choices", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [restored])!;
            passed &= choices.Count == 3 && choices.Cast<object>().Any(choice =>
                choice.GetType().GetProperty("AccountEmail")!.GetValue(choice)!.ToString() == "other@example.invalid");
            var signatureMenu = (ContextMenu)typeof(ComposeWindow).GetMethod("CreateSignatureMenu",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(compose,
                [((Button)compose.FindName("ComposeSignatureButton")), choices])!;
            passed &= signatureMenu.Items.Count == 7 &&
                ReferenceEquals(signatureMenu.Style, compose.FindResource("MailMessageContextMenuStyle")) &&
                signatureMenu.Items.OfType<MenuItem>().All(item =>
                    ReferenceEquals(item.Style, compose.FindResource("MailMessageMenuItemStyle")));
            var managerType = typeof(SmtpService).Assembly.GetType("Mailcario.SignaturesWindow")!;
            var manager = (Window)Activator.CreateInstance(managerType, ["preview@example.invalid"])!;
            managerType.GetMethod("AddEntry", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, null);
            managerType.GetMethod("AddEntry", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, null);
            var managerEntries = (System.Collections.IList)managerType.GetField("_entries",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            passed &= managerEntries.Count == 2 &&
                managerEntries[0]!.GetType().GetProperty("Name")!.GetValue(managerEntries[0])!.ToString() !=
                managerEntries[1]!.GetType().GetProperty("Name")!.GetValue(managerEntries[1])!.ToString();
            managerType.GetField("_dirty", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, false);
            manager.Close();
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Bold(new Run("<script>alert(1)</script>"))));
            var documentType = typeof(SmtpService).Assembly.GetType("Mailcario.ComposeDocument")!;
            string html = (string)documentType.GetMethod("ToHtml", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [doc])!;
            passed &= html.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", StringComparison.Ordinal) &&
                !html.Contains("<script>", StringComparison.Ordinal) &&
                html.Contains("font-weight:bold", StringComparison.Ordinal);
            var styledRun = new Run("A<&>") { FontFamily = new FontFamily("Georgia"), FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(54, 142, 207)),
                Background = new SolidColorBrush(Color.FromRgb(255, 238, 135)) };
            var styledDocument = new FlowDocument();
            styledDocument.Blocks.Add(new Paragraph(styledRun) { Margin = new Thickness(24, 0, 0, 0) });
            string styledHtml = (string)documentType.GetMethod("ToHtml", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [styledDocument])!;
            passed &= styledHtml.Contains("font-family:'Georgia'", StringComparison.Ordinal) &&
                styledHtml.Contains("font-size:18px", StringComparison.Ordinal) &&
                styledHtml.Contains("color:#368ECF", StringComparison.Ordinal) &&
                styledHtml.Contains("background-color:#FFEE87", StringComparison.Ordinal) &&
                styledHtml.Contains("margin-left:24px", StringComparison.Ordinal) &&
                styledHtml.Contains("A&lt;&amp;&gt;", StringComparison.Ordinal);
            using var message = SmtpService.CreateMessage(sample, "recipient@example.invalid", "", "Subject", "Body");
            message.AlternateViews.Add(System.Net.Mail.AlternateView.CreateAlternateViewFromString(
                "<p>Body</p>", Encoding.UTF8, "text/html"));
            message.Attachments.Add(new System.Net.Mail.Attachment(
                new MemoryStream(Encoding.UTF8.GetBytes("file content")), "rapport-été.txt", "text/plain"));
            message.Priority = System.Net.Mail.MailPriority.High;
            message.Headers.Add("Disposition-Notification-To", sample.Email);
            message.Headers["Message-ID"] = "<test-copy@mailcario.local>";
            var smtpType = typeof(SmtpService).Assembly.GetType("Mailcario.OAuthSmtpSender")!;
            string mime = (string)smtpType.GetMethod("BuildMime", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [message])!;
            passed &= mime.Contains("multipart/mixed", StringComparison.Ordinal) &&
                mime.Contains("multipart/alternative", StringComparison.Ordinal) &&
                mime.Contains("filename*=UTF-8''rapport-%C3%A9t%C3%A9.txt", StringComparison.Ordinal) &&
                mime.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("file content")), StringComparison.Ordinal) &&
                mime.Contains("Importance: high", StringComparison.Ordinal) &&
                mime.Contains("Message-ID: <test-copy@mailcario.local>", StringComparison.Ordinal) &&
                mime.Contains("Disposition-Notification-To: <sender@example.invalid>", StringComparison.Ordinal);
            var rule = new MailRule { AccountEmail = sample.Email, SenderContains = "sender@example.invalid",
                SubjectContains = "Project", Action = "mark-read", MinimumUid = 10 };
            passed &= rule.Matches(new MailItem { Uid = 11, From = "Sender <sender@example.invalid>", Subject = "Project update" }) &&
                !rule.Matches(new MailItem { Uid = 10, From = "Sender <sender@example.invalid>", Subject = "Project update" }) &&
                !rule.Matches(new MailItem { Uid = 11, From = "other@example.invalid", Subject = "Project update" });
            var rulesWindow = new RulesWindow(sample,
                [new MailFolder { ServerName = "INBOX" }, new MailFolder { ServerName = "Archive" }],
                10, 1, new MailItem { Uid = 10, FromAddress = "sender@example.invalid" });
            passed &= rulesWindow.Title == Localizer.T("rules");
            rulesWindow.Close();
            var main = new MainWindow();
            var messageStyle = ((ListBox)main.FindName("MessageList")).ItemContainerStyle;
            var accountStyle = ((ListBox)main.FindName("AccountList")).ItemContainerStyle;
            passed &= messageStyle.Setters.OfType<EventSetter>()
                .Any(setter => setter.Event == UIElement.PreviewMouseRightButtonDownEvent) &&
                !accountStyle.Setters.OfType<EventSetter>()
                    .Any(setter => setter.Event == UIElement.PreviewMouseRightButtonDownEvent);
            var row = new ListBoxItem { DataContext = new MailItem { Uid = 11,
                From = "Sender <sender@example.invalid>", FromAddress = "sender@example.invalid",
                Subject = "Project update" } };
            typeof(MainWindow).GetMethod("MessageItem_PreviewMouseRightButtonDown",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main,
                [row, new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent }]);
            passed &= row.ContextMenu is { } context && context.Items.Count >= 15 &&
                context.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == Localizer.T("rules"));
            typeof(MainWindow).GetField("_accounts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(main, new[] { sample });
            typeof(MainWindow).GetMethod("BeginSync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [sample.Email]);
            passed &= ((ProgressBar)main.FindName("SyncProgress")).Visibility == Visibility.Visible &&
                ((TextBlock)main.FindName("SyncStatusText")).Text.Contains(sample.Email, StringComparison.Ordinal);
            typeof(MainWindow).GetMethod("SetAccountConnected", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [sample.Email, true, null]);
            typeof(MainWindow).GetMethod("EndSync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, null);
            passed &= ((ProgressBar)main.FindName("SyncProgress")).Visibility == Visibility.Collapsed &&
                ((TextBlock)main.FindName("ConnectionStatusText")).Text == Localizer.T("connected");
            typeof(MainWindow).GetMethod("SetAccountConnected", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [sample.Email, false, "Test IMAP failure"]);
            ((Button)main.FindName("ConnectionStatusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed &= ((StackPanel)main.FindName("ConnectionDetailsPanel")).Children.OfType<StackPanel>()
                .SelectMany(panel => panel.Children.OfType<TextBlock>())
                .Any(label => label.Text.Contains("Test IMAP failure", StringComparison.Ordinal));
            typeof(MainWindow).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(main, new[] { new MailItem { Uid = 1, From = "Alice <alice@example.invalid>", Subject = "Project update" },
                    new MailItem { Uid = 2, From = "Bob <bob@example.invalid>", Subject = "Other" } });
            ((TextBox)main.FindName("SearchBox")).Text = "Project";
            passed &= ((ListBox)main.FindName("MessageList")).Items.Count == 1 &&
                ((System.Windows.Threading.DispatcherTimer)typeof(MainWindow)
                .GetField("_searchTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!).IsEnabled;
            ((TextBox)main.FindName("SearchBox")).Clear();
            passed &= ((ListBox)main.FindName("MessageList")).Items.Count == 2;
            var settingsWindow = new SettingsWindow(new ClientSettings(), [sample]);
            passed &= ((ListBox)settingsWindow.FindName("AccountsList")).Items.Count == 1 &&
                ((Button)settingsWindow.FindName("RemoveAccountButton")).Content?.ToString() == Localizer.T("removeAccount");
            settingsWindow.Close();
            var insertSignature = typeof(ComposeWindow).GetMethod("InsertSignature",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            insertSignature.Invoke(compose, ["-- First"]);
            insertSignature.Invoke(compose, ["-- Second"]);
            string composedText = new TextRange(((RichTextBox)compose.FindName("BodyEditor")).Document.ContentStart,
                ((RichTextBox)compose.FindName("BodyEditor")).Document.ContentEnd).Text;
            passed &= composedText.Contains("-- Second", StringComparison.Ordinal) &&
                !composedText.Contains("-- First", StringComparison.Ordinal);
            typeof(ComposeWindow).GetMethod("ClearSignature", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(compose, null);
            composedText = new TextRange(((RichTextBox)compose.FindName("BodyEditor")).Document.ContentStart,
                ((RichTextBox)compose.FindName("BodyEditor")).Document.ContentEnd).Text;
            passed &= !composedText.Contains("-- Second", StringComparison.Ordinal);
            ((TextBox)compose.FindName("ToBox")).Text = "invalid recipient";
            typeof(ComposeWindow).GetMethod("CheckNamesButton_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(compose, [new Button(), new RoutedEventArgs(Button.ClickEvent)]);
            passed &= ((TextBlock)compose.FindName("StatusLabel")).Text.Contains("invalid recipient",
                StringComparison.Ordinal);
            var contactsWindow = (Window)Activator.CreateInstance(
                typeof(SmtpService).Assembly.GetType("Mailcario.ContactsWindow")!)!;
            passed &= contactsWindow.Title == Localizer.T("addressBook");
            contactsWindow.Close();
            main.Close();
            compose.Close();
            ThemeManager.Shutdown();
            Console.WriteLine(passed ? "Rules and compose check passed" : "Rules and compose check failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] is "--compose-preview" or "--compose-preview-light")
        {
            var previewApp = new App();
            previewApp.InitializeComponent();
            ThemeManager.Initialize(args[0] == "--compose-preview-light" ? "light" : "dark");
            var sample = new MailAccount { Email = "alex@example.invalid", DisplayName = "Alex" };
            var window = new ComposeWindow(sample, "unused", "contact@example.invalid", "Projet Mailcario",
                "Bonjour,\n\nVoici les informations demandées.") {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000,
                ShowInTaskbar = false, ShowActivated = false
            };
            var fromBox = (ComboBox)window.FindName("FromAccountBox");
            fromBox.Items.Clear();
            fromBox.Items.Add(new ComboBoxItem { Content = sample.Email, Tag = sample });
            fromBox.SelectedIndex = 0;
            window.Show();
            window.UpdateLayout();
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            image.Render(window);
            string path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "Mailcario-compose-preview.png");
            using (var stream = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }
            Console.WriteLine(path);
            window.Close();
            ThemeManager.Shutdown();
            return;
        }
        if (args.Length > 0 && args[0] == "--account-folder-check")
        {
            var checkApp = new App();
            checkApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var accountWindow = new AccountWindow();
            ((Button)accountWindow.FindName("GooglePreset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool passed = ((TextBox)accountWindow.FindName("ImapBox")).Text == "imap.gmail.com" &&
                ((TextBox)accountWindow.FindName("SmtpBox")).Text == "smtp.gmail.com" &&
                ((Button)accountWindow.FindName("GoogleHelpButton")).Visibility == Visibility.Collapsed &&
                ((PasswordBox)accountWindow.FindName("PasswordInput")).Visibility == Visibility.Collapsed &&
                ((Button)accountWindow.FindName("GoogleAuthSwitchButton")).Visibility == Visibility.Visible;
            ((Button)accountWindow.FindName("GoogleAuthSwitchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed &= ((Button)accountWindow.FindName("GoogleHelpButton")).Visibility == Visibility.Visible &&
                ((PasswordBox)accountWindow.FindName("PasswordInput")).Visibility == Visibility.Visible;
            string authorizationUrl = (string)typeof(GoogleOAuthService).GetMethod("BuildAuthorizationUri",
                BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                ["sample.apps.googleusercontent.com", "demo@example.invalid", "http://127.0.0.1:12345/", "challenge", "state"])!;
            passed &= authorizationUrl.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", StringComparison.Ordinal) &&
                authorizationUrl.Contains("scope=https%3A%2F%2Fmail.google.com%2F", StringComparison.Ordinal) &&
                authorizationUrl.Contains("code_challenge_method=S256", StringComparison.Ordinal) &&
                authorizationUrl.Contains("access_type=offline", StringComparison.Ordinal) &&
                authorizationUrl.Contains("state=state", StringComparison.Ordinal) &&
                !authorizationUrl.Contains("client_secret", StringComparison.OrdinalIgnoreCase);
            ((Button)accountWindow.FindName("IcloudPreset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)accountWindow.FindName("EmailBox")).Text = "demo@example.invalid";
            passed &= ((TextBox)accountWindow.FindName("ImapBox")).Text == "imap.mail.me.com" &&
                ((TextBox)accountWindow.FindName("UsernameBox")).Text == "demo" &&
                ((TextBox)accountWindow.FindName("SmtpUsernameBox")).Text == "demo@example.invalid";
            ((Button)accountWindow.FindName("MicrosoftPreset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed &= ((TextBox)accountWindow.FindName("ImapBox")).Text == "outlook.office365.com" &&
                !((Button)accountWindow.FindName("ConnectButton")).IsEnabled &&
                accountWindow.FindName("OAuthClientPanel") is null &&
                ((PasswordBox)accountWindow.FindName("PasswordInput")).Visibility == Visibility.Collapsed;
            accountWindow.Close();
            var main = new MainWindow();
            const string fullLink = "https://example.org/very/long/path?source=message&campaign=mailcario";
            typeof(MainWindow).GetMethod("ShowLinkTarget", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [fullLink, false]);
            passed &= ((TextBlock)main.FindName("LinkPreviewText")).Text == fullLink &&
                main.FindName("LinkStatusPanel") is null;
            typeof(MainWindow).GetMethod("ClearLinkTarget", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, null);
            passed &= !((System.Windows.Controls.Primitives.Popup)main.FindName("LinkPreviewPopup")).IsOpen;
            typeof(MainWindow).GetField("_allFolders", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main,
                new[] { new MailFolder { ServerName = "INBOX" },
                    new MailFolder { ServerName = "INBOX/Projects" },
                    new MailFolder { ServerName = "INBOX/Projects/2026" } });
            var rebuild = typeof(MainWindow).GetMethod("ApplyFolderList", BindingFlags.NonPublic | BindingFlags.Instance)!;
            rebuild.Invoke(main, null);
            var folderList = (ListBox)main.FindName("FolderList");
            passed &= folderList.Items.Count == 3 && ((MailFolder)folderList.Items[0]).HasChildren;
            ((Button)main.FindName("AccountFolderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            passed &= folderList.Visibility == Visibility.Collapsed;
            ((Button)main.FindName("AccountFolderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var arrow = new Button { Tag = folderList.Items[0] };
            typeof(MainWindow).GetMethod("FolderExpand_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [arrow, new RoutedEventArgs(Button.ClickEvent)]);
            passed &= folderList.Visibility == Visibility.Visible && folderList.Items.Count == 1;
            var state = (FolderViewState)typeof(MainWindow).GetField("_folderView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            state.CollapsedFolders.Clear();
            state.HiddenFolders.Add("INBOX/Projects");
            rebuild.Invoke(main, null);
            passed &= folderList.Items.Count == 1;
            var savedView = new ClientSettings();
            savedView.FolderViews["hello@example.invalid"] = new FolderViewState
            {
                AccountExpanded = false,
                HiddenFolders = ["INBOX/Projects"],
                CollapsedFolders = ["INBOX"]
            };
            var restoredView = JsonSerializer.Deserialize<ClientSettings>(JsonSerializer.Serialize(savedView))!;
            passed &= !restoredView.FolderViews["hello@example.invalid"].AccountExpanded &&
                restoredView.FolderViews["hello@example.invalid"].HiddenFolders.Contains("INBOX/Projects") &&
                restoredView.FolderViews["hello@example.invalid"].CollapsedFolders.Contains("INBOX");
            using var smtpMessage = SmtpService.CreateMessage(
                new MailAccount { Email = "sender@example.invalid", DisplayName = "Équipe Mailcario" },
                "recipient@example.invalid", "", "Réunion demain", "Bonjour !", "<original@example.invalid>");
            var mimeType = typeof(SmtpService).Assembly.GetType("Mailcario.OAuthSmtpSender")!;
            string mime = (string)mimeType.GetMethod("BuildMime", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [smtpMessage])!;
            passed &= mime.Contains("Subject: =?UTF-8?B?", StringComparison.Ordinal) &&
                mime.Contains("In-Reply-To: <original@example.invalid>", StringComparison.Ordinal) &&
                mime.Contains("Content-Transfer-Encoding: base64", StringComparison.Ordinal) &&
                !mime.Contains("Réunion", StringComparison.Ordinal);
            using var inviteReply = CalendarSupport.CreateReply(
                new MailAccount { Email = "sender@example.invalid", DisplayName = "Équipe Mailcario" },
                new CalendarInvite { Uid = "meeting-1", Summary = "Réunion", Organizer = "organizer@example.invalid" },
                "ACCEPTED");
            string calendarMime = (string)mimeType.GetMethod("BuildMime", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [inviteReply])!;
            passed &= calendarMime.Contains("multipart/alternative", StringComparison.Ordinal) &&
                calendarMime.Contains("text/calendar; charset=utf-8; method=REPLY", StringComparison.Ordinal) &&
                calendarMime.Contains("QkVHSU46VkNBTEVOREFS", StringComparison.Ordinal);
            main.Close();
            ThemeManager.Shutdown();
            Console.WriteLine(passed ? "Account presets and folder view passed" : "Account presets and folder view failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--webview-check")
        {
            var webApp = new App();
            webApp.InitializeComponent();
            string outputImage = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "Mailcario-webview-check.png");
            var window = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000,
                Width = 1100, Height = 760, WindowState = WindowState.Normal,
                ShowInTaskbar = false, ShowActivated = false
            };
            var loadedMethod = typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!;
            window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, loadedMethod);
            window.Loaded += async (_, _) =>
            {
                try
                {
                    ((Grid)window.FindName("ReaderContent")).Visibility = Visibility.Visible;
                    ((StackPanel)window.FindName("ReaderEmptyState")).Visibility = Visibility.Collapsed;
                    ((ScrollViewer)window.FindName("PlainBodyScroller")).Visibility = Visibility.Collapsed;
                    var view = (WebView2)window.FindName("HtmlWebView");
                    view.Visibility = Visibility.Visible;
                    var initialize = typeof(MainWindow).GetMethod("InitializeWebViewAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    await (Task)initialize.Invoke(window, null)!;
                    string html = "<html><head><style>body{background:#252525;color:#eee;font-family:Arial;background-image:url(https://tracker.example/css)}" +
                        "table{width:500px;margin:20px auto;border-collapse:collapse}td{padding:16px;border-bottom:1px solid #555}" +
                        "h1{font-size:24px;color:#70a7ef}</style></head><body><table><tr><td><h1>Votre alerte Emploi</h1></td></tr>" +
                        "<tr><td><img src='https://cdn.example.org/logo.ico' width='40' height='40'> Responsable d'application (F/H)</td></tr>" +
                        "<tr><td>Ingénieur support applications</td></tr></table>" +
                        "<img src='https://tracker.example/pixel' width='1' height='1'></body></html>";
                    string iconData = "data:image/x-icon;base64," + Convert.ToBase64String(
                        File.ReadAllBytes(Path.Combine("tools", "Mailcario", "Assets", "Mailcario.ico")));
                    string safe = HtmlTemplateRenderer.Build(html,
                        new Dictionary<string, string> { ["https://cdn.example.org/logo.ico"] = iconData },
                        SafeHtmlRenderer.Render(html).DownloadableImages);
                    typeof(MainWindow).GetField("_currentHtml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, safe);
                    var core = view.CoreWebView2;
                    int blockedExternalRequests = 0;
                    bool unblockedExternalRequest = false;
                    core.WebResourceRequested += (_, request) =>
                    {
                        if (request.Request.Uri.StartsWith("https://mailcario.invalid/message/", StringComparison.OrdinalIgnoreCase)) return;
                        blockedExternalRequests++;
                        if (request.Response?.StatusCode != 403) unblockedExternalRequest = true;
                    };
                    core.NavigationCompleted += async (_, e) =>
                    {
                        try
                        {
                            if (!e.IsSuccess) throw new InvalidOperationException("The isolated HTML navigation failed.");
                            if (unblockedExternalRequest) throw new InvalidOperationException("An external request was not blocked.");
                            await Task.Delay(300);
                            using var stream = File.Create(outputImage);
                            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                            Console.WriteLine($"WebView2 rendering passed ({blockedExternalRequests} external requests intercepted): " + outputImage);
                        }
                        catch (Exception ex) { Console.WriteLine("WebView2 rendering failed: " + ex.Message); Environment.ExitCode = 1; }
                        finally { window.Close(); System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); }
                    };
                    core.Navigate("https://mailcario.invalid/message/1");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WebView2 rendering failed: " + ex.Message);
                    Environment.ExitCode = 1;
                    window.Close();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                }
            };
            window.Show();
            System.Windows.Threading.Dispatcher.Run();
            return;
        }
        if (args.Length > 0 && args[0] == "--storage-check")
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!local.Contains("\\Packages\\sandbox.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Storage check is restricted to the isolated sandbox profile.");
            string folder = Path.Combine(local, "Mailcario");
            string accountFile = Path.Combine(folder, "accounts.json");
            string legacyAccountFile = Path.Combine(folder, "account.json");
            string legacyPasswordFile = Path.Combine(folder, "password.bin");
            if (File.Exists(accountFile) || File.Exists(legacyAccountFile) || File.Exists(legacyPasswordFile))
                throw new InvalidOperationException("The isolated profile already contains accounts.");
            var account = new MailAccount { Email = "mailcario-check@example.invalid", UserName = "mailcario-check@example.invalid" };
            var second = new MailAccount { Email = "mailcario-second@example.invalid", UserName = "mailcario-second@example.invalid" };
            const string password = "sample-password-only";
            Directory.CreateDirectory(folder);
            File.WriteAllText(legacyAccountFile, JsonSerializer.Serialize(account));
            File.WriteAllBytes(legacyPasswordFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(password),
                Encoding.UTF8.GetBytes("Mailcario:" + account.Email.ToLowerInvariant()), DataProtectionScope.CurrentUser));
            bool legacyPassed = AccountStore.LoadAccounts().Count == 1 && AccountStore.LoadPassword(account.Email) == password;
            AccountStore.SaveAccount(second, "another-sample-password");
            AccountStore.SaveActiveEmail(account.Email);
            bool passed = legacyPassed && AccountStore.LoadAccounts().Count == 2 &&
                AccountStore.LoadActiveEmail() == account.Email &&
                AccountStore.LoadPassword(account.Email) == password &&
                AccountStore.LoadPassword(second.Email) == "another-sample-password";
            AccountStore.SaveAccount(account, "migrated-password");
            passed &= AccountStore.LoadAccounts().Count == 2 && AccountStore.LoadPassword(account.Email) == "migrated-password";
            passed &= AccountStore.RemoveAccount(second.Email) && AccountStore.LoadAccounts().Count == 1 &&
                AccountStore.LoadPassword(second.Email) is null;
            passed &= AccountStore.RemoveAccount(account.Email) && AccountStore.LoadAccounts().Count == 0 &&
                AccountStore.LoadPassword(account.Email) is null && AccountStore.LoadActiveEmail() is null &&
                !File.Exists(legacyAccountFile) && !File.Exists(legacyPasswordFile);
            File.Delete(accountFile);
            File.Delete(Path.Combine(folder, "active-account.txt"));
            File.Delete(legacyAccountFile);
            File.Delete(legacyPasswordFile);
            foreach (string email in new[] { account.Email, second.Email })
            {
                string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant())));
                File.Delete(Path.Combine(folder, "credentials", hash + ".bin"));
            }
            Console.WriteLine(passed ? "Storage round-trip passed" : "Storage round-trip failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        if (args.Length > 0 && args[0] == "--security-check")
        {
            string hostile = "<p>Hello <b>world</b></p>" +
                "<img src='https://cdn.example.org/banner.png' width='600' height='200'>" +
                "<img src='https://tracker.example/pixel?id=1' width='1' height='1'>" +
                "<img src='https://cdn.example.org/hidden.png' style='display:none'>" +
                "<img src=x onerror='alert(1)'>" +
                "<script>maliciousScript()</script><svg><script>svgAttack()</script></svg>" +
                "<a href='javascript:alert(1)'>unsafe</a>" +
                "<a href='file:///C:/secret'>local</a>" +
                "<a data-href='https://tracker.example'>attribute trick</a>" +
                "<a href='https://example.org/path'>safe</a>";
            var rendered = SafeHtmlRenderer.Render(hostile);
            string visible = new TextRange(rendered.Document.ContentStart, rendered.Document.ContentEnd).Text;
            var links = rendered.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines.OfType<Hyperlink>()).ToList();
            bool passed = rendered.BlockedImages == 2 && visible.Contains("Hello world") &&
                rendered.TrackingPixels == 2 && rendered.DownloadableImages.Count == 1 &&
                rendered.DownloadableImages[0].AbsoluteUri == "https://cdn.example.org/banner.png" &&
                !visible.Contains("maliciousScript") && !visible.Contains("svgAttack") &&
                links.Count == 1 && links[0].NavigateUri?.AbsoluteUri == "https://example.org/path" &&
                !rendered.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).Any(i => i is InlineUIContainer);
            var bitmap = new WriteableBitmap(10, 10, 96, 96, PixelFormats.Bgra32, null);
            bitmap.Freeze();
            var inlinePng = new PngBitmapEncoder();
            inlinePng.Frames.Add(BitmapFrame.Create(bitmap));
            using var inlineBytes = new MemoryStream();
            inlinePng.Save(inlineBytes);
            string inlineMime = "MIME-Version: 1.0\r\nContent-Type: multipart/related; boundary=mailcario-test\r\n\r\n" +
                "--mailcario-test\r\nContent-Type: text/html; charset=utf-8\r\n\r\n" +
                "<p>Signature <img src=\"cid:logo@example.invalid\"></p>\r\n" +
                "--mailcario-test\r\nContent-Type: image/png\r\nContent-ID: <logo@example.invalid>\r\n" +
                "Content-Transfer-Encoding: base64\r\nContent-Disposition: inline\r\n\r\n" +
                Convert.ToBase64String(inlineBytes.ToArray()) + "\r\n--mailcario-test--\r\n";
            MailContent signature = MimeReader.ReadMessage(Encoding.UTF8.GetBytes(inlineMime));
            string newsletter = "List-Unsubscribe: <https://newsletter.example/unsubscribe?id=123>, <mailto:leave@example.org>\r\n" +
                "List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\nContent-Type: text/plain\r\n\r\nHello";
            UnsubscribeAction? action = MimeReader.ReadMessage(Encoding.UTF8.GetBytes(newsletter)).Unsubscribe;
            passed &= action?.Url.AbsoluteUri == "https://newsletter.example/unsubscribe?id=123" && action.OneClick;
            passed &= MimeReader.ReadMessage(Encoding.UTF8.GetBytes(
                "List-Unsubscribe: <javascript:alert(1)>, <http://127.0.0.1/unsubscribe>\r\n\r\nHello")).Unsubscribe is null;
            passed &= MimeReader.ReadMessage(Encoding.UTF8.GetBytes(
                "List-Unsubscribe: <mailto:leave@example.org>\r\n\r\nHello")).Unsubscribe?.Url.Scheme == "mailto";
            const string inlineId = "cid:logo@example.invalid";
            var hiddenSignature = SafeHtmlRenderer.Render(signature.HtmlText);
            RemoteMailImage? embeddedImage = signature.InlineImages.TryGetValue(inlineId, out var embedded)
                ? RemoteImageLoader.LoadEmbedded(embedded) : null;
            passed &= hiddenSignature.BlockedImages == 1 &&
                hiddenSignature.DownloadableImages.Count == 0 &&
                hiddenSignature.InlineImageIds.Contains(inlineId) && embeddedImage is not null;
            var securityApp = new App();
            securityApp.InitializeComponent();
            ThemeManager.Initialize("dark");
            var signatureWindow = new MainWindow();
            typeof(MainWindow).GetField("_selectedContent", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(signatureWindow, signature);
            typeof(MainWindow).GetMethod("ApplyRenderedMail", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(signatureWindow, [hiddenSignature]);
            passed &= ((Button)signatureWindow.FindName("LoadImagesButton")).Visibility == Visibility.Visible;
            signatureWindow.Close();
            ThemeManager.Shutdown();
            if (embeddedImage is not null)
            {
                var visibleSignature = SafeHtmlRenderer.Render(signature.HtmlText,
                    new Dictionary<string, BitmapSource> { [inlineId] = embeddedImage.Bitmap });
                string signatureTemplate = HtmlTemplateRenderer.Build(signature.HtmlText,
                    new Dictionary<string, string> { [inlineId] = embeddedImage.DataUri }, []);
                passed &= visibleSignature.BlockedImages == 0 &&
                    signatureTemplate.Contains("data:image/png;base64,", StringComparison.Ordinal) &&
                    !signatureTemplate.Contains("cid:", StringComparison.OrdinalIgnoreCase);
            }
            string locationMime = inlineMime.Replace("cid:logo@example.invalid", "brand.png", StringComparison.Ordinal)
                .Replace("Content-ID: <logo@example.invalid>", "Content-Location: brand.png", StringComparison.Ordinal);
            MailContent locatedSignature = MimeReader.ReadMessage(Encoding.UTF8.GetBytes(locationMime));
            passed &= locatedSignature.InlineImages.ContainsKey("location:brand.png") &&
                SafeHtmlRenderer.Render(locatedSignature.HtmlText).InlineImageIds.Contains("location:brand.png");
            string dataSource = "data:image/png;base64," + Convert.ToBase64String(inlineBytes.ToArray());
            MailContent dataSignature = MimeReader.ReadMessage(Encoding.UTF8.GetBytes(
                "Content-Type: text/html; charset=utf-8\r\n\r\n<img src=\"" + dataSource + "\">"));
            string? dataId = InlineMailImage.NormalizeSource(dataSource);
            passed &= dataId is not null && dataSignature.InlineImages.ContainsKey(dataId) &&
                SafeHtmlRenderer.Render(dataSignature.HtmlText).InlineImageIds.Contains(dataId);
            string partialPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("Bonjour, ceci est un extrait du message."));
            string partialMime = "Content-Type: text/plain; charset=utf-8\r\n" +
                "Content-Transfer-Encoding: base64\r\n\r\n" + partialPayload[..^2];
            passed &= MimeReader.ReadMessage(Encoding.UTF8.GetBytes(partialMime)).PlainText.StartsWith(
                "Bonjour", StringComparison.Ordinal);
            var withImages = SafeHtmlRenderer.Render(hostile,
                new Dictionary<string, BitmapSource> { ["https://cdn.example.org/banner.png"] = bitmap });
            passed &= withImages.BlockedImages == 1 && withImages.TrackingPixels == 2 &&
                withImages.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineUIContainer>()
                    .Count() == 1;
            var sharedTrackingUrl = SafeHtmlRenderer.Render(
                "<img src='https://cdn.example.org/shared.png' width='600'>" +
                "<img src='https://cdn.example.org/shared.png' width='1' height='1'>");
            passed &= sharedTrackingUrl.DownloadableImages.Count == 0 && sharedTrackingUrl.TrackingPixels == 2;
            string template = "<html><head><style>td{background:#252525;color:#eee}</style></head>" +
                "<body><table><tr><td onclick='alert(1)'>Template <img src='https://cdn.example.org/banner.png'></td></tr></table>" +
                "<script>attack()</script><iframe src='https://tracker.example/frame'></iframe></body></html>";
            string protectedTemplate = HtmlTemplateRenderer.Build(template, new Dictionary<string, string>(),
                SafeHtmlRenderer.Render(template).DownloadableImages);
            passed &= protectedTemplate.Contains("<table") && protectedTemplate.Contains("background:#252525") &&
                !protectedTemplate.Contains("onclick") && !protectedTemplate.Contains("attack()") &&
                !protectedTemplate.Contains("<iframe") && !protectedTemplate.Contains("https://cdn.example.org/banner.png") &&
                protectedTemplate.Contains("data:image/gif;base64,");
            string linkTemplate = HtmlTemplateRenderer.Build(
                "<a href='https://example.org/path?a=1&amp;b=2'>Visit</a>" +
                "<a href='javascript:alert(1)'>Unsafe</a>" +
                "<a href='https://mailcario.invalid/message/1'>Internal</a>",
                new Dictionary<string, string>(), []);
            var safeTargets = HtmlTemplateRenderer.SafeLinkTargets(linkTemplate);
            passed &= safeTargets.Count == 1 && safeTargets.Contains("https://example.org/path?a=1&b=2");
            var nestedFolder = new MailFolder { ServerName = "INBOX/Projets/&AMk-tudes", Delimiter = '/' };
            passed &= nestedFolder.DisplayName == "Études" && nestedFolder.Depth == 2 &&
                ImapFolderName.Decode("R&AOk-sum&AOk-") == "Résumé" &&
                ImapFolderName.Decode(ImapFolderName.Encode("Projets & études")) == "Projets & études";
            bool localAddressRejected = false;
            try { RemoteImageLoader.LoadAsync(new Uri("http://127.0.0.1/private"), CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException) { localAddressRejected = true; }
            passed &= localAddressRejected;
            Console.WriteLine(passed ? "HTML security check passed" : "HTML security check failed");
            if (!passed) Environment.ExitCode = 1;
            return;
        }
        string output = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "MailcarioPreview");
        Directory.CreateDirectory(output);
        var app = new App();
        app.InitializeComponent();
        ThemeManager.Initialize("dark");
        Render(new MainWindow(), 1320, 780, Path.Combine(output, "main.png"));
        var accountsPreview = new MainWindow();
        var sampleAccounts = new[]
        {
            new MailAccount { Email = "first@example.invalid" },
            new MailAccount { Email = "second@example.invalid" }
        };
        typeof(MainWindow).GetField("_accounts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(accountsPreview, sampleAccounts);
        typeof(MainWindow).GetField("_account", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(accountsPreview, sampleAccounts[0]);
        typeof(MainWindow).GetMethod("RefreshAccountList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(accountsPreview, null);
        Render(accountsPreview, 1320, 780, Path.Combine(output, "accounts-pane.png"));
        var populated = new MainWindow();
        typeof(MainWindow).GetField("_allFolders", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(populated, new[]
        {
            new MailFolder { ServerName = "INBOX", Delimiter = '/' },
            new MailFolder { ServerName = "INBOX/Projets", Delimiter = '/' },
            new MailFolder { ServerName = "INBOX/Projets/&AMk-tudes", Delimiter = '/' }
        });
        typeof(MainWindow).GetMethod("ApplyFolderList", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(populated, null);
        var list = (ListBox)populated.FindName("MessageList");
        var sampleMessages = new[]
        {
            new MailItem { From = "Camille Dupont <camille@example.invalid>", FromAddress = "camille@example.invalid", To = "hello@example.invalid", Subject = "Point sur le projet", Date = DateTimeOffset.Now, Uid = 1 },
            new MailItem { From = "Alex Martin <alex@example.invalid>", FromAddress = "alex@example.invalid", To = "hello@example.invalid", Subject = "Invitation : réunion de jeudi", Date = DateTimeOffset.Now.AddHours(-3), Uid = 2, IsRead = true },
            new MailItem { From = "Équipe Mailcario <news@example.invalid>", FromAddress = "news@example.invalid", To = "hello@example.invalid", Subject = "Bienvenue sur Mailcario", Date = DateTimeOffset.Now.AddDays(-1), Uid = 3, IsRead = true }
        };
        typeof(MainWindow).GetField("_messages", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(populated, sampleMessages);
        typeof(MainWindow).GetMethod("FilterMessages", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(populated, null);
        list.SelectedIndex = 0;
        ((TextBlock)populated.FindName("MessageEmptyLabel")).Visibility = Visibility.Collapsed;
        ((TextBlock)populated.FindName("BodyLabel")).Text = "Bonjour,\n\nVoici le compte rendu de notre échange. Nous pouvons avancer sur les prochaines étapes cette semaine.\n\nÀ bientôt,\nCamille";
        ((TextBlock)populated.FindName("EmailLabel")).Text = "hello@example.invalid";
        ((TextBlock)populated.FindName("CountLabel")).Text = "3 messages";
        ((TextBlock)populated.FindName("StatusLabel")).Text = "3 messages · Prêt";
        ((TextBlock)populated.FindName("ConnectionStatusText")).Text = "Connecté";
        ((System.Windows.Shapes.Ellipse)populated.FindName("ConnectionDot")).Fill =
            (Brush)populated.FindResource("IconSync");
        Render(populated, 1320, 780, Path.Combine(output, "main-populated.png"));
        var searchDark = new MainWindow();
        ((TextBox)searchDark.FindName("SearchBox")).Text = "Rechercher un message";
        Render(searchDark, 1320, 780, Path.Combine(output, "search-dark.png"));
        var imagePreview = new MainWindow();
        var imageMail = SafeHtmlRenderer.Render("<p>Bonjour, voici les nouvelles du jour.</p>" +
            "<img src='https://cdn.example.org/photo.jpg' width='600' height='300'>" +
            "<img src='https://tracker.example/pixel' width='1' height='1'>");
        ((Grid)imagePreview.FindName("ReaderContent")).Visibility = Visibility.Visible;
        ((StackPanel)imagePreview.FindName("ReaderEmptyState")).Visibility = Visibility.Collapsed;
        ((TextBlock)imagePreview.FindName("SubjectLabel")).Text = "Nouvelles du jour";
        ((TextBlock)imagePreview.FindName("FromLabel")).Text = "Club exemple";
        ((TextBlock)imagePreview.FindName("SenderAddressLabel")).Text = "club@example.invalid";
        ((TextBlock)imagePreview.FindName("ToLabel")).Text = "À : hello@example.invalid";
        ((TextBlock)imagePreview.FindName("EmailLabel")).Text = "HELLO@example.invalid";
        ((RichTextBox)imagePreview.FindName("HtmlBodyBox")).Document = imageMail.Document;
        ((RichTextBox)imagePreview.FindName("HtmlBodyBox")).Visibility = Visibility.Visible;
        ((ScrollViewer)imagePreview.FindName("PlainBodyScroller")).Visibility = Visibility.Collapsed;
        ((Border)imagePreview.FindName("BlockedImagesPanel")).Visibility = Visibility.Visible;
        ((TextBlock)imagePreview.FindName("BlockedImagesLabel")).Text = string.Format(Localizer.T("blockedImages"), imageMail.BlockedImages);
        ((Button)imagePreview.FindName("LoadImagesButton")).Content = Localizer.T("loadImages");
        ((Button)imagePreview.FindName("LoadImagesButton")).Visibility = Visibility.Visible;
        ((TextBlock)imagePreview.FindName("ImagePrivacyHint")).Visibility = Visibility.Visible;
        ((Border)imagePreview.FindName("TrackingNoticePanel")).Visibility = Visibility.Visible;
        ((TextBlock)imagePreview.FindName("TrackingNoticeLabel")).Text = string.Format(Localizer.T("trackingBlocked"), imageMail.TrackingPixels);
        Render(imagePreview, 1320, 780, Path.Combine(output, "main-images.png"));
        Render(new AccountWindow(), 520, 760, Path.Combine(output, "account.png"));
        Render(new FolderVisibilityWindow(new[]
        {
            new MailFolder { ServerName = "INBOX" },
            new MailFolder { ServerName = "INBOX/Projets" },
            new MailFolder { ServerName = "INBOX/Projets/Archives" }
        }, new[] { "INBOX/Projets/Archives" }), 470, 590, Path.Combine(output, "folder-visibility.png"));
        Render(new OAuthCodeWindow(new MicrosoftDeviceCode("preview", "ABCD-EFGH",
            new Uri("https://microsoft.com/devicelogin"), 5, DateTimeOffset.UtcNow.AddMinutes(10))),
            470, 300, Path.Combine(output, "microsoft-signin.png"));
        Render(new ComposeWindow(new MailAccount { Email = "hello@example.invalid", DisplayName = "Example" }, ""), 820, 690, Path.Combine(output, "compose.png"));
        Render(new RulesWindow(new MailAccount { Email = "hello@example.invalid" },
            [new MailFolder { ServerName = "INBOX" }, new MailFolder { ServerName = "Archive" },
             new MailFolder { ServerName = "Projects" }], 42, 1,
            new MailItem { FromAddress = "camille@example.invalid", From = "Camille <camille@example.invalid>" }),
            750, 540, Path.Combine(output, "rules.png"));
        Render(new MainWindow(), 980, 600, Path.Combine(output, "main-compact.png"));
        var syncPreview = new MainWindow();
        ((Button)syncPreview.FindName("SyncTabButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(syncPreview, 1320, 780, Path.Combine(output, "ribbon-sync.png"));
        var viewPreview = new MainWindow();
        ((Button)viewPreview.FindName("ViewTabButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(viewPreview, 1320, 780, Path.Combine(output, "ribbon-view.png"));
        var filePreview = new MainWindow();
        ((Button)filePreview.FindName("FileTabButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(filePreview, 1320, 780, Path.Combine(output, "file.png"));
        Render(new SettingsWindow(new ClientSettings(), sampleAccounts), 600, 700, Path.Combine(output, "settings.png"));
        Render(new SettingsWindow(new ClientSettings(), sampleAccounts), 600, 850, Path.Combine(output, "settings-full.png"));
        Render(new AboutWindow(), 500, 340, Path.Combine(output, "about.png"));
        var importPreview = new ThunderbirdImportWindow();
        var previewCandidates = new[]
        {
            new ThunderbirdCandidate { Account = new MailAccount { Email = "hello@example.invalid", ImapHost = "imap.example.com", SmtpHost = "smtp.example.com" }, Profile = "Demo" },
            new ThunderbirdCandidate { Account = new MailAccount { Email = "team@example.invalid", ImapHost = "imap.example.org", SmtpHost = "smtp.example.org" }, Profile = "Demo" }
        };
        typeof(ThunderbirdImportWindow).GetMethod("ShowCandidates", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(importPreview, [previewCandidates]);
        Render(importPreview, 660, 540, Path.Combine(output, "thunderbird-import.png"));
        Localizer.SetLanguage("en");
        Render(new MainWindow(), 1320, 780, Path.Combine(output, "main-en.png"));
        Render(new AccountWindow(), 520, 760, Path.Combine(output, "account-en.png"));
        Localizer.SetLanguage("fr");
        var liveSwitchPreview = new MainWindow();
        ThemeManager.SetMode("light");
        var searchLight = new MainWindow();
        ((TextBox)searchLight.FindName("SearchBox")).Text = "Search for a message";
        Render(searchLight, 1320, 780, Path.Combine(output, "search-light.png"));
        Render(liveSwitchPreview, 1320, 780, Path.Combine(output, "main-light.png"));
        Render(new SettingsWindow(new ClientSettings { ThemeMode = "light" }, sampleAccounts), 600, 700, Path.Combine(output, "settings-light.png"));
        ThemeManager.Shutdown();
    }

    private static void Render(Window window, int width, int height, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        window.Content = null;
        var surface = new Border { Background = window.Background, Child = root, Width = width, Height = height };
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        window.Close();
    }
}
