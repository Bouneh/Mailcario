using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Mailcario;

public partial class MailNotificationWindow : Window
{
    private readonly Action _open;
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(8) };

    public MailNotificationWindow(string account, string detail, Action open)
    {
        InitializeComponent();
        _open = open;
        Heading.Text = Localizer.T("newMailNotification");
        Account.Text = account;
        Detail.Text = detail;
        Hint.Text = Localizer.T("openMailcario");
        Left = SystemParameters.WorkArea.Right - Width - 20;
        Top = SystemParameters.WorkArea.Bottom - Height - 20;
        _dismiss.Tick += (_, _) => Close();
        Closed += (_, _) => _dismiss.Stop();
        _dismiss.Start();
    }

    private void Notification_Click(object sender, MouseButtonEventArgs e)
    {
        _open();
        if (IsVisible) Close();
    }
}
