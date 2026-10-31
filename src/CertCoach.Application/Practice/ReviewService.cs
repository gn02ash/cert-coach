using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Practice;

/// <summary>
/// The spaced-repetition loop: today's queue of due and new items, quizzes on a study session's objectives, and
/// answers, which reschedule the item and are logged for readiness.
/// </summary>
public sealed class ReviewService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    /// <summary>New items introduced per day, so a big generation run doesn't bury the learner in reviews later.</summary>
    public const int NewPerDay = 20;

    public const int MaxCards = 100;
    public const int QuizSize = 10;

    /// <param name="certificationId">Limits the queue to one certification; all of them when null.</param>
    public async Task<ReviewSet> GetQueueAsync(Guid? certificationId, CancellationToken ct)
    {
        var today = clock.Today;
        var active = ActiveItems(certificationId);
        var due = await active.Where(i => i.DueOn != null && i.DueOn <= today).OrderBy(i => i.DueOn).ThenBy(i => i.Id).ToListAsync(ct);
        var newAvailable = await active.CountAsync(i => i.DueOn == null, ct);
        var reviewedToday = await db.ReviewLogs.Where(r => r.OwnerId == user.UserId && r.ReviewedOn == today).ToListAsync(ct);
        var newLeft = Math.Max(0, NewPerDay - reviewedToday.Count(r => r.WasNew));

        var fresh = (await active.Where(i => i.DueOn == null).OrderBy(i => i.CreatedAt).ToListAsync(ct))
            // Shuffled the same way all day, so domains are mixed and a reload keeps the order.
            .OrderBy(i => StableHash(i.Id, today))
            .Take(newLeft);

        var cards = due.Concat(fresh).Take(MaxCards).ToList();
        return new ReviewSet(today, due.Count, newAvailable, newLeft, reviewedToday.Count, await ToCardsAsync(cards, ct));
    }

    /// <summary>What today's queue holds across all certifications: due items plus the new ones today's limit allows.</summary>
    public async Task<int> CountForTodayAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var active = ActiveItems(null);
        var due = await active.CountAsync(i => i.DueOn != null && i.DueOn <= today, ct);
        var newAvailable = await active.CountAsync(i => i.DueOn == null, ct);
        var newToday = await db.ReviewLogs.CountAsync(r => r.OwnerId == user.UserId && r.ReviewedOn == today && r.WasNew, ct);
        return due + Math.Min(newAvailable, Math.Max(0, NewPerDay - newToday));
    }

    /// <summary>A short quiz on some objectives, such as today's session: items due or new first, then the rest.</summary>
    public async Task<ReviewSet> GetQuizAsync(IReadOnlyList<Guid> objectiveIds, CancellationToken ct)
    {
        if (objectiveIds.Count == 0)
        {
            throw new DomainException("Choose the objectives to be quizzed on.");
        }

        var today = clock.Today;
        var items = await ActiveItems(null)
            .Where(i => i.ObjectiveId != null && objectiveIds.Contains(i.ObjectiveId.Value))
            .ToListAsync(ct);
        var cards = items
            .OrderBy(i => i.DueOn is null || i.DueOn <= today ? 0 : 1)
            .ThenBy(i => StableHash(i.Id, today))
            .Take(QuizSize)
            .ToList();
        var reviewedToday = await db.ReviewLogs.CountAsync(r => r.OwnerId == user.UserId && r.ReviewedOn == today, ct);
        return new ReviewSet(
            today, items.Count(i => i.DueOn <= today), items.Count(i => i.DueOn == null), QuizSize, reviewedToday, await ToCardsAsync(cards, ct));
    }

    public async Task<AnswerResult> AnswerAsync(Guid itemId, AnswerRequest request, CancellationToken ct)
    {
        var item = await db.PracticeItems.SingleOrDefaultAsync(i => i.Id == itemId && i.OwnerId == user.UserId, ct)
            ?? throw new NotFoundException("Practice item");

        var log = item.Kind == PracticeItemKind.Flashcard
            ? item.Grade(request.Grade ?? throw new DomainException("Grade the card: Again, Hard, Good or Easy."), request.Source, clock.Today, clock.Now)
            : item.Submit(request.SelectedOptions ?? [], request.Source, clock.Today, clock.Now);
        db.ReviewLogs.Add(log);
        await db.SaveChangesAsync(ct);

        var titles = await DocumentTitlesAsync([item], ct);
        return new AnswerResult(
            log.Correct,
            item.CorrectOptions,
            item.Answer,
            item.Sources.Select(s => new ItemSourceView(s.DocumentId, titles.GetValueOrDefault(s.DocumentId), s.Page, s.Quote)).ToList(),
            item.IntervalDays,
            item.DueOn!.Value);
    }

    private IQueryable<PracticeItem> ActiveItems(Guid? certificationId) =>
        db.PracticeItems.Where(i =>
            i.OwnerId == user.UserId
            && i.Status == PracticeItemStatus.Active
            && (certificationId == null || i.CertificationId == certificationId));

    private async Task<List<ReviewCard>> ToCardsAsync(IReadOnlyList<PracticeItem> items, CancellationToken ct)
    {
        var certificationIds = items.Select(i => i.CertificationId).Distinct().ToList();
        var certifications = await db.Certifications
            .Include(c => c.Domains).ThenInclude(d => d.SkillAreas).ThenInclude(s => s.Objectives)
            .Where(c => c.OwnerId == user.UserId && certificationIds.Contains(c.Id))
            .ToListAsync(ct);
        var examCodes = certifications.ToDictionary(c => c.Id, c => c.ExamCode);
        var objectives = certifications
            .SelectMany(c => c.Domains.SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives.Select(o => (o.Id, o.Text, Domain: d.Name)))))
            .ToDictionary(o => o.Id);

        return items.Select(i =>
        {
            var objective = i.ObjectiveId is { } id && objectives.TryGetValue(id, out var o) ? o : default;
            var flashcard = i.Kind == PracticeItemKind.Flashcard;
            return new ReviewCard(
                i.Id,
                i.CertificationId,
                examCodes.GetValueOrDefault(i.CertificationId, ""),
                i.ObjectiveId,
                objective.Text,
                objective.Domain,
                i.Kind,
                i.Origin,
                i.Prompt,
                i.Options,
                flashcard ? 0 : i.CorrectOptions.Count,
                flashcard ? i.Answer : null,
                i.IsNew,
                flashcard
                    ? new ReviewIntervals(
                        i.IntervalAfter(ReviewGrade.Again), i.IntervalAfter(ReviewGrade.Hard), i.IntervalAfter(ReviewGrade.Good), i.IntervalAfter(ReviewGrade.Easy))
                    : null);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> DocumentTitlesAsync(IReadOnlyList<PracticeItem> items, CancellationToken ct)
    {
        var ids = items.SelectMany(i => i.Sources).Select(s => s.DocumentId).Distinct().ToList();
        return ids.Count == 0
            ? []
            : (await db.Documents.Where(d => ids.Contains(d.Id)).ToListAsync(ct)).ToDictionary(d => d.Id, d => d.Title);
    }

    private static uint StableHash(Guid id, DateOnly day)
    {
        var hash = 2166136261u ^ (uint)day.DayNumber;
        foreach (var b in id.ToByteArray())
        {
            hash = (hash ^ b) * 16777619;
        }

        return hash;
    }
}
