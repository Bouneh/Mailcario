using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mailcario;

public sealed record SafeMailDocument(FlowDocument Document, int BlockedImages, int TrackingPixels,
    IReadOnlyList<Uri> DownloadableImages);

/// <summary>
/// Converts a small, allowlisted subset of email HTML to inert WPF text elements.
/// The input is never loaded by a browser. Only previously downloaded, verified bitmaps
/// may be inserted; HTML image URLs are never assigned to a WPF Image source.
/// </summary>
public static class SafeHtmlRenderer
{
    private static readonly Regex Tokens = new(@"<!--.*?-->|<![^>]*>|<[^>]*>|[^<]+", RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex Tag = new(@"^<\s*(?<close>/)?\s*(?<name>[a-z][a-z0-9]*)\b(?<attrs>[^>]*)>$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Href = new(@"(?:^|\s)href\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TrackerUrl = new(@"(?:mailtrack|mailtracker|trackingpixel|tracker|beacon|webbug|/pixel(?:[./?_-]|$)|/track(?:[./?_-]|$)|/open(?:[./?_-]|$)|(?:^|[/._-])1x1(?:[./?_-]|$))", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex HiddenStyle = new(@"(?:display\s*:\s*none|visibility\s*:\s*hidden|opacity\s*:\s*0(?:[;\s]|$)|mso-hide\s*:\s*all)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> Suppressed = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "script", "style", "iframe", "object", "embed", "form", "input", "button", "textarea",
        "select", "option", "svg", "math", "template", "video", "audio", "canvas", "noscript"
    };
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "h1", "h2", "h3", "h4", "li", "blockquote", "pre", "tr"
    };

    public static SafeMailDocument Render(string html, IReadOnlyDictionary<string, BitmapSource>? loadedImages = null)
    {
        if (html.Length > 2_000_000) html = html[..2_000_000];
        var trackedUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match candidate in Tokens.Matches(html))
        {
            if (!candidate.Value.StartsWith('<')) continue;
            var imageTag = Tag.Match(candidate.Value);
            if (!imageTag.Success || imageTag.Groups["close"].Success ||
                !imageTag.Groups["name"].Value.Equals("img", StringComparison.OrdinalIgnoreCase)) continue;
            string attributes = imageTag.Groups["attrs"].Value;
            Uri? uri = ImageUri(Attribute(attributes, "src"));
            if (uri is not null && IsTrackingPixel(attributes, uri)) trackedUrls.Add(uri.AbsoluteUri);
        }
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = ThemeBrush("Ink", Color.FromRgb(240, 240, 240)),
            TextAlignment = TextAlignment.Left
        };
        Paragraph paragraph = NewParagraph(document);
        var inline = new List<(string Name, Uri? Link)>();
        string? suppressedTag = null;
        int suppressedDepth = 0;
        int blockedImages = 0;
        int trackingPixels = 0;
        var downloadableImages = new List<Uri>();
        var seenImages = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match token in Tokens.Matches(html))
        {
            string value = token.Value;
            if (value.StartsWith("<!--") || value.StartsWith("<!")) continue;
            if (!value.StartsWith('<'))
            {
                if (suppressedDepth == 0) AppendText(paragraph, value, inline);
                continue;
            }
            var tag = Tag.Match(value);
            if (!tag.Success) continue;
            string name = tag.Groups["name"].Value.ToLowerInvariant();
            bool closing = tag.Groups["close"].Success;
            if (suppressedDepth > 0)
            {
                if (name == suppressedTag)
                {
                    if (closing) suppressedDepth--;
                    else suppressedDepth++;
                    if (suppressedDepth == 0) suppressedTag = null;
                }
                continue;
            }
            if (!closing && name == "img")
            {
                string attributes = tag.Groups["attrs"].Value;
                string? source = Attribute(attributes, "src");
                Uri? uri = ImageUri(source);
                if (IsTrackingPixel(attributes, uri) || (uri is not null && trackedUrls.Contains(uri.AbsoluteUri)))
                {
                    trackingPixels++;
                    continue;
                }
                if (uri is not null)
                {
                    if (seenImages.Add(uri.AbsoluteUri)) downloadableImages.Add(uri);
                    if (loadedImages?.TryGetValue(uri.AbsoluteUri, out var bitmap) == true)
                    {
                        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform,
                            MaxWidth = 620, MaxHeight = 1000, HorizontalAlignment = HorizontalAlignment.Left };
                        paragraph.Inlines.Add(new InlineUIContainer(image));
                        continue;
                    }
                }
                blockedImages++;
                paragraph.Inlines.Add(new Run("[Image masquée]")
                    { Foreground = ThemeBrush("Muted", Color.FromRgb(170, 170, 170)) });
                continue;
            }
            if (!closing && Suppressed.Contains(name))
            {
                if (name is not ("input" or "embed") && !value.EndsWith("/>", StringComparison.Ordinal))
                {
                    suppressedTag = name;
                    suppressedDepth = 1;
                }
                continue;
            }
            if (Suppressed.Contains(name)) continue;
            if (name == "br" && !closing)
            {
                paragraph.Inlines.Add(new LineBreak());
                continue;
            }
            if (name == "hr" && !closing)
            {
                paragraph = NewParagraph(document);
                paragraph.Inlines.Add(new Run("──────────") { Foreground = ThemeBrush("Muted", Colors.LightGray) });
                paragraph = NewParagraph(document);
                continue;
            }
            if (name == "td" && !closing && paragraph.Inlines.Count > 0) paragraph.Inlines.Add(new Run("     "));
            if (BlockTags.Contains(name))
            {
                paragraph = NewParagraph(document);
                if (!closing)
                {
                    if (name == "li")
                    {
                        paragraph.Margin = new Thickness(17, 0, 0, 6);
                        paragraph.Inlines.Add(new Run("•  "));
                    }
                    else if (name == "blockquote") paragraph.Margin = new Thickness(18, 6, 0, 10);
                    else if (name.StartsWith('h'))
                    {
                        paragraph.FontWeight = FontWeights.SemiBold;
                        paragraph.FontSize = name == "h1" ? 21 : name == "h2" ? 18 : 16;
                    }
                }
                continue;
            }
            if (name is "b" or "strong" or "i" or "em" or "u" or "code" or "a")
            {
                if (closing)
                {
                    int index = inline.FindLastIndex(x => x.Name == name);
                    if (index >= 0) inline.RemoveAt(index);
                }
                else inline.Add((name, name == "a" ? SafeLink(tag.Groups["attrs"].Value) : null));
            }
        }
        return new SafeMailDocument(document, blockedImages, trackingPixels, downloadableImages);
    }

    private static Paragraph NewParagraph(FlowDocument document)
    {
        var current = document.Blocks.LastBlock as Paragraph;
        if (current is not null && current.Inlines.Count == 0) return current;
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10), LineHeight = 23 };
        document.Blocks.Add(paragraph);
        return paragraph;
    }

    private static void AppendText(Paragraph paragraph, string raw, List<(string Name, Uri? Link)> inline)
    {
        string value = WebUtility.HtmlDecode(raw);
        value = Regex.Replace(value, @"\s+", " ");
        if (paragraph.Inlines.Count == 0) value = value.TrimStart();
        if (value.Length == 0) return;
        var run = new Run(value);
        if (inline.Any(x => x.Name is "b" or "strong")) run.FontWeight = FontWeights.SemiBold;
        if (inline.Any(x => x.Name is "i" or "em")) run.FontStyle = FontStyles.Italic;
        if (inline.Any(x => x.Name == "u")) run.TextDecorations = TextDecorations.Underline;
        if (inline.Any(x => x.Name == "code")) run.FontFamily = new FontFamily("Consolas");
        Uri? link = inline.LastOrDefault(x => x.Name == "a").Link;
        if (link is null) paragraph.Inlines.Add(run);
        else
        {
            var hyperlink = new Hyperlink(run)
            {
                NavigateUri = link,
                Foreground = ThemeBrush("AccentDark", Color.FromRgb(133, 198, 245)),
                TextDecorations = TextDecorations.Underline,
                ToolTip = link.AbsoluteUri
            };
            hyperlink.RequestNavigate += (_, e) =>
            {
                e.Handled = true;
                if (e.Uri is null || !SafeUri(e.Uri)) return;
                try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { /* No default browser is configured. */ }
            };
            paragraph.Inlines.Add(hyperlink);
        }
    }

    private static Uri? SafeLink(string attributes)
    {
        string? value = Attribute(attributes, "href");
        if (value is null) return null;
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        return SafeUri(uri) ? uri : null;
    }

    private static Uri? ImageUri(string? source) =>
        source is { Length: <= 2048 } && Uri.TryCreate(source, UriKind.Absolute, out var uri) && SafeUri(uri)
            ? uri : null;

    private static string? Attribute(string attributes, string name)
    {
        Regex expression = name == "href" ? Href : new Regex(@"(?:^|\s)" + Regex.Escape(name) +
            @"\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        var match = expression.Match(attributes);
        if (!match.Success) return null;
        return WebUtility.HtmlDecode(match.Groups["double"].Success ? match.Groups["double"].Value :
            match.Groups["single"].Success ? match.Groups["single"].Value : match.Groups["bare"].Value);
    }

    private static bool IsTrackingPixel(string attributes, Uri? uri)
    {
        if (uri is not null && TrackerUrl.IsMatch(uri.Host + uri.PathAndQuery)) return true;
        if (Regex.IsMatch(attributes, @"(?:^|\s)hidden(?:\s|=|$)", RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100))) return true;
        string style = Attribute(attributes, "style") ?? "";
        if (HiddenStyle.IsMatch(style)) return true;
        return IsTiny(Attribute(attributes, "width")) || IsTiny(Attribute(attributes, "height")) ||
            Regex.IsMatch(style, @"(?:^|;)\s*(?:width|height|max-width|max-height)\s*:\s*[012](?:px)?(?:\s|;|$)",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }

    private static bool IsTiny(string? value) =>
        int.TryParse(value?.TrimEnd('p', 'x', ' '), out int pixels) && pixels <= 2;

    private static bool SafeUri(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.UserInfo.Length == 0;

    private static Brush ThemeBrush(string key, Color fallback) =>
        Application.Current?.Resources[key] as Brush ?? new SolidColorBrush(fallback);
}
