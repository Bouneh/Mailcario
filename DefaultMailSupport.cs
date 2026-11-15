using System.Diagnostics;
using System.Net.Mail;
using System.Windows;
using Microsoft.Win32;

namespace Mailcario;

public sealed record MailtoRequest(string To, string Cc, string Bcc, string Subject, string Body)
{
    public static bool TryParse(string? value, out MailtoRequest? request)
    {
        request = null;
        if (value is null || value.Length > 8192 ||
            !value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return false;
        string content = value[7..];
        int queryStart = content.IndexOf('?');
        string addressPart = queryStart < 0 ? content : content[..queryStart];
        string query = queryStart < 0 ? "" : content[(queryStart + 1)..];
        try
        {
            var fields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = pair.IndexOf('=');
                if (separator < 0) continue;
                string name = Uri.UnescapeDataString(pair[..separator]).ToLowerInvariant();
                if (name is not ("to" or "cc" or "bcc" or "subject" or "body")) continue;
                if (!fields.TryGetValue(name, out var values)) fields[name] = values = [];
                values.Add(Uri.UnescapeDataString(pair[(separator + 1)..]));
            }
            static string Get(Dictionary<string, List<string>> values, string key) =>
                values.TryGetValue(key, out var parts) ? string.Join(key == "body" ? "\n" : ",", parts) : "";
            static string Addresses(string raw)
            {
                var result = new List<string>();
                foreach (string candidate in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (result.Count == 30 || candidate.Any(char.IsControl) ||
                        !MailAddress.TryCreate(candidate, out var address) ||
                        !candidate.Equals(address.Address, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("Invalid mailto recipient.");
                    result.Add(address.Address);
                }
                return string.Join("; ", result);
            }
            string to = Addresses(Uri.UnescapeDataString(addressPart) + "," + Get(fields, "to"));
            string cc = Addresses(Get(fields, "cc"));
            string bcc = Addresses(Get(fields, "bcc"));
            string subject = Get(fields, "subject").Replace('\r', ' ').Replace('\n', ' ');
            string body = Get(fields, "body");
            if (subject.Length > 500 || body.Length > 10_000) return false;
            request = new MailtoRequest(to, cc, bcc, subject, body);
            return true;
        }
        catch (Exception ex) when (ex is UriFormatException or FormatException or ArgumentException) { return false; }
    }
}

public static class DefaultMailSupport
{
    public static bool IsCurrentInstallationRegistered()
    {
        try
        {
            using var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            using var command = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Mailcario.Mailto\shell\open\command");
            string? executable = Environment.ProcessPath;
            return registered?.GetValue("Mailcario") is string &&
                executable is not null &&
                command?.GetValue(null) is string value &&
                value.StartsWith("\"" + executable + "\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        { return false; }
    }

    public static void PromptOnFirstRun(Window owner, ClientSettings settings)
    {
        if (settings.DefaultMailPromptShown || !IsCurrentInstallationRegistered()) return;
        settings.DefaultMailPromptShown = true;
        try { settings.Save(); }
        catch (Exception) { /* The choice can still be offered in Settings. */ }
        if (MessageBox.Show(owner, Localizer.T("defaultMailPrompt"), Localizer.T("defaultMailHeading"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            OpenWindowsDefaultApps();
    }

    public static void OpenWindowsDefaultApps()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Mailcario")
                { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
            catch (Exception fallback) when (fallback is System.ComponentModel.Win32Exception or InvalidOperationException)
            { MessageBox.Show(Localizer.T("defaultMailSettingsError"), "Mailcario", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
