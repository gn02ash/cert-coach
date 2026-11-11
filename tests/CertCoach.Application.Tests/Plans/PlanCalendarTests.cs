using System.Text;
using CertCoach.Application.Plans;
using CertCoach.Domain.Planning;

namespace CertCoach.Application.Tests.Plans;

public class PlanCalendarTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);

    private static SessionView Session(int sequence, DateOnly date, SessionStatus status = SessionStatus.Planned, string title = "Combine vector, keyword search; cite") =>
        new(Guid.Parse($"00000000-0000-0000-0000-{sequence:D12}"), sequence, date, 40, SessionKind.Learn, status, title, Guid.NewGuid(), "Retrieval",
            [new SessionObjectiveView(Guid.NewGuid(), "Combine vector and keyword search", "Search", Guid.NewGuid(), "Retrieval")], null);

    private static PlanView Plan(params SessionView[] sessions) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.NewGuid(), "DEMO-101", "Contoso AI Solutions Engineer", Today.AddDays(-7), Today.AddDays(30), 5m,
        [DayOfWeek.Monday], 10, PlanStatus.Active, null, sessions, []);

    [Fact]
    public void Writes_remaining_sessions_as_all_day_events_and_the_exam_day()
    {
        var ics = PlanCalendar.Build(
            Plan(Session(1, Today.AddDays(-1)), Session(2, Today, SessionStatus.Completed), Session(3, Today), Session(4, Today.AddDays(2))), Today, Now, null);

        ics.ShouldStartWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
        ics.ShouldEndWith("END:VCALENDAR\r\n");
        ics.Split("BEGIN:VEVENT").Length.ShouldBe(1 + 3); // Two sessions still to do, and the exam.
        ics.ShouldContain("UID:00000000-0000-0000-0000-000000000003@certcoach\r\nDTSTAMP:20261111T093000Z\r\nDTSTART;VALUE=DATE:20261111\r\nDTEND;VALUE=DATE:20261112\r\n");
        ics.ShouldContain("SUMMARY:DEMO-101: Combine vector\\, keyword search\\; cite\r\n");
        ics.ShouldContain("UID:11111111-1111-1111-1111-111111111111-exam@certcoach");
        ics.ShouldContain("DTSTART;VALUE=DATE:20261211");
        ics.ShouldNotContain("000000000001@");
        ics.ShouldNotContain("000000000002@");
    }

    [Fact]
    public void A_start_time_makes_timed_events_that_follow_each_other_on_one_day()
    {
        var ics = PlanCalendar.Build(Plan(Session(1, Today), Session(2, Today)), Today, Now, new TimeOnly(18, 30));

        ics.ShouldContain("DTSTART:20261111T183000\r\nDURATION:PT40M\r\n");
        ics.ShouldContain("DTSTART:20261111T191000\r\nDURATION:PT40M\r\n");
        ics.ShouldNotContain("TRANSP");
    }

    [Fact]
    public void Folds_long_lines_at_75_bytes_without_splitting_characters()
    {
        var ics = new StringBuilder();

        PlanCalendar.Line(ics, "SUMMARY:" + new string('é', 60));

        var lines = ics.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        lines.ShouldAllBe(l => Encoding.UTF8.GetByteCount(l) <= 75);
        lines[1].ShouldStartWith(" ");
        string.Concat(lines[0], lines[1][1..]).ShouldBe("SUMMARY:" + new string('é', 60));
    }

    [Fact]
    public void Escapes_line_breaks_in_descriptions()
    {
        PlanCalendar.Escape("a\nb\\c").ShouldBe("a\\nb\\\\c");
    }
}
