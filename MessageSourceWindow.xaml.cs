using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Mailcario;

internal static class MessageSourceFormatter
{
    internal const int DisplayLimitBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] Headers(byte[] raw)
    {
        for (int i = 0; i < raw.Length - 1; i++)
        {
            if (raw[i] == '\r' && i + 3 < raw.Length && raw[i + 1] == '\n' &&
                raw[i + 2] == '\r' && raw[i + 3] == '\n') return raw[..i];
            if (raw[i] == '\n' && raw[i + 1] == '\n') return raw[..i];
        }
        return raw;
    }

    internal static string Display(byte[] bytes, out bool truncated, bool showAll = false)
    {
        truncated = !showAll && bytes.Length > DisplayLimitBytes;
        ReadOnlySpan<byte> visible = bytes.AsSpan(0, truncated ? DisplayLimitBytes : bytes.Length);
        try { return StrictUtf8.GetString(visible); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(visible); }
    }
}

public partial class MessageSourceWindow : Window
{
    private readonly string _subject;
    private readonly Func<CancellationToken, Task<byte[]>> _loader;
    private readonly CancellationTokenSource _cancellation = new();
    private byte[]? _raw;
    private byte[]? _headers;
    private bool _showHeaders = true;
    private bool _showAll;
    private bool _truncated;

    public MessageSourceWindow(string subject, Func<CancellationToken, Task<byte[]>> loader)
    {
        _subject = subject;
        _loader = loader;
        InitializeComponent();
        WindowTheme.Attach(this);
        Heading.Text = Localizer.T("messageSource");
        Title = "Mailcario · " + Heading.Text;
        SubjectLabel.Text = subject;
        HeadersButton.Content = Localizer.T("sourceHeaders");
        FullSourceButton.Content = Localizer.T("sourceFull");
        ShowAllButton.Content = Localizer.T("sourceShowAll");
        SearchLabel.Text = Localizer.T("sourceSearchLabel");
        SearchBox.ToolTip = Localizer.T("sourceSearch");
        FindButton.Content = Localizer.T("sourceFind");
        CopyButton.Content = Localizer.T("copy");
        SaveButton.Content = Localizer.T("sourceSave");
        CloseButton.Content = Localizer.T("close");
        StatusLabel.Text = Localizer.T("sourceLoading");
        Loaded += LoadSourceAsync;
        Closed += (_, _) => _cancellation.Cancel();
    }

    private async void LoadSourceAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadSourceAsync;
        try
        {
            _raw = await _loader(_cancellation.Token);
            if (_cancellation.IsCancellationRequested) return;
            _headers = MessageSourceFormatter.Headers(_raw);
            CopyButton.IsEnabled = SaveButton.IsEnabled = true;
            ShowCurrentView();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_cancellation.IsCancellationRequested)
                StatusLabel.Text = string.Format(Localizer.T("sourceLoadFailed"), ex.Message);
        }
    }

    private void ShowCurrentView()
    {
        if (_raw is null || _headers is null) return;
        byte[] selected = _showHeaders ? _headers : _raw;
        SourceBox.Text = MessageSourceFormatter.Display(selected, out _truncated, _showAll);
        HeadersButton.Style = (Style)FindResource(_showHeaders ? "AccentButton" : "ChromeButton");
        FullSourceButton.Style = (Style)FindResource(_showHeaders ? "ChromeButton" : "AccentButton");
        ShowAllButton.Visibility = _truncated ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = string.Format(Localizer.T("sourceSize"), _raw.Length.ToString("N0")) +
            (_truncated ? " · " + Localizer.T("sourceTruncated") : "");
        SourceBox.ScrollToHome();
    }

    private void Headers_Click(object sender, RoutedEventArgs e) { _showHeaders = true; ShowCurrentView(); }
    private void FullSource_Click(object sender, RoutedEventArgs e) { _showHeaders = false; ShowCurrentView(); }
    private void ShowAll_Click(object sender, RoutedEventArgs e) { _showAll = true; ShowCurrentView(); }

    private void Find_Click(object sender, RoutedEventArgs e) => FindNext();
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }

    private void FindNext()
    {
        string query = SearchBox.Text;
        if (query.Length == 0) { SearchBox.Focus(); return; }
        int start = SourceBox.SelectionStart + SourceBox.SelectionLength;
        int index = SourceBox.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = SourceBox.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) { StatusLabel.Text = Localizer.T("sourceNoMatch"); return; }
        SourceBox.Focus();
        SourceBox.Select(index, query.Length);
        SourceBox.ScrollToLine(SourceBox.GetLineIndexFromCharacterIndex(index));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(SourceBox.Text); }
        catch (Exception ex) { StatusLabel.Text = string.Format(Localizer.T("sourceCopyFailed"), ex.Message); }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_raw is null) return;
        string safeName = new(_subject.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(75).ToArray());
        var dialog = new SaveFileDialog
        {
            Title = Localizer.T("sourceSave"),
            Filter = Localizer.T("sourceEmlFilter") + " (*.eml)|*.eml",
            DefaultExt = ".eml",
            FileName = (string.IsNullOrWhiteSpace(safeName) ? "message" : safeName.Trim()) + ".eml"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SaveButton.IsEnabled = false;
            await File.WriteAllBytesAsync(dialog.FileName, _raw);
            StatusLabel.Text = Localizer.T("sourceSaved");
        }
        catch (Exception ex) { StatusLabel.Text = string.Format(Localizer.T("sourceSaveFailed"), ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
