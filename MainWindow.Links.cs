using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace Mailcario;

public partial class MainWindow
{
    private IReadOnlySet<string> _currentLinkTargets = new HashSet<string>(StringComparer.Ordinal);

    private void ShowLinkTarget(string? value, bool requireCurrentMailLink)
    {
        if (value is null || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 ||
            IsInternalMailUrl(uri.AbsoluteUri) ||
            (requireCurrentMailLink && !_currentLinkTargets.Contains(uri.AbsoluteUri)))
        {
            ClearLinkTarget();
            return;
        }
        if (LinkPreviewPopup.IsOpen && LinkPreviewText.Text == uri.AbsoluteUri) return;
        LinkPreviewCaption.Text = Localizer.T("linkTarget");
        LinkPreviewText.Text = uri.AbsoluteUri;
        LinkPreviewPopup.IsOpen = true;
    }

    private void ClearLinkTarget()
    {
        LinkPreviewPopup.IsOpen = false;
        LinkPreviewText.Text = "";
    }

    private void HtmlBodyBox_MouseMove(object sender, MouseEventArgs e)
    {
        TextPointer? position = HtmlBodyBox.GetPositionFromPoint(e.GetPosition(HtmlBodyBox), true);
        DependencyObject? current = position?.Parent;
        while (current is not null)
        {
            if (current is Hyperlink link)
            {
                ShowLinkTarget(link.NavigateUri?.AbsoluteUri, false);
                return;
            }
            current = LogicalTreeHelper.GetParent(current);
        }
        ClearLinkTarget();
    }

    private void HtmlBodyBox_MouseLeave(object sender, MouseEventArgs e) => ClearLinkTarget();
}
