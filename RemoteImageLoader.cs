using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows.Media.Imaging;

namespace Mailcario;

public sealed record RemoteMailImage(BitmapSource Bitmap, string DataUri);

/// <summary>
/// Fetches an image only after the reader explicitly asks for it. Requests carry
/// no cookies or referrer, never follow redirects, and cannot target local IPs.
/// </summary>
public static class RemoteImageLoader
{
    private const int MaxImageBytes = 4_000_000;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/x-icon", "image/vnd.microsoft.icon"
    };
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = ConnectPublicAsync
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<BitmapSource?> LoadAsync(Uri uri, CancellationToken token) =>
        (await LoadFullAsync(uri, token))?.Bitmap;

    public static async Task<RemoteMailImage?> LoadFullAsync(Uri uri, CancellationToken token)
    {
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new IOException("Unsupported image URL.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("image/png, image/jpeg, image/gif, image/bmp, image/tiff, image/x-icon");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentType?.MediaType is not string contentType ||
            !AllowedTypes.Contains(contentType) ||
            response.Content.Headers.ContentLength is > MaxImageBytes)
            throw new IOException("The image was rejected by Mailcario.");

        await using var network = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[64_000];
        while (true)
        {
            int read = await network.ReadAsync(buffer, timeout.Token);
            if (read == 0) break;
            if (bytes.Length + read > MaxImageBytes) throw new IOException("The image is too large.");
            bytes.Write(buffer, 0, read);
        }
        return DecodeVerifiedImage(bytes.ToArray(), contentType);
    }

    public static RemoteMailImage? LoadEmbedded(InlineMailImage embedded)
    {
        if (!AllowedTypes.Contains(embedded.ContentType) || embedded.Bytes.Length is < 1 or > MaxImageBytes)
            return null;
        return DecodeVerifiedImage(embedded.Bytes, embedded.ContentType);
    }

    private static RemoteMailImage? DecodeVerifiedImage(byte[] data, string contentType)
    {
        using var bytes = new MemoryStream(data, writable: false);
        var decoder = BitmapDecoder.Create(bytes, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        BitmapFrame frame = decoder.Frames[0];
        long width = frame.PixelWidth;
        long height = frame.PixelHeight;
        if (width <= 2 || height <= 2 || width > 8000 || height > 8000 || width * height > 20_000_000)
            return null;
        bytes.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 1600) image.DecodePixelWidth = 1600;
        image.StreamSource = bytes;
        image.EndInit();
        image.Freeze();
        return new RemoteMailImage(image, "data:" + contentType + ";base64," + Convert.ToBase64String(data));
    }

    internal static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        string host = context.DnsEndPoint.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Local image addresses are blocked.");
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, token);
        bool foundPublicAddress = false;
        foreach (IPAddress address in addresses.Where(IsPublicAddress))
        {
            foundPublicAddress = true;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) when (!token.IsCancellationRequested) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new IOException(foundPublicAddress ? "The image host is unreachable." : "The image address is not public.");
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte first = address.GetAddressBytes()[0];
            return first is >= 0x20 and <= 0x3f; // Global unicast only.
        }
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] is > 0 and < 224 && b[0] != 10 && b[0] != 127 &&
            !(b[0] == 169 && b[1] == 254) &&
            !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
            !(b[0] == 192 && b[1] == 168) &&
            !(b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}
