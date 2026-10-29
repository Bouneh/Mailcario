using System.Net;
using HtmlAgilityPack;

namespace Mailcario;

public static class HtmlTemplateRenderer
{
    private const string TransparentPixel = "data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=";
    private static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "iframe", "frame", "frameset", "object", "embed", "applet", "form", "input",
        "button", "textarea", "select", "option", "template", "svg", "math", "canvas", "video",
        "audio", "source", "track", "link", "meta", "base"
    };

    public static string Build(string html, IReadOnlyDictionary<string, string> imageData,
        IReadOnlyList<Uri> permittedImages)
    {
        if (html.Length > 2_000_000) html = html[..2_000_000];
        var permitted = permittedImages.Select(uri => uri.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        var document = new HtmlDocument { OptionFixNestedTags = true };
        document.LoadHtml(html);
        foreach (HtmlNode node in document.DocumentNode.Descendants().ToList())
        {
            if (node.ParentNode is null) continue;
            if (RemovedElements.Contains(node.Name))
            {
                node.Remove();
                continue;
            }
            foreach (HtmlAttribute attribute in node.Attributes.ToList())
            {
                string name = attribute.Name.ToLowerInvariant();
                if (name.StartsWith("on", StringComparison.Ordinal) || name is "srcset" or "formaction" or "action" or "background" or "poster" or "xlink:href")
                {
                    node.Attributes.Remove(attribute);
                    continue;
                }
                if (name == "src" && node.Name != "img") node.Attributes.Remove(attribute);
                if (name == "href" && (node.Name != "a" || !SafeHttpUri(attribute.Value, out _)))
                    node.Attributes.Remove(attribute);
            }
            if (node.Name != "img") continue;
            string? source = node.GetAttributeValue("src", null);
            if (source is not null && SafeHttpUri(source, out Uri? uri) && uri is not null &&
                permitted.Contains(uri.AbsoluteUri) && imageData.TryGetValue(uri.AbsoluteUri, out string? dataUri))
                node.SetAttributeValue("src", dataUri);
            else node.SetAttributeValue("src", TransparentPixel);
            node.Attributes.Remove("srcset");
        }
        // A response-level CSP and a WebView2 request blocker add independent network barriers.
        return document.DocumentNode.OuterHtml;
    }

    public static IReadOnlySet<string> SafeLinkTargets(string sanitizedHtml)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var document = new HtmlDocument { OptionFixNestedTags = true };
        document.LoadHtml(sanitizedHtml.Length > 2_000_000 ? sanitizedHtml[..2_000_000] : sanitizedHtml);
        foreach (var link in document.DocumentNode.Descendants("a"))
            if (SafeHttpUri(link.GetAttributeValue("href", null), out Uri? uri) && uri is not null)
                targets.Add(uri.AbsoluteUri);
        return targets;
    }

    private static bool SafeHttpUri(string? value, out Uri? uri)
    {
        uri = null;
        if (value is null) return false;
        string decoded = WebUtility.HtmlDecode(value);
        if (decoded.Length > 2048 || !Uri.TryCreate(decoded, UriKind.Absolute, out uri)) return false;
        return (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            uri.UserInfo.Length == 0 && !uri.Host.Equals("mailcario.invalid", StringComparison.OrdinalIgnoreCase);
    }
}
