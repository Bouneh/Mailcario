using System.Diagnostics;
using System.Windows;

namespace Mailcario;

public partial class MainWindow
{
    private async void UnsubscribeButton_Click(object sender, RoutedEventArgs e)
    {
        UnsubscribeAction? action = _unsubscribeAction;
        if (action is null) return;
        string prompt = string.Format(Localizer.T("unsubscribeConfirm"), action.Url.AbsoluteUri);
        if (MessageBox.Show(this, prompt, Localizer.T("unsubscribe"), MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (action.OneClick)
            {
                UnsubscribeButton.IsEnabled = false;
                await UnsubscribeSupport.PostOneClickAsync(action.Url);
                StatusLabel.Text = Localizer.T("unsubscribeSent");
            }
            else
            {
                Process.Start(new ProcessStartInfo(action.Url.AbsoluteUri) { UseShellExecute = true });
                StatusLabel.Text = Localizer.T("unsubscribeOpened");
            }
        }
        catch (Exception ex) { StatusLabel.Text = Localizer.T("failed") + ": " + ex.Message; }
        finally { UnsubscribeButton.IsEnabled = true; }
    }
}
