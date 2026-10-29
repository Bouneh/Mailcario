using System.Net.Mail;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Mailcario;

public partial class AccountWindow : Window
{
    private string _lastAutomaticUsername = "";
    private string _lastAutomaticSmtpUsername = "";
    private string _provider = "laposte";
    private bool _initializing = true;
    private readonly string? _originalEmail;
    private readonly string _microsoftClientId;
    private readonly string _googleClientId;
    private bool _googleUseAppPassword;
    private CancellationTokenSource? _signInCancellation;
    private bool MicrosoftReady => Guid.TryParse(_microsoftClientId, out _);
    private bool GoogleReady => !string.IsNullOrWhiteSpace(_googleClientId);
    private bool GoogleOAuthSelected => _provider == "google" && GoogleReady && !_googleUseAppPassword;
    public MailAccount? Account { get; private set; }
    public string Password { get; private set; } = "";

    public AccountWindow(MailAccount? existing = null)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        _originalEmail = existing?.Email;
        _microsoftClientId = !string.IsNullOrWhiteSpace(existing?.OAuthClientId)
            && existing.AuthMethod == "microsoft-oauth" ? existing.OAuthClientId : MicrosoftOAuthService.ConfiguredClientId;
        _googleClientId = !string.IsNullOrWhiteSpace(existing?.OAuthClientId)
            && existing.AuthMethod == "google-oauth" ? existing.OAuthClientId : GoogleOAuthService.ConfiguredClientId;
        _googleUseAppPassword = existing?.AuthMethod == "password" &&
            existing.ImapHost.Equals("imap.gmail.com", StringComparison.OrdinalIgnoreCase);
        Localizer.Changed += ApplyLanguage;
        Closed += (_, _) => { _signInCancellation?.Cancel(); Localizer.Changed -= ApplyLanguage; };
        ApplyLanguage();
        if (existing is null)
        {
            SetPreset("laposte", true);
            EmailBox.Focus();
        }
        else
        {
            NameBox.Text = existing.DisplayName;
            EmailBox.Text = existing.Email;
            UsernameBox.Text = existing.UserName;
            SmtpUsernameBox.Text = existing.SmtpUserName;
            string localPart = existing.Email.Split('@')[0];
            _lastAutomaticUsername = existing.UserName == existing.Email || existing.UserName == localPart
                ? existing.UserName : "";
            _lastAutomaticSmtpUsername = existing.SmtpUserName == existing.Email
                ? existing.SmtpUserName : "";
            ImapBox.Text = existing.ImapHost;
            ImapPortBox.Text = existing.ImapPort.ToString();
            SmtpBox.Text = existing.SmtpHost;
            SmtpPortBox.Text = existing.SmtpPort.ToString();
            if (existing.AuthMethod == "password")
                PasswordInput.Password = AccountStore.LoadPassword(existing.Email) ?? "";
            string provider = existing.AuthMethod switch
            {
                "microsoft-oauth" => "microsoft",
                "google-oauth" => "google",
                _ => existing.ImapHost.ToLowerInvariant() switch
                {
                    "imap.laposte.net" => "laposte",
                    "imap.gmail.com" => "google",
                    "imap.mail.me.com" => "icloud",
                    _ => "manual"
                }
            };
            SetPreset(provider, false);
        }
        _initializing = false;
    }

    private void ApplyLanguage()
    {
        Heading.Text = Localizer.T("connectAccount");
        SubtitleLabel.Text = Localizer.T("accountSubtitle");
        ProviderLabel.Text = Localizer.T("provider");
        StorageNote.Text = Localizer.T("credentialStorage");
        LapostePreset.Content = Localizer.T("laposte");
        GooglePreset.Content = "Google";
        MicrosoftPreset.Content = "Outlook / Hotmail";
        IcloudPreset.Content = "iCloud";
        ManualPreset.Content = Localizer.T("manual");
        NameLabel.Text = Localizer.T("displayName");
        EmailLabel.Text = Localizer.T("email");
        PasswordLabel.Text = Localizer.T(_provider is "google" or "icloud" ? "appPassword" : "password");
        ProviderHint.Text = Localizer.T(_provider switch
        {
            "google" => GoogleOAuthSelected ? "googleOAuthHint" : "googleProviderHint",
            "icloud" => "icloudProviderHint",
            "microsoft" => MicrosoftReady ? "microsoftProviderHint" : "microsoftUnavailable",
            "manual" => "manualProviderHint",
            _ => "laposteProviderHint"
        });
        GoogleHelpButton.Content = Localizer.T("googleAppPasswordHelp");
        GoogleHelpButton.ToolTip = "https://myaccount.google.com/apppasswords";
        GoogleAuthSwitchButton.Content = Localizer.T(GoogleOAuthSelected ? "googleUseAppPassword" : "googleUseBrowser");
        MicrosoftPreset.ToolTip = MicrosoftReady ? Localizer.T("microsoftProviderHint") : Localizer.T("microsoftUnavailable");
        ServerHeading.Text = Localizer.T("serverSettings");
        UsernameLabel.Text = Localizer.T("username");
        SmtpUsernameLabel.Text = Localizer.T("smtpUsername");
        ImapLabel.Text = Localizer.T("imap");
        SmtpLabel.Text = Localizer.T("smtp");
        ImapPortLabel.Text = SmtpPortLabel.Text = Localizer.T("port");
        SecureNote.Text = Localizer.T("secureNote");
        CancelButton.Content = Localizer.T("cancel");
        ConnectButton.Content = Localizer.T(GoogleOAuthSelected ? "googleBrowserSignIn" : "connect");
    }

    private void SetPreset(string provider, bool applyDefaults)
    {
        _provider = provider;
        foreach (var (id, button) in new[]
        {
            ("laposte", LapostePreset), ("google", GooglePreset), ("microsoft", MicrosoftPreset),
            ("icloud", IcloudPreset), ("manual", ManualPreset)
        }) button.Style = (Style)FindResource(id == provider ? "NavActiveButton" : "ChromeButton");
        ServerExpander.IsExpanded = provider == "manual";
        PasswordLabel.Visibility = PasswordInput.Visibility = StorageNote.Visibility =
            provider == "microsoft" || GoogleOAuthSelected ? Visibility.Collapsed : Visibility.Visible;
        GoogleAuthSwitchButton.Visibility = provider == "google" && GoogleReady ? Visibility.Visible : Visibility.Collapsed;
        GoogleHelpButton.Visibility = provider == "google" && !GoogleOAuthSelected ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsEnabled = provider != "microsoft" || MicrosoftReady;
        ApplyLanguage();
        if (!applyDefaults || provider == "manual") return;
        (ImapBox.Text, SmtpBox.Text) = provider switch
        {
            "google" => ("imap.gmail.com", "smtp.gmail.com"),
            "microsoft" => ("outlook.office365.com", "smtp-mail.outlook.com"),
            "icloud" => ("imap.mail.me.com", "smtp.mail.me.com"),
            _ => ("imap.laposte.net", "smtp.laposte.net")
        };
        ImapPortBox.Text = "993";
        SmtpPortBox.Text = "587";
        UpdateAutomaticUsernames();
    }

    private void LapostePreset_Click(object sender, RoutedEventArgs e) => SetPreset("laposte", true);
    private void GooglePreset_Click(object sender, RoutedEventArgs e) => SetPreset("google", true);
    private void MicrosoftPreset_Click(object sender, RoutedEventArgs e) => SetPreset("microsoft", true);
    private void IcloudPreset_Click(object sender, RoutedEventArgs e) => SetPreset("icloud", true);
    private void ManualPreset_Click(object sender, RoutedEventArgs e) => SetPreset("manual", false);

    private void GoogleAuthSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        _googleUseAppPassword = !_googleUseAppPassword;
        SetPreset("google", false);
    }

    private void GoogleHelpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://myaccount.google.com/apppasswords") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorLabel.Text = Localizer.T("failed") + ": " + ex.Message;
        }
    }

    private void EmailBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        string? domain = EmailBox.Text.Trim().Split('@').LastOrDefault()?.ToLowerInvariant();
        string? detected = domain switch
        {
            "laposte.net" => "laposte",
            "gmail.com" or "googlemail.com" => "google",
            "outlook.com" or "hotmail.com" or "live.com" or "msn.com" => "microsoft",
            "icloud.com" or "me.com" or "mac.com" => "icloud",
            _ => null
        };
        if (detected is not null && detected != _provider && _provider != "manual") SetPreset(detected, true);
        UpdateAutomaticUsernames();
    }

    private void UpdateAutomaticUsernames()
    {
        string email = EmailBox.Text.Trim();
        string imapUser = _provider == "icloud" && email.Contains('@') ? email.Split('@')[0] : email;
        if (UsernameBox.Text.Length == 0 || UsernameBox.Text == _lastAutomaticUsername)
        {
            UsernameBox.Text = imapUser;
            _lastAutomaticUsername = UsernameBox.Text;
        }
        if (SmtpUsernameBox.Text.Length == 0 || SmtpUsernameBox.Text == _lastAutomaticSmtpUsername)
        {
            SmtpUsernameBox.Text = email;
            _lastAutomaticSmtpUsername = email;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Text = "";
        string email = EmailBox.Text.Trim();
        if (_provider == "microsoft" && !MicrosoftReady)
        {
            ErrorLabel.Text = Localizer.T("microsoftClientIdMissing");
            return;
        }
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email ||
            !int.TryParse(ImapPortBox.Text, out int imapPort) || imapPort is < 1 or > 65535 ||
            !int.TryParse(SmtpPortBox.Text, out int smtpPort) || smtpPort is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(ImapBox.Text) || string.IsNullOrWhiteSpace(SmtpBox.Text) ||
            (_provider != "microsoft" && !GoogleOAuthSelected && string.IsNullOrEmpty(PasswordInput.Password)))
        {
            ErrorLabel.Text = Localizer.T("invalidAccount");
            return;
        }
        var account = new MailAccount
        {
            DisplayName = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : email,
            Email = email,
            UserName = UsernameBox.Text.Trim().Length > 0 ? UsernameBox.Text.Trim() : email,
            SmtpUserName = SmtpUsernameBox.Text.Trim().Length > 0 ? SmtpUsernameBox.Text.Trim() : email,
            AuthMethod = _provider == "microsoft" ? "microsoft-oauth" : GoogleOAuthSelected ? "google-oauth" : "password",
            OAuthClientId = _provider == "microsoft" ? _microsoftClientId : GoogleOAuthSelected ? _googleClientId : "",
            ImapHost = ImapBox.Text.Trim(), ImapPort = imapPort,
            SmtpHost = SmtpBox.Text.Trim(), SmtpPort = smtpPort
        };
        ConnectButton.IsEnabled = false;
        ConnectButton.Content = Localizer.T("connecting");
        string stage = Localizer.T("connectingImap");
        using var cancellation = new CancellationTokenSource();
        _signInCancellation = cancellation;
        try
        {
            string secret;
            if (_provider == "microsoft")
            {
                stage = Localizer.T("microsoftSignIn");
                MicrosoftDeviceCode code = await MicrosoftOAuthService.StartAsync(account.OAuthClientId);
                var codeWindow = new OAuthCodeWindow(code) { Owner = this };
                codeWindow.Show();
                MicrosoftToken token;
                try { token = await MicrosoftOAuthService.CompleteAsync(account.OAuthClientId, code, codeWindow.CancellationToken); }
                finally { codeWindow.Complete(); }
                stage = Localizer.T("connectingImap");
                await using var oauthSession = await ImapSession.ConnectWithOAuthTokenAsync(account, token.AccessToken);
                secret = token.RefreshToken;
                MicrosoftOAuthService.CacheInitial(account, token);
            }
            else if (GoogleOAuthSelected)
            {
                stage = Localizer.T("googleBrowserSignIn");
                GoogleToken token = await GoogleOAuthService.SignInAsync(account.OAuthClientId, account.Email,
                    cancellation.Token);
                stage = Localizer.T("connectingImap");
                await using var oauthSession = await ImapSession.ConnectWithOAuthTokenAsync(account, token.AccessToken,
                    cancellation.Token);
                secret = token.RefreshToken;
                GoogleOAuthService.CacheInitial(account, token);
            }
            else
            {
                secret = _provider == "google" ? PasswordInput.Password.Replace(" ", "", StringComparison.Ordinal)
                    : PasswordInput.Password;
                await using var session = await ImapSession.ConnectAsync(account, secret);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            stage = Localizer.T("savingAccount");
            AccountStore.SaveAccount(account, secret, _originalEmail);
            Account = account;
            Password = secret;
            DialogResult = true;
        }
        catch (OperationCanceledException) { ErrorLabel.Text = Localizer.T("signInCancelled"); }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or TimeoutException)
        {
            ErrorLabel.Text = _provider == "google" && !GoogleOAuthSelected && ex.Message.Contains("Application-specific password required", StringComparison.OrdinalIgnoreCase)
                ? Localizer.T("googleAppPasswordRequired") : stage + " : " + ex.Message;
        }
        finally
        {
            _signInCancellation = null;
            ConnectButton.IsEnabled = _provider != "microsoft" || MicrosoftReady;
            ConnectButton.Content = Localizer.T(GoogleOAuthSelected ? "googleBrowserSignIn" : "connect");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _signInCancellation?.Cancel();
        DialogResult = false;
    }
}
