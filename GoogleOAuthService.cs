using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mailcario;

public sealed record GoogleToken(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public static class GoogleOAuthService
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string MailScope = "https://mail.google.com/";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, GoogleToken> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string ConfiguredClientId
    {
        get
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "oauth-providers.json");
                if (!File.Exists(path)) return "";
                using var config = JsonDocument.Parse(File.ReadAllText(path));
                string? value = config.RootElement.GetProperty("googleClientId").GetString();
                return value is not null && value.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)
                    ? value : "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
            { return ""; }
        }
    }

    public static async Task<GoogleToken> SignInAsync(string clientId, string email,
        CancellationToken cancellation = default)
    {
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Google OAuth client ID is missing or invalid.");
        string clientSecret = LoadClientSecret(clientId);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string redirectUri = $"http://127.0.0.1:{port}/";
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        string authorizationUrl = BuildAuthorizationUri(clientId, email, redirectUri, challenge, state);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });
            string code = await WaitForCodeAsync(listener, port, state, timeout.Token);
            using JsonDocument response = await PostAsync(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            }, timeout.Token);
            return ReadToken(response.RootElement, null);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("Google sign-in expired. Please try again."); }
    }

    internal static string BuildAuthorizationUri(string clientId, string email, string redirectUri,
        string challenge, string state)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = MailScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["login_hint"] = email
        };
        return AuthorizationEndpoint + "?" + string.Join("&", values.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }

    private static async Task<string> WaitForCodeAsync(TcpListener listener, int port, string expectedState,
        CancellationToken cancellation)
    {
        while (true)
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(cancellation);
            if (connection.Client.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address))
                continue;
            using NetworkStream stream = connection.GetStream();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string? request;
            try { request = await ReadHeadersAsync(stream, requestTimeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { continue; }
            if (request is null) continue;
            string[] lines = request.Split("\r\n", StringSplitOptions.None);
            string[] firstLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string expectedHost = "127.0.0.1:" + port;
            bool validHost = lines.Skip(1).Any(line =>
                line.Equals("Host: " + expectedHost, StringComparison.OrdinalIgnoreCase));
            if (firstLine.Length != 3 || firstLine[0] != "GET" || !validHost ||
                !Uri.TryCreate("http://" + expectedHost + firstLine[1], UriKind.Absolute, out Uri? callback) ||
                callback.AbsolutePath != "/" || callback.Host != "127.0.0.1" || callback.Port != port)
            {
                await WritePageAsync(stream, false, cancellation);
                continue;
            }
            var query = ParseQuery(callback.Query);
            if (!query.TryGetValue("state", out string? returnedState) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedState),
                    Encoding.ASCII.GetBytes(returnedState)))
            {
                await WritePageAsync(stream, false, cancellation);
                continue;
            }
            if (query.TryGetValue("error", out string? error))
            {
                await WritePageAsync(stream, true, cancellation);
                throw new IOException("Google OAuth: " + SafeErrorCode(error));
            }
            if (!query.TryGetValue("code", out string? code) || code.Length is < 1 or > 4096)
            {
                await WritePageAsync(stream, false, cancellation);
                continue;
            }
            await WritePageAsync(stream, true, cancellation);
            return code;
        }
    }

    private static async Task<string?> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellation)
    {
        byte[] buffer = new byte[8192];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(count), cancellation);
            if (read == 0) return null;
            count += read;
            if (count >= 4 && buffer.AsSpan(0, count).IndexOf("\r\n\r\n"u8) >= 0)
                return Encoding.ASCII.GetString(buffer, 0, count);
        }
        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            string key = WebUtility.UrlDecode(pair[0]);
            if (!values.TryAdd(key, pair.Length == 2 ? WebUtility.UrlDecode(pair[1]) : ""))
                return new Dictionary<string, string>();
        }
        return values;
    }

    private static async Task WritePageAsync(NetworkStream stream, bool success, CancellationToken cancellation)
    {
        string page = success
            ? "<!doctype html><meta charset=utf-8><title>Mailcario</title><p>Autorisation reçue. Revenez à Mailcario pour terminer la connexion.</p><p>Authorization received. Return to Mailcario to finish signing in.</p>"
            : "<!doctype html><meta charset=utf-8><title>Mailcario</title><p>Réponse de connexion invalide. Revenez à Mailcario et réessayez.</p><p>Invalid sign-in response. Return to Mailcario and try again.</p>";
        byte[] body = Encoding.UTF8.GetBytes(page);
        string header = $"HTTP/1.1 {(success ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Security-Policy: default-src 'none'; style-src 'none'; sandbox\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellation);
        await stream.WriteAsync(body, cancellation);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string CacheKey(MailAccount account) => account.Email + "|" + account.OAuthClientId;

    public static void CacheInitial(MailAccount account, GoogleToken token) => Cache[CacheKey(account)] = token;

    public static async Task<string> AccessTokenAsync(MailAccount account, string storedRefreshToken,
        CancellationToken cancellation = default)
    {
        string key = CacheKey(account);
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.AccessToken;
        await TokenGate.WaitAsync(cancellation);
        try
        {
            if (Cache.TryGetValue(key, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return cached.AccessToken;
            string refreshToken = AccountStore.LoadPassword(account.Email) ?? storedRefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new IOException("Google authorization has expired. Reconnect the account.");
            using JsonDocument response = await PostAsync(new Dictionary<string, string>
            {
                ["client_id"] = account.OAuthClientId,
                ["client_secret"] = LoadClientSecret(account.OAuthClientId),
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            }, cancellation);
            GoogleToken acquired = ReadToken(response.RootElement, refreshToken);
            Cache[key] = acquired;
            if (acquired.RefreshToken != refreshToken)
                AccountStore.UpdateCredential(account.Email, acquired.RefreshToken);
            return acquired.AccessToken;
        }
        finally { TokenGate.Release(); }
    }

    private static GoogleToken ReadToken(JsonElement root, string? oldRefreshToken)
    {
        string access = root.GetProperty("access_token").GetString() ?? throw new IOException("Google did not return an access token.");
        string refresh = root.TryGetProperty("refresh_token", out var property)
            ? property.GetString() ?? oldRefreshToken ?? "" : oldRefreshToken ?? "";
        if (refresh.Length == 0) throw new IOException("Google did not return a refresh token.");
        return new GoogleToken(access, refresh,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    private static async Task<JsonDocument> PostAsync(Dictionary<string, string> parameters, CancellationToken cancellation)
    {
        using var form = new FormUrlEncodedContent(parameters);
        using HttpResponseMessage response = await Client.PostAsync(TokenEndpoint, form, cancellation);
        using var content = await response.Content.ReadAsStreamAsync(cancellation);
        JsonDocument body = await JsonDocument.ParseAsync(content, cancellationToken: cancellation);
        if (response.IsSuccessStatusCode) return body;
        string error = body.RootElement.TryGetProperty("error", out var value) ? value.GetString() ?? "oauth_error" : "oauth_error";
        string detail = body.RootElement.TryGetProperty("error_description", out var description)
            ? description.GetString() ?? "" : "";
        body.Dispose();
        throw new IOException("Google OAuth: " + SafeErrorCode(error) +
            (detail.Length > 0 ? " — " + SafeErrorDetail(detail) : ""));
    }

    private static string SafeErrorCode(string code) => new(code.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(80).ToArray());

    private static string SafeErrorDetail(string detail) => new(detail.Where(c =>
        char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or ',' or ':' or '-' or '_' or '(' or ')').Take(160).ToArray());

    private static string LoadClientSecret(string clientId)
    {
        foreach (string name in new[] { "oauth-providers.local.json", "oauth-providers.json" })
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, name);
                if (!File.Exists(path)) continue;
                using var config = JsonDocument.Parse(File.ReadAllText(path));
                string? configuredId = config.RootElement.TryGetProperty("googleClientId", out var id)
                    ? id.GetString() : null;
                string? secret = config.RootElement.TryGetProperty("googleClientSecret", out var value)
                    ? value.GetString() : null;
                if (configuredId == clientId && !string.IsNullOrWhiteSpace(secret)) return secret;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { /* Try the next configuration file. */ }
        }
        throw new IOException("Google OAuth desktop client credentials are incomplete. The client secret is missing.");
    }
}
