using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Mailcario;

public sealed record UnsubscribeAction(Uri Url, bool OneClick);

public static class UnsubscribeSupport
{
    public static UnsubscribeAction? Parse(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("list-unsubscribe", out string? value) || value.Length > 8192)
            return null;
        var candidates = new List<Uri>();
        foreach (Match match in Regex.Matches(value, @"<([^<>]+)>", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            string target = match.Groups[1].Value.Trim();
            if (target.Length > 2048 || !Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)) continue;
            if (uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.HostNameType == UriHostNameType.Dns &&
                !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) candidates.Add(uri);
            else if (uri.Scheme == "mailto" && target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) &&
                target.Length > 7 && target.IndexOfAny(['\r', '\n']) < 0) candidates.Add(uri);
        }
        Uri? choice = candidates.FirstOrDefault(uri => uri.Scheme == "https") ?? candidates.FirstOrDefault();
        if (choice is null) return null;
        bool oneClick = choice.Scheme == "https" &&
            headers.TryGetValue("list-unsubscribe-post", out string? post) &&
            post.Contains("List-Unsubscribe=One-Click", StringComparison.OrdinalIgnoreCase);
        return new UnsubscribeAction(choice, oneClick);
    }

    public static async Task PostOneClickAsync(Uri uri, CancellationToken token = default)
    {
        if (uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.HostNameType != UriHostNameType.Dns)
            throw new IOException("Unsupported unsubscribe URL.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = RemoteImageLoader.ConnectPublicAsync
        });
        using var content = new StringContent("List-Unsubscribe=One-Click", Encoding.ASCII,
            "application/x-www-form-urlencoded");
        using var response = await client.PostAsync(uri, content, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new IOException("The server rejected the unsubscribe request.");
    }
}
