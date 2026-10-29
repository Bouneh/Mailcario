using System.Text;
using System.Text.RegularExpressions;

namespace Mailcario;

public static class ImapFolderName
{
    private static readonly Regex Shifted = new(@"&(?<data>[A-Za-z0-9+,]*)-", RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // IMAP4rev1 mailbox names use modified UTF-7 (RFC 3501, section 5.1.3).
    public static string Decode(string name) => Shifted.Replace(name, match =>
    {
        string data = match.Groups["data"].Value;
        if (data.Length == 0) return "&";
        try
        {
            string base64 = data.Replace(',', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            byte[] bytes = Convert.FromBase64String(base64);
            return bytes.Length % 2 == 0 ? Encoding.BigEndianUnicode.GetString(bytes) : match.Value;
        }
        catch (FormatException) { return match.Value; }
    });

    public static string Encode(string name)
    {
        var output = new StringBuilder();
        var shifted = new StringBuilder();
        void Flush()
        {
            if (shifted.Length == 0) return;
            output.Append('&').Append(Convert.ToBase64String(Encoding.BigEndianUnicode.GetBytes(shifted.ToString()))
                .TrimEnd('=').Replace('/', ',')).Append('-');
            shifted.Clear();
        }
        foreach (char c in name)
        {
            if (c is >= (char)0x20 and <= (char)0x7e)
            {
                Flush();
                output.Append(c == '&' ? "&-" : c);
            }
            else shifted.Append(c);
        }
        Flush();
        return output.ToString();
    }
}
