using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mailcario;

internal sealed class SignatureEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

internal sealed record SignatureChoice(string AccountEmail, SignatureEntry Signature);

internal sealed class SignatureCatalog
{
    public int Version { get; set; } = 2;
    public Dictionary<string, List<SignatureEntry>> Accounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal static class SignatureStore
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailcario", "signatures.json");

    public static List<SignatureEntry> List(string email)
    {
        SignatureCatalog catalog = LoadCatalog();
        var pair = catalog.Accounts.FirstOrDefault(p => p.Key.Equals(email, StringComparison.OrdinalIgnoreCase));
        return pair.Value?.Select(entry => new SignatureEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Text = entry.Text
        }).ToList() ?? [];
    }

    public static List<SignatureChoice> ListAll() => Choices(LoadCatalog());

    internal static List<SignatureChoice> Choices(SignatureCatalog catalog) => catalog.Accounts
        .SelectMany(account => account.Value.Select(signature => new SignatureChoice(account.Key,
            new SignatureEntry { Id = signature.Id, Name = signature.Name, Text = signature.Text })))
        .Where(choice => !string.IsNullOrWhiteSpace(choice.Signature.Name))
        .ToList();

    public static void Save(string email, IReadOnlyList<SignatureEntry> entries)
    {
        SignatureCatalog catalog = LoadCatalog();
        string key = catalog.Accounts.Keys.FirstOrDefault(k => k.Equals(email, StringComparison.OrdinalIgnoreCase)) ?? email;
        catalog.Accounts[key] = entries.Select(entry => new SignatureEntry
        {
            Id = entry.Id,
            Name = entry.Name.Trim(),
            Text = entry.Text
        }).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathName, true);
    }

    public static void RemoveAccount(string email)
    {
        SignatureCatalog catalog = LoadCatalog();
        string? key = catalog.Accounts.Keys.FirstOrDefault(value =>
            value.Equals(email, StringComparison.OrdinalIgnoreCase));
        if (key is null) return;
        catalog.Accounts.Remove(key);
        File.WriteAllText(PathName, JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SignatureCatalog LoadCatalog()
    {
        try { return Parse(File.ReadAllText(PathName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new SignatureCatalog(); }
    }

    internal static SignatureCatalog Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (document.RootElement.TryGetProperty("Accounts", out _))
        {
            var catalog = JsonSerializer.Deserialize<SignatureCatalog>(json) ?? new SignatureCatalog();
            catalog.Accounts ??= new Dictionary<string, List<SignatureEntry>>(StringComparer.OrdinalIgnoreCase);
            return catalog;
        }
        // Older releases stored one signature string per account.
        var legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        var migrated = new SignatureCatalog();
        foreach (var pair in legacy)
            if (!string.IsNullOrWhiteSpace(pair.Value))
                migrated.Accounts[pair.Key] =
                [
                    new SignatureEntry { Name = Localizer.T("signature") + " 1", Text = pair.Value }
                ];
        return migrated;
    }
}

internal sealed class SignaturesWindow : Window
{
    private readonly string _email;
    private readonly List<SignatureEntry> _entries;
    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly TextBox _text = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _empty = new();
    private SignatureEntry? _current;
    private bool _loading;
    private bool _dirty;

    public SignaturesWindow(string email)
    {
        _email = email;
        _entries = SignatureStore.List(email);
        Title = Localizer.T("manageSignatures");
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Mailcario;component/Assets/Mailcario-512.png"));
        Width = 730; Height = 490; MinWidth = 580; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Canvas");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowTheme.Attach(this);
        Closing += OnClosing;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = Title, FontSize = 21, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Ink") });
        header.Children.Add(new TextBlock { Text = email, Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("Muted") });
        root.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1); root.Children.Add(content);
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        content.Children.Add(left);
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(leftButtons, Dock.Bottom); left.Children.Add(leftButtons);
        var add = new Button { Content = Localizer.T("newSignature"), Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => AddEntry();
        var delete = new Button { Content = Localizer.T("delete"), IsEnabled = _entries.Count > 0 };
        delete.Click += (_, _) => DeleteEntry();
        leftButtons.Children.Add(add); leftButtons.Children.Add(delete);
        _list.DisplayMemberPath = "Name";
        _list.ItemsSource = _entries;
        _list.SelectionChanged += (_, _) =>
        {
            _current = _list.SelectedItem as SignatureEntry;
            _loading = true;
            _name.Text = _current?.Name ?? "";
            _text.Text = _current?.Text ?? "";
            _name.IsEnabled = _text.IsEnabled = _current is not null;
            delete.IsEnabled = _current is not null;
            _loading = false;
        };
        var listSurface = new Grid();
        listSurface.Children.Add(_list);
        _empty.Text = Localizer.T("noSignatures");
        _empty.Foreground = (Brush)Application.Current.FindResource("Muted");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.Margin = new Thickness(10);
        _empty.IsHitTestVisible = false;
        listSurface.Children.Add(_empty);
        left.Children.Add(listSurface);

        var editor = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(editor, 1); content.Children.Add(editor);
        editor.Children.Add(new TextBlock { Text = Localizer.T("signatureName"), FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Ink") });
        _name.Margin = new Thickness(0, 5, 0, 14);
        _name.Padding = new Thickness(12, 5, 12, 5);
        Grid.SetRow(_name, 1); editor.Children.Add(_name);
        var bodyLabel = new TextBlock { Text = Localizer.T("signatureText"), FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Ink") };
        Grid.SetRow(bodyLabel, 2); editor.Children.Add(bodyLabel);
        _text.AcceptsReturn = true;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.Margin = new Thickness(0, 5, 0, 0);
        Grid.SetRow(_text, 3); editor.Children.Add(_text);
        _name.TextChanged += EditorChanged;
        _text.TextChanged += EditorChanged;

        var bottom = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(bottom, 2); root.Children.Add(bottom);
        _status.Foreground = (Brush)Application.Current.FindResource("ErrorText");
        _status.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(actions, 1); bottom.Children.Add(actions);
        var close = new Button { Content = Localizer.T("close"), Margin = new Thickness(0, 0, 8, 0) };
        close.Click += (_, _) => Close();
        var save = new Button { Content = Localizer.T("save"),
            Style = (Style)Application.Current.FindResource("AccentButton") };
        save.Click += (_, _) => Save();
        actions.Children.Add(close); actions.Children.Add(save);
        UpdateEmptyState();
        if (_entries.Count > 0) _list.SelectedIndex = 0;
        else { _name.IsEnabled = _text.IsEnabled = false; }
    }

    private void UpdateEmptyState() =>
        _empty.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void EditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.Name = _name.Text;
        _current.Text = _text.Text;
        _dirty = true;
        _status.Text = "";
    }

    private void AddEntry()
    {
        int number = 1;
        string name;
        do { name = Localizer.T("signature") + " " + number++; }
        while (_entries.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var entry = new SignatureEntry { Name = name };
        _entries.Add(entry);
        _list.Items.Refresh();
        UpdateEmptyState();
        _list.SelectedItem = entry;
        _dirty = true;
        _name.Focus();
        _name.SelectAll();
    }

    private void DeleteEntry()
    {
        if (_current is null) return;
        _entries.Remove(_current);
        _list.Items.Refresh();
        UpdateEmptyState();
        _list.SelectedIndex = _entries.Count > 0 ? 0 : -1;
        _dirty = true;
        _status.Text = "";
    }

    private bool Save()
    {
        if (_entries.Any(entry => string.IsNullOrWhiteSpace(entry.Name)))
        {
            _status.Text = Localizer.T("signatureNameRequired");
            return false;
        }
        if (_entries.GroupBy(entry => entry.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            _status.Text = Localizer.T("signatureNameUnique");
            return false;
        }
        try
        {
            SignatureStore.Save(_email, _entries);
            _list.Items.Refresh();
            _dirty = false;
            _status.Text = Localizer.T("signaturesSaved");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = ex.Message;
            return false;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_dirty) return;
        MessageBoxResult choice = MessageBox.Show(this, Localizer.T("saveSignatureChanges"),
            Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.Yes && !Save()) e.Cancel = true;
    }
}
