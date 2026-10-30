using System.Globalization;
using System.Text.RegularExpressions;

namespace Mailcario;

public static class MailDateParser
{
    public static DateTimeOffset? Parse(string? headerDate, string? internalDate, bool sentFolder = false)
    {
        DateTimeOffset? received = null;
        if (!string.IsNullOrWhiteSpace(internalDate))
        {
            string value = Regex.Replace(internalDate.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
            if (DateTimeOffset.TryParseExact(value, "d-MMM-yyyy HH:mm:ss zzz",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) received = parsed;
        }

        DateTimeOffset? sent = null;
        if (!string.IsNullOrWhiteSpace(headerDate))
        {
            // RFC 5322 comments such as "(UTC)" are common in Date headers but
            // DateTimeOffset.TryParse does not accept them after the numeric offset.
            string value = headerDate;
            while (Regex.IsMatch(value, @"\([^()]*\)"))
                value = Regex.Replace(value, @"\([^()]*\)", " ");
            value = Regex.Replace(value, @"^\s*(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s*,\s*", "",
                RegexOptions.IgnoreCase);
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var parsed)) sent = parsed;
        }
        // Received folders follow the IMAP delivery time, while Sent follows
        // the sender's Date header. Either date serves as a fallback.
        return sentFolder ? sent ?? received : received ?? sent;
    }
}
