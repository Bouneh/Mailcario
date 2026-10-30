using System.Windows;
using System.Windows.Controls;

namespace Mailcario;

public partial class MainWindow
{
    private void ReceivedAttachment_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MailAttachment attachment } || !attachment.CanPreview) return;
        new AttachmentPreviewWindow(attachment) { Owner = this }.Show();
    }

    private async void ReceivedAttachment_Save_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MailAttachment attachment }) return;
        try
        {
            if (await AttachmentFileActions.SaveAsync(this, attachment) is not null)
                StatusLabel.Text = Localizer.T("attachmentSaved") + " · " + attachment.FileName;
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
    }

    private void ReceivedAttachment_Print_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MailAttachment attachment } || !attachment.CanPrint) return;
        new AttachmentPreviewWindow(attachment, printOnOpen: true) { Owner = this }.Show();
    }
}
