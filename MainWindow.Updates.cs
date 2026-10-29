using System.Windows;

namespace Mailcario;

public partial class MainWindow
{
    private bool _updateChecked;

    private async void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_updateChecked) return;
        _updateChecked = true;
        try
        {
            AvailableUpdate? update = await UpdateService.CheckAsync();
            if (update is not null && IsVisible)
                new UpdateWindow(update) { Owner = this }.ShowDialog();
        }
        catch
        {
            // A failed network check must never interrupt the mail client.
        }
    }
}
