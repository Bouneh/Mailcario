using System.Globalization;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace Mailcario;

public static class CalendarSupport
{
    public static CalendarInvite? Parse(string? calendar)
    {
        if (string.IsNullOrWhiteSpace(calendar)) return null;
        string unfolded = calendar.Replace("\r\n ", "").Replace("\r\n\t", "")
            .Replace("\n ", "").Replace("\n\t", "");
        var lines = unfolded.Split('\n').Select(x => x.TrimEnd('\r')).ToArray();
        if (!lines.Any(x => x.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))) return null;
        string Get(string key)
        {
            string? line = lines.FirstOrDefault(x => x.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) ||
                                                     x.StartsWith(key + ";", StringComparison.OrdinalIgnoreCase));
            return line is null ? "" : line[(line.IndexOf(':') + 1)..];
        }
        return new CalendarInvite
        {
            Uid = Get("UID"),
            Summary = Unescape(Get("SUMMARY")),
            Location = Unescape(Get("LOCATION")),
            Organizer = Get("ORGANIZER").Replace("mailto:", "", StringComparison.OrdinalIgnoreCase),
            Start = ParseDate(Get("DTSTART")),
            End = ParseDate(Get("DTEND")),
            RawCalendar = calendar
        };
    }

    private static string Unescape(string value) => value.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    private static DateTimeOffset? ParseDate(string value)
    {
        string[] formats = ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmm'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd"];
        foreach (string format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return value.EndsWith('Z')
                    ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
                    : new DateTimeOffset(date);
            }
        }
        return null;
    }

    public static MailMessage CreateReply(MailAccount account, CalendarInvite invite, string response)
    {
        string recipient = MimeReader.ExtractAddress(invite.Organizer);
        if (recipient.Length == 0) throw new InvalidOperationException("The organizer address is missing.");
        string partStat = response switch
        {
            "ACCEPTED" => "ACCEPTED",
            "TENTATIVE" => "TENTATIVE",
            "DECLINED" => "DECLINED",
            _ => throw new ArgumentOutOfRangeException(nameof(response))
        };
        string text = $"{account.DisplayName} replied {partStat.ToLowerInvariant()} to {invite.Summary}.";
        var mail = new MailMessage(new MailAddress(account.Email, account.DisplayName), new MailAddress(recipient))
        {
            Subject = "Re: " + invite.Summary,
            Body = text,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        string ics = string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Mailcario//Mailcario 1.0//EN", "METHOD:REPLY",
            "BEGIN:VEVENT", "UID:" + Escape(invite.Uid),
            "DTSTAMP:" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            "SUMMARY:" + Escape(invite.Summary),
            "ORGANIZER:mailto:" + recipient,
            $"ATTENDEE;PARTSTAT={partStat};RSVP=FALSE:mailto:{account.Email}",
            "END:VEVENT", "END:VCALENDAR", ""
        });
        var view = AlternateView.CreateAlternateViewFromString(ics, Encoding.UTF8, "text/calendar");
        view.ContentType.Parameters.Add("method", "REPLY");
        mail.AlternateViews.Add(view);
        return mail;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
