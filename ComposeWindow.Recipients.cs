using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mailcario;

public partial class ComposeWindow
{
    private TextBox? _activeRecipientField;
    private bool _acceptingRecipientSuggestion;
    private List<ContactEntry> _suggestedContacts = [];
    private DateTimeOffset _contactsLoadedAt = DateTimeOffset.MinValue;

    private void RecipientBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox field) RefreshRecipientSuggestions(field);
    }

    private void RecipientBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_acceptingRecipientSuggestion && IsLoaded && sender is TextBox field && field.IsKeyboardFocused)
            RefreshRecipientSuggestions(field);
    }

    private void RefreshRecipientSuggestions(TextBox field)
    {
        _activeRecipientField = field;
        (int start, int end) = RecipientTokenBounds(field);
        string query = field.Text[start..end].Trim();
        if (query.Length == 0 || (System.Net.Mail.MailAddress.TryCreate(query, out var complete) &&
            complete.Address.Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            RecipientSuggestionsPopup.IsOpen = false;
            return;
        }
        if (DateTimeOffset.UtcNow - _contactsLoadedAt > TimeSpan.FromSeconds(5))
        {
            _suggestedContacts = ContactStore.Load();
            _contactsLoadedAt = DateTimeOffset.UtcNow;
        }
        var matches = _suggestedContacts
            .Where(contact => contact.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              contact.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(contact => contact.Email.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                                          contact.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(contact => contact.Source switch { "manual" => 0, "sent" => 1, _ => 2 })
            .ThenBy(contact => contact.Name.Length == 0 ? contact.Email : contact.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .Take(8).ToList();
        RecipientSuggestions.ItemsSource = matches;
        RecipientSuggestions.SelectedIndex = matches.Count > 0 ? 0 : -1;
        RecipientSuggestionsPopup.PlacementTarget = field;
        RecipientSuggestionsPopup.IsOpen = matches.Count > 0;
    }

    private static (int Start, int End) RecipientTokenBounds(TextBox field)
    {
        string text = field.Text;
        if (text.Length == 0) return (0, 0);
        int caret = Math.Clamp(field.CaretIndex, 0, text.Length);
        int start = caret == 0 ? 0 : text.LastIndexOfAny([',', ';'], caret - 1) + 1;
        int end = text.IndexOfAny([',', ';'], caret);
        return (start, end < 0 ? text.Length : end);
    }

    private void RecipientBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!RecipientSuggestionsPopup.IsOpen || sender != _activeRecipientField) return;
        if (e.Key is Key.Down or Key.Up)
        {
            int count = RecipientSuggestions.Items.Count;
            if (count > 0)
                RecipientSuggestions.SelectedIndex = (RecipientSuggestions.SelectedIndex +
                    (e.Key == Key.Down ? 1 : count - 1)) % count;
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Tab)
        {
            AcceptRecipientSuggestion();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RecipientSuggestionsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void RecipientSuggestions_Click(object sender, MouseButtonEventArgs e) =>
        AcceptRecipientSuggestion();

    private void AcceptRecipientSuggestion()
    {
        if (_activeRecipientField is not { } field ||
            RecipientSuggestions.SelectedItem is not ContactEntry contact) return;
        (int start, int end) = RecipientTokenBounds(field);
        string prefix = field.Text[..start].TrimEnd();
        string suffix = field.Text[end..];
        string replacement = (prefix.Length > 0 ? prefix + " " : "") + contact.Email +
            (suffix.Length == 0 ? "; " : suffix);
        _acceptingRecipientSuggestion = true;
        RecipientSuggestionsPopup.IsOpen = false;
        field.Text = replacement;
        field.CaretIndex = (prefix.Length > 0 ? prefix.Length + 1 : 0) + contact.Email.Length +
            (suffix.Length == 0 ? 2 : 0);
        _acceptingRecipientSuggestion = false;
        field.Focus();
    }
}
