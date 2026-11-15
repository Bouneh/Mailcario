using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Mailcario;

internal sealed record SaveableMailImage(byte[] Bytes, string FileName, string Extension, string ContentType);

internal static class ImageSaveSupport
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/gif"] = ".gif",
        ["image/bmp"] = ".bmp", ["image/tiff"] = ".tif", ["image/x-icon"] = ".ico",
        ["image/vnd.microsoft.icon"] = ".ico"
    };

    internal static bool TryGetLoadedImage(string? sourceUri, IReadOnlyDictionary<string, string> loaded,
        out SaveableMailImage? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(sourceUri) || sourceUri.Length > 6_000_000) return false;
        foreach (var image in loaded)
        {
            if (!string.Equals(image.Value, sourceUri, StringComparison.Ordinal)) continue;
            int separator = sourceUri.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
            if (!sourceUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || separator < 6) return false;
            string contentType = sourceUri[5..separator].ToLowerInvariant();
            if (!Extensions.TryGetValue(contentType, out string? extension)) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(sourceUri[(separator + 8)..]);
                if (bytes.Length is < 1 or > 4_000_000) return false;
                string name = "image";
                if (Uri.TryCreate(image.Key, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
                    name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
                name = new string(name.Where(character => !Path.GetInvalidFileNameChars().Contains(character)).ToArray());
                if (name.Length is < 1 or > 100) name = "image";
                result = new SaveableMailImage(bytes, Path.ChangeExtension(name, extension), extension, contentType);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or UriFormatException or ArgumentException)
            { return false; }
        }
        return false;
    }

    internal static void Save(Window owner, SaveableMailImage image)
    {
        var dialog = new SaveFileDialog
        {
            Title = Localizer.T("saveImageAs"), FileName = image.FileName,
            DefaultExt = image.Extension, Filter = $"{image.ContentType}|*{image.Extension}|All files|*.*",
            AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != true) return;
        File.WriteAllBytes(dialog.FileName, image.Bytes);
    }

    internal static void SaveBitmap(Window owner, BitmapSource bitmap)
    {
        var dialog = new SaveFileDialog
        {
            Title = Localizer.T("saveImageAs"), FileName = "image.png", DefaultExt = ".png",
            Filter = "PNG|*.png|All files|*.*", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != true) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(dialog.FileName);
        encoder.Save(output);
    }
}
