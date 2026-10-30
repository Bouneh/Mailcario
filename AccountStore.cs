using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mailcario;

public static class AccountStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailcario");
    private static readonly string AccountPath = Path.Combine(DirectoryPath, "account.json");
    private static readonly string PasswordPath = Path.Combine(DirectoryPath, "password.bin");
    private static readonly string AccountsPath = Path.Combine(DirectoryPath, "accounts.json");
    private static readonly string ActiveAccountPath = Path.Combine(DirectoryPath, "active-account.txt");
    private static readonly string CredentialsPath = Path.Combine(DirectoryPath, "credentials");
    private static readonly string LanguagePath = Path.Combine(DirectoryPath, "language.txt");

    public static IReadOnlyList<MailAccount> LoadAccounts()
    {
        var accounts = new List<MailAccount>();
        try
        {
            if (File.Exists(AccountsPath))
                accounts = JsonSerializer.Deserialize<List<MailAccount>>(File.ReadAllText(AccountsPath)) ?? [];
            if (!File.Exists(AccountsPath) && File.Exists(AccountPath))
            {
                var legacy = JsonSerializer.Deserialize<MailAccount>(File.ReadAllText(AccountPath));
                if (legacy is not null && !accounts.Any(a => SameEmail(a.Email, legacy.Email))) accounts.Insert(0, legacy);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { /* Keep accounts that were already read. */ }
        return accounts.Where(a => !string.IsNullOrWhiteSpace(a.Email))
            .DistinctBy(a => a.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? LoadActiveEmail()
    {
        try { return File.Exists(ActiveAccountPath) ? File.ReadAllText(ActiveAccountPath).Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void SaveActiveEmail(string email)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(ActiveAccountPath, email);
    }

    public static void SaveAccount(MailAccount account, string password, string? originalEmail = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(CredentialsPath);
        byte[] plain = Encoding.UTF8.GetBytes(password);
        byte[] entropy = Encoding.UTF8.GetBytes("Mailcario:" + account.Email.ToLowerInvariant());
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(CredentialPath(account.Email), protectedBytes);
            var accounts = LoadAccounts().ToList();
            int index = accounts.FindIndex(a => SameEmail(a.Email, originalEmail ?? account.Email));
            if (index < 0) index = accounts.FindIndex(a => SameEmail(a.Email, account.Email));
            if (index < 0) accounts.Add(account);
            else accounts[index] = account;
            File.WriteAllText(AccountsPath, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
            SaveActiveEmail(account.Email);
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public static int ImportAccounts(IEnumerable<MailAccount> candidates)
    {
        Directory.CreateDirectory(DirectoryPath);
        var accounts = LoadAccounts().ToList();
        int added = 0;
        foreach (MailAccount candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Email) || accounts.Any(a => SameEmail(a.Email, candidate.Email))) continue;
            accounts.Add(candidate);
            added++;
        }
        if (added > 0)
            File.WriteAllText(AccountsPath, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
        return added;
    }

    public static bool RemoveAccount(string email)
    {
        var accounts = LoadAccounts().ToList();
        int removed = accounts.RemoveAll(account => SameEmail(account.Email, email));
        if (removed == 0) return false;
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(AccountsPath, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
        File.Delete(CredentialPath(email));
        try
        {
            var legacy = File.Exists(AccountPath)
                ? JsonSerializer.Deserialize<MailAccount>(File.ReadAllText(AccountPath)) : null;
            if (legacy is not null && SameEmail(legacy.Email, email))
            {
                File.Delete(AccountPath);
                File.Delete(PasswordPath);
            }
        }
        catch (JsonException) { /* A malformed legacy entry cannot restore a removed account. */ }
        if (SameEmail(LoadActiveEmail(), email))
        {
            if (accounts.Count > 0) SaveActiveEmail(accounts[0].Email);
            else File.Delete(ActiveAccountPath);
        }
        return true;
    }

    public static void UpdateCredential(string email, string secret)
    {
        Directory.CreateDirectory(CredentialsPath);
        byte[] plain = Encoding.UTF8.GetBytes(secret);
        byte[] entropy = Encoding.UTF8.GetBytes("Mailcario:" + email.ToLowerInvariant());
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(CredentialPath(email), protectedBytes);
        }
        finally
        {
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public static string? LoadPassword(string email)
    {
        string path = CredentialPath(email);
        if (!File.Exists(path))
        {
            try
            {
                var legacy = File.Exists(AccountPath) ? JsonSerializer.Deserialize<MailAccount>(File.ReadAllText(AccountPath)) : null;
                if (legacy is null || !SameEmail(legacy.Email, email)) return null;
                path = PasswordPath;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
        }
        if (!File.Exists(path)) return null;
        byte[] entropy = Encoding.UTF8.GetBytes("Mailcario:" + email.ToLowerInvariant());
        try
        {
            byte[] protectedBytes = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { return null; }
        finally { CryptographicOperations.ZeroMemory(entropy); }
    }

    public static string LoadLanguage()
    {
        try { return File.ReadAllText(LanguagePath).Trim() == "en" ? "en" : "fr"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "fr"; }
    }

    public static void SaveLanguage(string language)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(LanguagePath, language);
    }

    private static bool SameEmail(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string CredentialPath(string email)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
        return Path.Combine(CredentialsPath, Convert.ToHexStringLower(hash) + ".bin");
    }
}
