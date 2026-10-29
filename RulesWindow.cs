using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

public sealed class RulesWindow : Window
{
    private readonly string _email;
    private readonly uint _minimumUid;
    private readonly uint _uidValidity;
    private readonly List<MailRule> _allRules;
    private readonly ListBox _list = new();
    private readonly TextBox _sender = new();
    private readonly TextBox _subject = new();
    private readonly ComboBox _action = new();
    private readonly ComboBox _target = new();
    private TextBlock? _targetLabel;
    private readonly TextBlock _emptyRules = new();
    private readonly CheckBox _enabled = new();
    private readonly TextBlock _error = new();
    private Guid? _editingId;

    public RulesWindow(MailAccount account, IReadOnlyList<MailFolder> folders, uint minimumUid, uint uidValidity,
        MailItem? fromMessage = null, string? preferredTarget = null)
    {
        _email = account.Email;
        _minimumUid = minimumUid;
        _uidValidity = uidValidity;
        _allRules = MailRuleStore.Load();
        Title = Localizer.T("rules");
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Mailcario;component/Assets/Mailcario-512.png"));
        Width = 750; Height = 540; MinWidth = 630; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Canvas");
        Foreground = (Brush)FindResource("Ink");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        WindowTheme.Attach(this);

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = Localizer.T("rules"), FontSize = 22,
            FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Ink") });
        heading.Children.Add(new TextBlock { Text = _email + " · " + Localizer.T("rulesFutureOnly"),
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(heading);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(columns, 1); root.Children.Add(columns);
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        columns.Children.Add(left);
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        var add = new Button { Content = Localizer.T("newRule"), Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => NewRule();
        var remove = new Button { Content = Localizer.T("deleteRule") };
        remove.Click += (_, _) => DeleteSelected();
        leftButtons.Children.Add(add); leftButtons.Children.Add(remove);
        _list.DisplayMemberPath = "Description";
        _list.SelectionChanged += (_, _) => LoadSelected();
        var listSurface = new Grid();
        listSurface.Children.Add(_list);
        _emptyRules.Text = Localizer.T("noRules");
        _emptyRules.Foreground = (Brush)FindResource("Muted");
        _emptyRules.TextWrapping = TextWrapping.Wrap;
        _emptyRules.Margin = new Thickness(12);
        _emptyRules.IsHitTestVisible = false;
        listSurface.Children.Add(_emptyRules);
        left.Children.Add(listSurface);

        var editor = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var editorScroll = new ScrollViewer { Content = editor,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(editorScroll, 1); columns.Children.Add(editorScroll);
        editor.Children.Add(FieldLabel("ruleSender"));
        _sender.Margin = new Thickness(0, 5, 0, 14); editor.Children.Add(_sender);
        editor.Children.Add(FieldLabel("ruleSubject"));
        _subject.Margin = new Thickness(0, 5, 0, 14); editor.Children.Add(_subject);
        editor.Children.Add(FieldLabel("ruleAction"));
        _action.Items.Add(new ComboBoxItem { Content = Localizer.T("markRead"), Tag = "mark-read" });
        _action.Items.Add(new ComboBoxItem { Content = Localizer.T("markUnread"), Tag = "mark-unread" });
        _action.Items.Add(new ComboBoxItem { Content = Localizer.T("followUp"), Tag = "flag" });
        _action.Items.Add(new ComboBoxItem { Content = Localizer.T("moveToFolder"), Tag = "move" });
        _action.SelectedIndex = 0;
        _action.SelectionChanged += (_, _) => UpdateTargetVisibility();
        _action.Margin = new Thickness(0, 5, 0, 14); editor.Children.Add(_action);
        _targetLabel = FieldLabel("targetFolder");
        editor.Children.Add(_targetLabel);
        foreach (MailFolder folder in folders.Where(f => f.CanSelect &&
            !f.ServerName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)))
            _target.Items.Add(new ComboBoxItem { Content = folder.DisplayName, Tag = folder.ServerName });
        _target.SelectedIndex = _target.Items.Count > 0 ? 0 : -1;
        _target.Margin = new Thickness(0, 5, 0, 14); editor.Children.Add(_target);
        UpdateTargetVisibility();
        _enabled.Content = Localizer.T("ruleEnabled"); _enabled.IsChecked = true;
        editor.Children.Add(_enabled);
        _error.Foreground = (Brush)FindResource("ErrorText");
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, 12, 0, 0); editor.Children.Add(_error);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0) };
        Grid.SetRow(bottom, 2); root.Children.Add(bottom);
        var close = new Button { Content = Localizer.T("close"), Margin = new Thickness(0, 0, 9, 0) };
        close.Click += (_, _) => Close();
        var save = new Button { Content = Localizer.T("save"), Style = (Style)FindResource("AccentButton") };
        save.Click += (_, _) => SaveRule();
        bottom.Children.Add(close); bottom.Children.Add(save);

        RefreshList();
        if (fromMessage is not null)
        {
            NewRule();
            _sender.Text = fromMessage.FromAddress;
            if (preferredTarget is not null)
            {
                _action.SelectedItem = _action.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x => x.Tag?.ToString() == "move");
                _target.SelectedItem = _target.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(x => x.Tag?.ToString() == preferredTarget);
            }
        }
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else NewRule();
    }

    private TextBlock FieldLabel(string key) => new()
    {
        Text = Localizer.T(key), Foreground = (Brush)FindResource("Ink"), FontWeight = FontWeights.SemiBold
    };

    private void RefreshList()
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _allRules.Where(r => r.AccountEmail.Equals(_email, StringComparison.OrdinalIgnoreCase)).ToList();
        _emptyRules.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTargetVisibility()
    {
        bool move = (_action.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "move";
        _target.Visibility = move ? Visibility.Visible : Visibility.Collapsed;
        if (_targetLabel is not null) _targetLabel.Visibility = _target.Visibility;
    }

    private void NewRule()
    {
        _list.SelectedItem = null; _editingId = null;
        _sender.Clear(); _subject.Clear(); _action.SelectedIndex = 0;
        _enabled.IsChecked = true; _error.Text = "";
    }

    private void LoadSelected()
    {
        if (_list.SelectedItem is not MailRule rule) return;
        _editingId = rule.Id;
        _sender.Text = rule.SenderContains;
        _subject.Text = rule.SubjectContains;
        _action.SelectedItem = _action.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == rule.Action) ?? _action.Items[0];
        _target.SelectedItem = _target.Items.Cast<ComboBoxItem>().FirstOrDefault(x =>
            x.Tag?.ToString() == rule.TargetFolder);
        _enabled.IsChecked = rule.Enabled;
        _error.Text = "";
    }

    private void SaveRule()
    {
        string sender = _sender.Text.Trim();
        string subject = _subject.Text.Trim();
        string action = (_action.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mark-read";
        string target = (_target.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        if (sender.Length == 0 && subject.Length == 0)
        { _error.Text = Localizer.T("ruleConditionRequired"); return; }
        if (action == "move" && target.Length == 0)
        { _error.Text = Localizer.T("ruleFolderRequired"); return; }
        MailRule? rule = _editingId is Guid id ? _allRules.FirstOrDefault(x => x.Id == id) : null;
        if (rule is null)
        {
            rule = new MailRule { AccountEmail = _email, MinimumUid = _minimumUid,
                LastProcessedUid = _minimumUid, UidValidity = _uidValidity };
            _allRules.Add(rule);
        }
        rule.SenderContains = sender; rule.SubjectContains = subject;
        rule.Action = action; rule.TargetFolder = action == "move" ? target : "";
        rule.Enabled = _enabled.IsChecked == true;
        try
        {
            MailRuleStore.Save(_allRules);
            _editingId = rule.Id;
            RefreshList();
            _list.SelectedItem = _list.Items.Cast<MailRule>().FirstOrDefault(x => x.Id == rule.Id);
            _error.Text = Localizer.T("ruleSaved");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { _error.Text = ex.Message; }
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not MailRule rule) return;
        _allRules.Remove(rule);
        try { MailRuleStore.Save(_allRules); RefreshList(); NewRule(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { _allRules.Add(rule); _error.Text = ex.Message; }
    }
}
