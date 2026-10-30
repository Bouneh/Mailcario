using System.Windows;
using System.Windows.Controls;

namespace Mailcario;

public partial class SettingsWindow : Window
{
    public ClientSettings Settings { get; private set; }
    private readonly HashSet<string> _accountsToRemove = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> AccountsToRemove => _accountsToRemove;

    public SettingsWindow(ClientSettings settings, IReadOnlyList<MailAccount> accounts)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        Settings = settings.Copy();
        Heading.Text = Localizer.T("clientSettings");
        Subtitle.Text = Localizer.T("settingsSubtitle");
        AppearanceHeading.Text = Localizer.T("appearanceSettings");
        ThemeLabel.Text = Localizer.T("themeMode");
        ((ComboBoxItem)ThemeCombo.Items[0]).Content = Localizer.T("themeSystem");
        ((ComboBoxItem)ThemeCombo.Items[1]).Content = Localizer.T("themeLight");
        ((ComboBoxItem)ThemeCombo.Items[2]).Content = Localizer.T("themeDark");
        ThemeCombo.SelectedItem = ThemeCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == Settings.ThemeMode) ?? ThemeCombo.Items[0];
        NotificationsHeading.Text = Localizer.T("notificationSettings");
        NotifyCheck.Content = Localizer.T("enableNotifications");
        SoundCheck.Content = Localizer.T("enableSound");
        PreviewCheck.Content = Localizer.T("notificationPreview");
        IntervalLabel.Text = Localizer.T("checkInterval");
        LayoutHeading.Text = Localizer.T("layoutSettings");
        RibbonCheck.Content = Localizer.T("showRibbon");
        ReaderCheck.Content = Localizer.T("showReader");
        CompactCheck.Content = Localizer.T("compactMessages");
        RibbonGroupsHeading.Text = Localizer.T("customizeRibbon");
        HomeCheck.Content = Localizer.T("homeTab");
        SyncCheck.Content = Localizer.T("sendReceiveTab");
        FolderCheck.Content = Localizer.T("folderTab");
        ViewCheck.Content = Localizer.T("viewTab");
        ImagesHeading.Text = Localizer.T("imagesPerAccount");
        ImagesRisk.Text = Localizer.T("autoLoadImagesRisk");
        AccountsHeading.Text = Localizer.T("accounts");
        RemoveAccountButton.Content = Localizer.T("removeAccount");
        RemoveAccountHint.Text = Localizer.T("removeAccountHint");
        foreach (MailAccount account in accounts)
        {
            AccountsList.Items.Add(account);
            ImageAccountOptions.Children.Add(new CheckBox
            {
                Content = string.Format(Localizer.T("autoLoadImages"), account.Email),
                Tag = account.Email,
                IsChecked = Settings.AutoLoadImages.TryGetValue(account.Email, out bool enabled) && enabled,
                Margin = new Thickness(0, 0, 0, 9),
                Foreground = (System.Windows.Media.Brush)FindResource("Ink")
            });
        }
        CancelButton.Content = Localizer.T("cancel");
        SaveButton.Content = Localizer.T("save");
        NotifyCheck.IsChecked = Settings.NotifyNewMail;
        SoundCheck.IsChecked = Settings.PlaySound;
        PreviewCheck.IsChecked = Settings.ShowMessagePreview;
        IntervalBox.SelectedItem = IntervalBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == Settings.CheckIntervalMinutes.ToString()) ?? IntervalBox.Items[1];
        RibbonCheck.IsChecked = Settings.ShowRibbon;
        ReaderCheck.IsChecked = Settings.ShowReadingPane;
        CompactCheck.IsChecked = Settings.CompactMessages;
        HomeCheck.IsChecked = Settings.HomeCommands;
        SyncCheck.IsChecked = Settings.SyncCommands;
        FolderCheck.IsChecked = Settings.FolderCommands;
        ViewCheck.IsChecked = Settings.ViewCommands;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.NotifyNewMail = NotifyCheck.IsChecked == true;
        Settings.PlaySound = SoundCheck.IsChecked == true;
        Settings.ShowMessagePreview = PreviewCheck.IsChecked == true;
        Settings.CheckIntervalMinutes = int.TryParse((IntervalBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int minutes)
            ? minutes : 2;
        Settings.ThemeMode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        Settings.ShowRibbon = RibbonCheck.IsChecked == true;
        Settings.ShowReadingPane = ReaderCheck.IsChecked == true;
        Settings.CompactMessages = CompactCheck.IsChecked == true;
        Settings.HomeCommands = HomeCheck.IsChecked == true;
        Settings.SyncCommands = SyncCheck.IsChecked == true;
        Settings.FolderCommands = FolderCheck.IsChecked == true;
        Settings.ViewCommands = ViewCheck.IsChecked == true;
        foreach (CheckBox check in ImageAccountOptions.Children.OfType<CheckBox>())
            if (check.Tag is string email) Settings.AutoLoadImages[email] = check.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveAccountButton.IsEnabled = AccountsList.SelectedItem is MailAccount;

    private void RemoveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsList.SelectedItem is not MailAccount account) return;
        string warning = string.Format(Localizer.T("removeAccountConfirm"), account.Email);
        if (MessageBox.Show(this, warning, Localizer.T("removeAccount"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _accountsToRemove.Add(account.Email);
        AccountsList.Items.Remove(account);
        foreach (CheckBox check in ImageAccountOptions.Children.OfType<CheckBox>().Where(check =>
                     string.Equals(check.Tag as string, account.Email, StringComparison.OrdinalIgnoreCase)).ToList())
            ImageAccountOptions.Children.Remove(check);
    }
}
