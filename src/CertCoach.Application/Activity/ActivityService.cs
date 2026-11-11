using CertCoach.Application.Abstractions;
using CertCoach.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Activity;

/// <param name="Reviews">Practice items answered that day, in reviews, quizzes and mock exams.</param>
/// <param name="Sessions">Study sessions completed that day.</param>
public sealed record ActivityDay(DateOnly Date, int Reviews, int Sessions);

/// <param name="Days">Every day of the period, oldest first, starting on a Monday and ending today.</param>
public sealed record ActivityView(DateOnly Today, Streak Streak, int ReviewsThisWeek, int SessionsThisWeek, IReadOnlyList<ActivityDay> Days);

/// <summary>What the learner did each day, in their own time zone, for the streak and the activity calendar.</summary>
public sealed class ActivityService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    public const int DefaultWeeks = 12;
    public const int MaxWeeks = 52;

    public async Task<ActivityView> GetAsync(int weeks, CancellationToken ct)
    {
        weeks = Math.Clamp(weeks, 1, MaxWeeks);
        var today = clock.Today;

        var reviews = await db.ReviewLogs
            .Where(r => r.OwnerId == user.UserId)
            .GroupBy(r => r.ReviewedOn)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count, ct);

        var completed = await db.StudyPlans
            .Where(p => p.OwnerId == user.UserId)
            .SelectMany(p => p.Sessions)
            .Where(s => s.Status == SessionStatus.Completed && s.CompletedAt != null)
            .Select(s => s.CompletedAt!.Value)
            .ToListAsync(ct);
        var sessions = completed
            .CountBy(at => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, user.TimeZone).DateTime))
            .ToDictionary(p => p.Key, p => p.Value);

        // Whole weeks, Monday first, so the calendar's columns line up.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var start = monday.AddDays(-7 * (weeks - 1));
        var days = Enumerable.Range(0, today.DayNumber - start.DayNumber + 1)
            .Select(i => start.AddDays(i))
            .Select(d => new ActivityDay(d, reviews.GetValueOrDefault(d), sessions.GetValueOrDefault(d)))
            .ToList();

        var studyDays = reviews.Keys.Concat(sessions.Keys).ToHashSet();
        var thisWeek = days.Where(d => d.Date >= monday).ToList();
        return new ActivityView(
            today, StudyStreak.Compute(studyDays, today), thisWeek.Sum(d => d.Reviews), thisWeek.Sum(d => d.Sessions), days);
    }
}
