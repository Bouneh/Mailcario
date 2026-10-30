using System.IO;
using System.Net.Mail;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

internal sealed class ContactEntry
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? Email : $"{Name}  ·  {Email}";
}

internal static class ContactStore
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailcario", "contacts.json");

    public static List<ContactEntry> Load()
    {
        try { return JsonSerializer.Deserialize<List<ContactEntry>>(File.ReadAllText(PathName)) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return []; }
    }

    public static void Save(IEnumerable<ContactEntry> contacts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(contacts.OrderBy(contact => contact.Name),
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathName, true);
    }
}

internal sealed class ContactsWindow : Window
{
    private readonly List<ContactEntry> _contacts = ContactStore.Load();
    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _searchHint = new();
    private readonly TextBlock _empty = new();
    private readonly TextBox _name = new();
    private readonly TextBox _email = new();
    private readonly TextBlock _status = new();

    public string? PickedEmail { get; private set; }
    public bool AddToCc { get; private set; }

    public ContactsWindow()
    {
        Title = Localizer.T("addressBook");
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Mailcario;component/Assets/Mailcario-512.png"));
        Width = 720; Height = 490; MinWidth = 560; MinHeight = 370;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Canvas");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        WindowTheme.Attach(this);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = Localizer.T("addressBook"), FontSize = 21,
            FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Ink") });
        _search.Margin = new Thickness(0, 10, 0, 0);
        _search.Padding = new Thickness(10, 6, 10, 6);
        _search.ToolTip = Localizer.T("searchContacts");
        _search.TextChanged += (_, _) => RefreshList();
        var searchSurface = new Grid();
        searchSurface.Children.Add(_search);
        _searchHint.Text = Localizer.T("searchContacts");
        _searchHint.Foreground = (Brush)FindResource("Muted");
        _searchHint.Margin = new Thickness(12, 15, 0, 0);
        _searchHint.IsHitTestVisible = false;
        searchSurface.Children.Add(_searchHint);
        heading.Children.Add(searchSurface);
        root.Children.Add(heading);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        Grid.SetRow(columns, 1); root.Children.Add(columns);
        _list.Margin = new Thickness(0, 0, 16, 0);
        _list.DisplayMemberPath = nameof(ContactEntry.DisplayLabel);
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is not ContactEntry contact) return;
            _name.Text = contact.Name; _email.Text = contact.Email;
        };
        var listSurface = new Grid();
        listSurface.Children.Add(_list);
        _empty.Foreground = (Brush)FindResource("Muted");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Margin = new Thickness(24);
        listSurface.Children.Add(_empty);
        columns.Children.Add(listSurface);

        var editor = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(editor, 1); columns.Children.Add(editor);
        editor.Children.Add(new TextBlock { Text = Localizer.T("contactName"), Foreground = (Brush)FindResource("Ink") });
        _name.Margin = new Thickness(0, 5, 0, 13); _name.Padding = new Thickness(10, 5, 10, 5);
        editor.Children.Add(_name);
        editor.Children.Add(new TextBlock { Text = Localizer.T("email"), Foreground = (Brush)FindResource("Ink") });
        _email.Margin = new Thickness(0, 5, 0, 13); _email.Padding = new Thickness(10, 5, 10, 5);
        editor.Children.Add(_email);
        var add = new Button { Content = Localizer.T("newContact"), Margin = new Thickness(0, 0, 0, 8) };
        add.Click += (_, _) => { _list.SelectedItem = null; _name.Clear(); _email.Clear(); _status.Text = ""; _name.Focus(); };
        editor.Children.Add(add);
        var save = new Button { Content = Localizer.T("saveContact"), Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)FindResource("AccentButton") };
        save.Click += (_, _) => SaveContact();
        editor.Children.Add(save);
        var delete = new Button { Content = Localizer.T("deleteContact") };
        delete.Click += (_, _) => DeleteContact();
        editor.Children.Add(delete);
        _status.Foreground = (Brush)FindResource("ErrorText");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 12, 0, 0);
        editor.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        Grid.SetRow(actions, 2); root.Children.Add(actions);
        var to = new Button { Content = Localizer.T("addToRecipient"), Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("AccentButton") };
        to.Click += (_, _) => Pick(false);
        var cc = new Button { Content = Localizer.T("addCcRecipient"), Margin = new Thickness(0, 0, 8, 0) };
        cc.Click += (_, _) => Pick(true);
        var close = new Button { Content = Localizer.T("close") };
        close.Click += (_, _) => Close();
        actions.Children.Add(to); actions.Children.Add(cc); actions.Children.Add(close);
        RefreshList();
    }

    private void RefreshList()
    {
        string query = _search.Text.Trim();
        var visible = _contacts.Where(contact => contact.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || contact.Email.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = visible;
        _searchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = _contacts.Count == 0 ? Localizer.T("noContacts") : Localizer.T("noContactMatches");
        _empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveContact()
    {
        string email = _email.Text.Trim();
        try
        {
            if (!new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
            ContactEntry contact = _list.SelectedItem as ContactEntry ??
                _contacts.FirstOrDefault(value => value.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) ?? new ContactEntry();
            if (_contacts.Any(value => value != contact && value.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
            { _status.Foreground = (Brush)FindResource("ErrorText"); _status.Text = Localizer.T("duplicateContact"); return; }
            if (!_contacts.Contains(contact)) _contacts.Add(contact);
            contact.Name = _name.Text.Trim(); contact.Email = email;
            ContactStore.Save(_contacts);
            RefreshList(); _list.SelectedItem = contact;
            _status.Foreground = (Brush)FindResource("AccentDark");
            _status.Text = Localizer.T("contactSaved");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or UnauthorizedAccessException)
        { _status.Foreground = (Brush)FindResource("ErrorText");
          _status.Text = ex is FormatException or ArgumentException ? Localizer.T("invalidEmail") : ex.Message; }
    }

    private void DeleteContact()
    {
        if (_list.SelectedItem is not ContactEntry contact) return;
        _contacts.Remove(contact);
        try { ContactStore.Save(_contacts); RefreshList(); _name.Clear(); _email.Clear(); _status.Text = ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; }
    }

    private void Pick(bool cc)
    {
        if (_list.SelectedItem is not ContactEntry contact) { _status.Text = Localizer.T("selectContact"); return; }
        PickedEmail = contact.Email; AddToCc = cc; DialogResult = true;
    }
}
