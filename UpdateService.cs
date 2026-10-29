using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mailcario;

internal sealed record AvailableUpdate(Version Version, Uri DownloadUrl, string Sha256);

internal static class UpdateService
{
    private const long MaximumInstallerBytes = 500L * 1024 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(30) };

    static UpdateService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("Mailcario-Desktop/0.1");

    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 1, 0);

    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "update-source.json");
        if (!File.Exists(configPath)) return null;
        using JsonDocument config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath, cancellationToken));
        string? repository = config.RootElement.TryGetProperty("githubRepository", out JsonElement repo)
            ? repo.GetString()?.Trim() : null;
        if (repository is null || !Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) return null;

        Uri api = new($"https://api.github.com/repos/{repository}/releases/latest");
        using CancellationTokenSource checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkTimeout.CancelAfter(TimeSpan.FromSeconds(12));
        using HttpResponseMessage response = await Client.GetAsync(api, checkTimeout.Token);
        if (response.StatusCode is HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using JsonDocument release = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(checkTimeout.Token));
        JsonElement root = release.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? version)
            || version.Build < 0 || version <= CurrentVersion) return null;

        string expectedName = $"Mailcario-Setup-{version.ToString(3)}.exe";
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), expectedName, StringComparison.OrdinalIgnoreCase)) continue;
            string? digest = asset.TryGetProperty("digest", out JsonElement digestElement) ? digestElement.GetString() : null;
            string? url = asset.GetProperty("browser_download_url").GetString();
            if (digest is null || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? downloadUrl)
                || downloadUrl.Scheme != Uri.UriSchemeHttps || downloadUrl.Host != "github.com") continue;
            return new AvailableUpdate(version, downloadUrl, digest[7..].ToUpperInvariant());
        }
        return null;
    }

    public static async Task<string> DownloadAsync(AvailableUpdate update, IProgress<double> progress, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mailcario", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string installerPath = Path.Combine(directory, $"Mailcario-Setup-{update.Version.ToString(3)}.exe");
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            if (length > MaximumInstallerBytes) throw new InvalidDataException("The installer is too large.");
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destination = new(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            long received = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                received += count;
                if (received > MaximumInstallerBytes) throw new InvalidDataException("The installer is too large.");
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                hash.AppendData(buffer, 0, count);
                if (length is > 0) progress.Report(Math.Clamp(100.0 * received / length.Value, 0, 100));
            }
            await destination.FlushAsync(cancellationToken);
            string actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update checksum does not match the published release.");
            progress.Report(100);
            return installerPath;
        }
        catch
        {
            try { File.Delete(installerPath); } catch { }
            throw;
        }
    }

    public static void ApplyAndRestart(string installerPath, string expectedSha256)
    {
        string helperSource = Path.Combine(AppContext.BaseDirectory, "Mailcario.Updater.exe");
        if (!File.Exists(helperSource)) throw new FileNotFoundException("The update helper is missing.", helperSource);
        string helperPath = Path.Combine(Path.GetDirectoryName(installerPath)!, "Mailcario.Updater.exe");
        File.Copy(helperSource, helperPath, true);
        string appPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Mailcario.exe");
        _ = Process.Start(new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!,
            ArgumentList = { "--wait-pid", Environment.ProcessId.ToString(), "--installer", installerPath,
                "--sha256", expectedSha256, "--app", appPath }
        }) ?? throw new IOException("Unable to launch the update helper.");
    }
}
