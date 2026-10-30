using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Mailcario;

public partial class AttachmentPreviewWindow : Window
{
    private readonly MailAttachment _attachment;
    private readonly AttachmentPreviewKind _kind;
    private readonly string _pdfUrl = "https://mailcario.invalid/attachment-preview/" + Guid.NewGuid().ToString("N") + ".pdf";
    private bool _pdfReady;

    private readonly bool _printOnOpen;
    private bool _printedOnOpen;

    public AttachmentPreviewWindow(MailAttachment attachment, bool printOnOpen = false)
    {
        _attachment = attachment;
        _kind = AttachmentPreviewSupport.Kind(attachment);
        _printOnOpen = printOnOpen;
        InitializeComponent();
        WindowTheme.Attach(this);
        Title = "Mailcario · " + attachment.FileName;
        FileNameLabel.Text = attachment.FileName;
        DetailsLabel.Text = attachment.Details;
        SaveButton.Content = Localizer.T("saveAttachment");
        PrintButton.Content = Localizer.T("printAttachment");
        CloseButton.Content = Localizer.T("close");
        PrintButton.IsEnabled = _kind is AttachmentPreviewKind.Text or AttachmentPreviewKind.Image;
        EmptyPreviewText.Text = Localizer.T(_kind == AttachmentPreviewKind.Unsupported
            ? "attachmentPreviewUnavailable" : "attachmentPreviewLoading");
        Loaded += async (_, _) => await LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            switch (_kind)
            {
                case AttachmentPreviewKind.Text:
                    TextPreview.Text = AttachmentPreviewSupport.ReadText(_attachment);
                    TextPreview.Visibility = Visibility.Visible;
                    EmptyPreview.Visibility = Visibility.Collapsed;
                    if (_printOnOpen) Print_Click(this, new RoutedEventArgs());
                    break;
                case AttachmentPreviewKind.Image:
                    using (var stream = new MemoryStream(_attachment.Bytes, false))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.DecodePixelWidth = 1800;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        ImagePreview.Source = image;
                    }
                    ImageScroller.Visibility = Visibility.Visible;
                    EmptyPreview.Visibility = Visibility.Collapsed;
                    if (_printOnOpen) Print_Click(this, new RoutedEventArgs());
                    break;
                case AttachmentPreviewKind.Pdf:
                    await LoadPdfAsync();
                    break;
            }
        }
        catch (Exception)
        {
            PrintButton.IsEnabled = false;
            EmptyPreviewText.Text = Localizer.T("attachmentPreviewUnavailable");
            TextPreview.Visibility = ImageScroller.Visibility = PdfPreview.Visibility = Visibility.Collapsed;
            EmptyPreview.Visibility = Visibility.Visible;
        }
    }

    private async Task LoadPdfAsync()
    {
        string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailcario", "WebView2AttachmentPreview");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        await PdfPreview.EnsureCoreWebView2Async(environment);
        CoreWebView2 core = PdfPreview.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) =>
        {
            bool allowed = args.Request.Uri.Equals(_pdfUrl, StringComparison.OrdinalIgnoreCase);
            if (!allowed)
            {
                args.Response = core.Environment.CreateWebResourceResponse(new MemoryStream([]),
                    403, "Blocked", "Content-Type: text/plain\r\nCache-Control: no-store");
                return;
            }
            string? rangeHeader = args.Request.Headers.Contains("Range")
                ? args.Request.Headers.GetHeader("Range") : null;
            var (start, length, status) = PdfRange(rangeHeader, _attachment.Bytes.Length);
            string headers = "Content-Type: application/pdf\r\nContent-Disposition: inline\r\n" +
                "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nAccept-Ranges: bytes\r\n";
            if (status == 416)
                headers += $"Content-Range: bytes */{_attachment.Bytes.Length}\r\nContent-Length: 0";
            else
            {
                headers += $"Content-Length: {length}";
                if (status == 206)
                    headers += $"\r\nContent-Range: bytes {start}-{start + length - 1}/{_attachment.Bytes.Length}";
            }
            args.Response = core.Environment.CreateWebResourceResponse(
                status == 416 ? new MemoryStream([]) :
                    new MemoryStream(_attachment.Bytes, start, length, false),
                status, status == 206 ? "Partial Content" : status == 416 ? "Range Not Satisfiable" : "OK",
                headers);
        };
        core.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.Equals(_pdfUrl, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.NavigationCompleted += (_, args) =>
        {
            _pdfReady = args.IsSuccess;
            PrintButton.IsEnabled = _pdfReady;
            if (_pdfReady) return;
            PdfPreview.Visibility = Visibility.Collapsed;
            EmptyPreview.Visibility = Visibility.Visible;
            EmptyPreviewText.Text = Localizer.T("attachmentPreviewUnavailable");
        };
        if (_printOnOpen)
            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess && !_printedOnOpen)
                {
                    _printedOnOpen = true;
                    Print_Click(this, new RoutedEventArgs());
                }
            };
        PdfPreview.Visibility = Visibility.Visible;
        EmptyPreview.Visibility = Visibility.Collapsed;
        core.Navigate(_pdfUrl);
    }

    internal static (int Start, int Length, int Status) PdfRange(string? header, int total)
    {
        if (string.IsNullOrWhiteSpace(header)) return (0, total, 200);
        Match match = Regex.Match(header, @"^bytes=(?<start>\d+)-(?<end>\d*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && long.TryParse(match.Groups["start"].Value, out long start) &&
            start >= 0 && start < total)
        {
            long end = total - 1;
            if (match.Groups["end"].Length > 0 &&
                (!long.TryParse(match.Groups["end"].Value, out end) || end < start)) return (0, 0, 416);
            end = Math.Min(end, total - 1);
            return ((int)start, (int)(end - start + 1), 206);
        }
        match = Regex.Match(header, @"^bytes=-(?<length>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && long.TryParse(match.Groups["length"].Value, out long suffix) && suffix > 0)
        {
            int length = (int)Math.Min(suffix, total);
            return (total - length, length, 206);
        }
        return (0, 0, 416);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;
            if (await AttachmentFileActions.SaveAsync(this, _attachment) is not null)
                DetailsLabel.Text = _attachment.Details + " · " + Localizer.T("attachmentSaved");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_kind == AttachmentPreviewKind.Pdf)
            {
                if (_pdfReady) PdfPreview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System);
                return;
            }
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            if (_kind == AttachmentPreviewKind.Text)
            {
                var document = new FlowDocument { PageWidth = dialog.PrintableAreaWidth,
                    ColumnWidth = dialog.PrintableAreaWidth, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
                document.Blocks.Add(new Paragraph(new Run(
                    AttachmentPreviewSupport.ReadText(_attachment, truncateForPreview: false))));
                dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, _attachment.FileName);
            }
            else if (_kind == AttachmentPreviewKind.Image && ImagePreview.Source is BitmapSource image)
            {
                var visual = new DrawingVisual();
                double scale = Math.Min(dialog.PrintableAreaWidth / image.PixelWidth,
                    dialog.PrintableAreaHeight / image.PixelHeight);
                double width = image.PixelWidth * scale;
                double height = image.PixelHeight * scale;
                using (DrawingContext drawing = visual.RenderOpen())
                    drawing.DrawImage(image, new Rect((dialog.PrintableAreaWidth - width) / 2,
                        (dialog.PrintableAreaHeight - height) / 2, width, height));
                dialog.PrintVisual(visual, _attachment.FileName);
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message,
        Localizer.T("failed"), MessageBoxButton.OK, MessageBoxImage.Error);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
