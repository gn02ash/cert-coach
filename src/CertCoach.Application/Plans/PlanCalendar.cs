using System.Globalization;
using System.Text;
using CertCoach.Domain.Planning;

namespace CertCoach.Application.Plans;

/// <summary>
/// Writes a plan's remaining sessions and its exam day as an iCalendar file (RFC 5545) that Outlook, Google
/// Calendar and Apple Calendar import. Sessions are all-day events unless a start time is given, in which case
/// they are timed in "floating" local time: the calendar shows them at that time wherever the learner is.
/// </summary>
public static class PlanCalendar
{
    public const string ContentType = "text/calendar; charset=utf-8";

    public static string Build(PlanView plan, DateOnly today, DateTimeOffset now, TimeOnly? startTime)
    {
        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var ics = new StringBuilder();
        Line(ics, "BEGIN:VCALENDAR");
        Line(ics, "VERSION:2.0");
        Line(ics, "PRODID:-//Cert Coach//Study plan//EN");
        Line(ics, "CALSCALE:GREGORIAN");
        Line(ics, "METHOD:PUBLISH");
        Line(ics, $"X-WR-CALNAME:{Escape($"{plan.ExamCode} study plan")}");

        // Several sessions on one day follow each other from the start time.
        foreach (var day in plan.Sessions.Where(s => s.Status == SessionStatus.Planned && s.Date >= today && s.Id is not null).GroupBy(s => s.Date))
        {
            var at = startTime;
            foreach (var session in day.OrderBy(s => s.Sequence))
            {
                Line(ics, "BEGIN:VEVENT");
                Line(ics, $"UID:{session.Id}@certcoach");
                Line(ics, $"DTSTAMP:{stamp}");
                if (at is { } time)
                {
                    Line(ics, $"DTSTART:{Local(session.Date, time)}");
                    Line(ics, string.Create(CultureInfo.InvariantCulture, $"DURATION:PT{session.DurationMinutes}M"));
                    at = time.AddMinutes(session.DurationMinutes);
                }
                else
                {
                    Line(ics, $"DTSTART;VALUE=DATE:{Date(session.Date)}");
                    Line(ics, $"DTEND;VALUE=DATE:{Date(session.Date.AddDays(1))}");
                    Line(ics, "TRANSP:TRANSPARENT");
                }

                Line(ics, $"SUMMARY:{Escape($"{plan.ExamCode}: {session.Title}")}");
                Line(ics, $"DESCRIPTION:{Escape(Describe(session))}");
                Line(ics, "END:VEVENT");
            }
        }

        if (plan.ExamDate >= today)
        {
            Line(ics, "BEGIN:VEVENT");
            Line(ics, $"UID:{plan.Id}-exam@certcoach");
            Line(ics, $"DTSTAMP:{stamp}");
            Line(ics, $"DTSTART;VALUE=DATE:{Date(plan.ExamDate)}");
            Line(ics, $"DTEND;VALUE=DATE:{Date(plan.ExamDate.AddDays(1))}");
            Line(ics, $"SUMMARY:{Escape($"{plan.ExamCode} exam: {plan.CertificationName}")}");
            Line(ics, "END:VEVENT");
        }

        Line(ics, "END:VCALENDAR");
        return ics.ToString();
    }

    private static string Describe(SessionView session)
    {
        var kind = session.Kind switch
        {
            SessionKind.Learn => "Learn",
            SessionKind.Review => "Review",
            _ => "Mock exam",
        };
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{kind}, {session.DurationMinutes} minutes"));
        if (session.DomainName is { Length: > 0 } domain)
        {
            text.Append(" · ").Append(domain);
        }

        foreach (var objective in session.Objectives)
        {
            text.Append("\n- ").Append(objective.Text);
        }

        return text.ToString();
    }

    private static string Date(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Local(DateOnly date, TimeOnly time) =>
        date.ToDateTime(time).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    /// <summary>Text values escape backslashes, semicolons, commas and line breaks.</summary>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Lines end in CRLF and are folded at 75 bytes, continuing on a line that starts with a space.</summary>
    internal static void Line(StringBuilder ics, string line)
    {
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 75)
            {
                ics.Append("\r\n ");
                bytes = 1;
            }

            ics.Append(rune.ToString());
            bytes += size;
        }

        ics.Append("\r\n");
    }
}
