using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Mailcario;

public static class AttachmentFileActions
{
    public static async Task<string?> SaveAsync(Window owner, MailAttachment attachment)
    {
        var dialog = new SaveFileDialog
        {
            FileName = MimeReader.SafeAttachmentName(attachment.FileName, 1, attachment.ContentType),
            Filter = Localizer.T("allFiles") + "|*.*",
            OverwritePrompt = true,
            AddExtension = false
        };
        if (dialog.ShowDialog(owner) != true) return null;
        await File.WriteAllBytesAsync(dialog.FileName, attachment.Bytes);
        return dialog.FileName;
    }
}
