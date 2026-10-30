using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace Mailcario;

internal static class ComposeDocument
{
    public static string ToHtml(FlowDocument document)
    {
        var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"></head><body>");
        foreach (Block block in document.Blocks) AppendBlock(html, block);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void AppendBlock(StringBuilder html, Block block)
    {
        if (block is Paragraph paragraph)
        {
            string alignment = paragraph.TextAlignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "justify",
                _ => "left"
            };
            html.Append("<p style=\"text-align:").Append(alignment).Append(";margin:0 0 0.6em;");
            if (paragraph.Margin.Left > 0)
                html.Append("margin-left:").Append(paragraph.Margin.Left.ToString("0.#",
                    System.Globalization.CultureInfo.InvariantCulture)).Append("px;");
            html.Append("\">");
            if (paragraph.Inlines.Count == 0) html.Append("<br>");
            foreach (Inline inline in paragraph.Inlines) AppendInline(html, inline);
            html.Append("</p>");
        }
        else if (block is List list)
        {
            string tag = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or
                TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? "ol" : "ul";
            html.Append('<').Append(tag).Append('>');
            foreach (ListItem item in list.ListItems)
            {
                html.Append("<li>");
                foreach (Block child in item.Blocks) AppendBlock(html, child);
                html.Append("</li>");
            }
            html.Append("</").Append(tag).Append('>');
        }
        else if (block is Section section)
        {
            foreach (Block child in section.Blocks) AppendBlock(html, child);
        }
        else if (block is Table table)
        {
            html.Append("<table style=\"border-collapse:collapse\">");
            foreach (TableRowGroup group in table.RowGroups)
                foreach (TableRow row in group.Rows)
                {
                    html.Append("<tr>");
                    foreach (TableCell cell in row.Cells)
                    {
                        html.Append("<td>");
                        foreach (Block child in cell.Blocks) AppendBlock(html, child);
                        html.Append("</td>");
                    }
                    html.Append("</tr>");
                }
            html.Append("</table>");
        }
    }

    private static void AppendInline(StringBuilder html, Inline inline)
    {
        if (inline is LineBreak) { html.Append("<br>"); return; }
        if (inline is Run run)
        {
            var style = new StringBuilder();
            if (run.FontWeight >= FontWeights.Bold) style.Append("font-weight:bold;");
            if (run.FontStyle == FontStyles.Italic) style.Append("font-style:italic;");
            if (run.TextDecorations.Count > 0) style.Append("text-decoration:underline;");
            if (run.FontSize > 0) style.Append("font-size:").Append(run.FontSize.ToString("0.#",
                System.Globalization.CultureInfo.InvariantCulture)).Append("px;");
            string family = run.FontFamily.Source;
            if (Regex.IsMatch(family, @"^[\p{L}\p{N} ,\-]{1,80}$"))
                style.Append("font-family:'").Append(family).Append("';");
            if (run.Foreground is SolidColorBrush foreground)
            {
                var themeInk = Application.Current?.TryFindResource("Ink") as SolidColorBrush;
                if (foreground.Color.A > 0 && (themeInk is null || foreground.Color != themeInk.Color))
                    style.Append("color:").Append(ToCssColor(foreground.Color)).Append(';');
            }
            if (run.Background is SolidColorBrush background && background.Color.A > 0)
                style.Append("background-color:").Append(ToCssColor(background.Color)).Append(';');
            html.Append("<span style=\"").Append(style).Append("\">")
                .Append(WebUtility.HtmlEncode(run.Text).Replace("\r\n", "<br>").Replace("\n", "<br>"))
                .Append("</span>");
        }
        else if (inline is Hyperlink link && link.NavigateUri is { } target &&
            target.Scheme is "https" or "http" or "mailto")
        {
            html.Append("<a href=\"").Append(WebUtility.HtmlEncode(target.AbsoluteUri)).Append("\">");
            foreach (Inline child in link.Inlines) AppendInline(html, child);
            html.Append("</a>");
        }
        else if (inline is Span span)
            foreach (Inline child in span.Inlines) AppendInline(html, child);
    }

    private static string ToCssColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
