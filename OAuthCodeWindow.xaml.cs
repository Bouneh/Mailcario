using System.Diagnostics;
using System.Windows;

namespace Mailcario;

public partial class OAuthCodeWindow : Window
{
    private readonly Uri _verificationUri;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _completed;
    public CancellationToken CancellationToken => _cancellation.Token;

    public OAuthCodeWindow(MicrosoftDeviceCode code)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        _verificationUri = code.VerificationUri;
        Heading.Text = Localizer.T("microsoftSignIn");
        Instructions.Text = string.Format(Localizer.T("microsoftCodeInstructions"), code.VerificationUri.Host);
        CodeLabel.Text = Localizer.T("microsoftCodeLabel");
        CodeValue.Text = code.UserCode;
        WaitingLabel.Text = Localizer.T("microsoftWaiting");
        CancelButton.Content = Localizer.T("cancel");
        OpenBrowserButton.Content = Localizer.T("openMicrosoftLogin");
        Closed += (_, _) => { if (!_completed) _cancellation.Cancel(); };
    }

    public void Complete()
    {
        _completed = true;
        Close();
        _cancellation.Dispose();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_verificationUri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
