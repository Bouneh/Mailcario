using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Mailcario;

public sealed record MicrosoftDeviceCode(string DeviceCode, string UserCode, Uri VerificationUri,
    int IntervalSeconds, DateTimeOffset ExpiresAt);
public sealed record MicrosoftToken(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public static class MicrosoftOAuthService
{
    private const string DeviceEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string Scopes = "offline_access https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, MicrosoftToken> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string ConfiguredClientId
    {
        get
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "oauth-providers.json");
                if (!File.Exists(path)) return "";
                using var config = JsonDocument.Parse(File.ReadAllText(path));
                string? value = config.RootElement.GetProperty("microsoftClientId").GetString();
                return Guid.TryParse(value, out _) ? value! : "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
            { return ""; }
        }
    }

    public static async Task<MicrosoftDeviceCode> StartAsync(string clientId, CancellationToken token = default)
    {
        using JsonDocument response = await PostAsync(DeviceEndpoint,
            new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = Scopes }, token);
        var root = response.RootElement;
        string verificationUrl = root.GetProperty("verification_uri").GetString() ?? "";
        if (!Uri.TryCreate(verificationUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !(uri.Host.Equals("microsoft.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".microsoftonline.com", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Microsoft returned an invalid sign-in URL.");
        return new MicrosoftDeviceCode(
            root.GetProperty("device_code").GetString() ?? throw new IOException("Missing device code."),
            root.GetProperty("user_code").GetString() ?? throw new IOException("Missing user code."), uri,
            Math.Clamp(root.GetProperty("interval").GetInt32(), 5, 30),
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    public static async Task<MicrosoftToken> CompleteAsync(string clientId, MicrosoftDeviceCode code,
        CancellationToken token = default)
    {
        int interval = code.IntervalSeconds;
        while (DateTimeOffset.UtcNow < code.ExpiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = code.DeviceCode
            });
            using HttpResponseMessage response = await Client.PostAsync(TokenEndpoint, form, token);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (response.IsSuccessStatusCode) return ReadToken(body.RootElement, null);
            string error = body.RootElement.TryGetProperty("error", out var value) ? value.GetString() ?? "" : "";
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval = Math.Min(interval + 5, 60); continue; }
            throw OAuthError(body.RootElement);
        }
        throw new TimeoutException("Microsoft sign-in expired. Please try again.");
    }

    private static string CacheKey(MailAccount account) => account.Email + "|" + account.OAuthClientId;

    public static void CacheInitial(MailAccount account, MicrosoftToken token) => Cache[CacheKey(account)] = token;

    public static async Task<string> AccessTokenAsync(MailAccount account, string storedRefreshToken,
        CancellationToken token = default)
    {
        string cacheKey = CacheKey(account);
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.AccessToken;
        await TokenGate.WaitAsync(token);
        try
        {
            if (Cache.TryGetValue(cacheKey, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return cached.AccessToken;
            string refreshToken = AccountStore.LoadPassword(account.Email) ?? storedRefreshToken;
            using JsonDocument response = await PostAsync(TokenEndpoint, new Dictionary<string, string>
            {
                ["client_id"] = account.OAuthClientId,
                ["grant_type"] = "refresh_token",
                ["scope"] = Scopes,
                ["refresh_token"] = refreshToken
            }, token);
            MicrosoftToken acquired = ReadToken(response.RootElement, refreshToken);
            Cache[cacheKey] = acquired;
            if (acquired.RefreshToken != refreshToken)
                AccountStore.UpdateCredential(account.Email, acquired.RefreshToken);
            return acquired.AccessToken;
        }
        finally { TokenGate.Release(); }
    }

    private static MicrosoftToken ReadToken(JsonElement root, string? oldRefreshToken)
    {
        string access = root.GetProperty("access_token").GetString() ?? throw new IOException("Missing access token.");
        string refresh = root.TryGetProperty("refresh_token", out var property)
            ? property.GetString() ?? oldRefreshToken ?? "" : oldRefreshToken ?? "";
        if (refresh.Length == 0) throw new IOException("Microsoft did not return a refresh token.");
        return new MicrosoftToken(access, refresh,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    private static async Task<JsonDocument> PostAsync(string endpoint, Dictionary<string, string> parameters,
        CancellationToken token)
    {
        using var form = new FormUrlEncodedContent(parameters);
        using HttpResponseMessage response = await Client.PostAsync(endpoint, form, token);
        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (response.IsSuccessStatusCode) return body;
        Exception error = OAuthError(body.RootElement);
        body.Dispose();
        throw error;
    }

    private static IOException OAuthError(JsonElement root)
    {
        string code = root.TryGetProperty("error", out var error) ? error.GetString() ?? "OAuth error" : "OAuth error";
        string detail = root.TryGetProperty("error_description", out var description)
            ? description.GetString() ?? "" : "";
        return new IOException("Microsoft OAuth: " + code + (detail.Length > 0 ? " — " + detail[..Math.Min(250, detail.Length)] : ""));
    }
}
