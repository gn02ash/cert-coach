using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Common;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Practice.Exams;

/// <summary>
/// Timed mock exams. An exam past its deadline is submitted the next time it is read, so closing the tab doesn't
/// leave it open. Submitting grades every question, which reschedules it like any review and feeds readiness.
/// </summary>
public sealed class MockExamService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    public const int RecentExams = 10;

    public async Task<MockExamView> StartAsync(StartMockExamRequest request, CancellationToken ct)
    {
        var (certificationId, sessionId) = await ResolveAsync(request, ct);
        var certification = await LoadCertificationAsync(certificationId, ct);

        // One exam at a time per certification: a second start resumes the first.
        var running = await db.MockExams
            .Where(e => e.OwnerId == user.UserId && e.CertificationId == certificationId && e.Status == MockExamStatus.InProgress)
            .ToListAsync(ct);
        if (running.Find(e => !e.IsOver(clock.Now)) is { } current)
        {
            return await ToViewAsync(current, certification, ct);
        }

        foreach (var expired in running)
        {
            await GradeAsync(expired, certification, ct);
        }

        var objectiveDomains = ObjectiveDomains(certification);
        var questions = await db.PracticeItems
            .Where(i => i.OwnerId == user.UserId
                && i.CertificationId == certificationId
                && i.Status == PracticeItemStatus.Active
                && i.Kind != PracticeItemKind.Flashcard
                && i.ObjectiveId != null)
            .Select(i => new { i.Id, ObjectiveId = i.ObjectiveId!.Value })
            .ToListAsync(ct);

        var pools = certification.Domains
            .Select(d => new DomainPool(d.Id, d.Weight, questions.Where(q => objectiveDomains.GetValueOrDefault(q.ObjectiveId) == d.Id).Select(q => q.Id).ToList()))
            .ToList();
        var count = Math.Clamp(request.QuestionCount, MockExam.MinQuestions, MockExam.MaxQuestions);
        var picked = ExamBlueprint.Pick(pools, count, seed: (int)(clock.Now.ToUnixTimeSeconds() & int.MaxValue));

        var exam = MockExam.Start(user.UserId, certificationId, sessionId, picked, request.DurationMinutes, clock.Now);
        db.MockExams.Add(exam);
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(exam, certification, ct);
    }

    public async Task<MockExamView> GetAsync(Guid id, CancellationToken ct)
    {
        var exam = await LoadAsync(id, ct);
        var certification = await LoadCertificationAsync(exam.CertificationId, ct);
        if (exam.Status == MockExamStatus.InProgress && exam.IsOver(clock.Now))
        {
            await GradeAsync(exam, certification, ct);
        }

        return await ToViewAsync(exam, certification, ct);
    }

    public async Task<IReadOnlyList<MockExamSummary>> ListAsync(Guid certificationId, CancellationToken ct) =>
        (await db.MockExams
            .Where(e => e.OwnerId == user.UserId && e.CertificationId == certificationId)
            .OrderByDescending(e => e.StartedAt)
            .Take(RecentExams)
            .ToListAsync(ct))
        .Select(e => new MockExamSummary(e.Id, e.Status, e.StartedAt, e.Deadline, e.ItemIds.Count, Percent(e.CorrectCount, e.ItemIds.Count)))
        .ToList();

    public async Task SaveAnswerAsync(Guid id, Guid itemId, SaveMockAnswerRequest request, CancellationToken ct)
    {
        var exam = await LoadAsync(id, ct);
        exam.SaveAnswer(itemId, request.Selected ?? [], clock.Now);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MockExamView> SubmitAsync(Guid id, CancellationToken ct)
    {
        var exam = await LoadAsync(id, ct);
        var certification = await LoadCertificationAsync(exam.CertificationId, ct);
        if (exam.Status == MockExamStatus.InProgress)
        {
            await GradeAsync(exam, certification, ct);
        }

        return await ToViewAsync(exam, certification, ct);
    }

    /// <summary>Grades every question still in the library, logs the answers, and completes the plan session.</summary>
    private async Task GradeAsync(MockExam exam, Certification certification, CancellationToken ct)
    {
        var now = clock.Now;
        var items = await db.PracticeItems.Where(i => exam.ItemIds.Contains(i.Id)).ToListAsync(ct);
        var correct = 0;
        foreach (var item in items)
        {
            var log = item.Submit(exam.SelectedFor(item.Id), ReviewSource.MockExam, clock.Today, now);
            db.ReviewLogs.Add(log);
            correct += log.Correct == true ? 1 : 0;
        }

        exam.Submit(correct, now);

        if (exam.SessionId is { } sessionId
            && await db.StudyPlans.Include(p => p.Sessions).SingleOrDefaultAsync(p => p.OwnerId == user.UserId && p.Sessions.Any(s => s.Id == sessionId), ct) is { } plan
            && plan.Status == PlanStatus.Active
            && plan.Sessions.Single(s => s.Id == sessionId).Status == SessionStatus.Planned)
        {
            plan.CompleteSession(sessionId, now);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<(Guid CertificationId, Guid? SessionId)> ResolveAsync(StartMockExamRequest request, CancellationToken ct)
    {
        if (request.SessionId is not { } sessionId)
        {
            return (request.CertificationId ?? throw new DomainException("Choose a certification for the exam."), null);
        }

        var plan = await db.StudyPlans.Include(p => p.Sessions)
            .SingleOrDefaultAsync(p => p.OwnerId == user.UserId && p.Sessions.Any(s => s.Id == sessionId), ct)
            ?? throw new NotFoundException("Study session");
        return plan.Sessions.Single(s => s.Id == sessionId).Kind == SessionKind.MockExam
            ? (plan.CertificationId, sessionId)
            : throw new DomainException("That session isn't a mock exam session.");
    }

    private async Task<MockExam> LoadAsync(Guid id, CancellationToken ct) =>
        await db.MockExams.SingleOrDefaultAsync(e => e.Id == id && e.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Mock exam");

    private async Task<Certification> LoadCertificationAsync(Guid id, CancellationToken ct) =>
        await db.Certifications
            .Include(c => c.Domains).ThenInclude(d => d.SkillAreas).ThenInclude(s => s.Objectives)
            .SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Certification");

    private static Dictionary<Guid, Guid> ObjectiveDomains(Certification certification) =>
        certification.Domains
            .SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives.Select(o => (o.Id, DomainId: d.Id))))
            .ToDictionary(o => o.Id, o => o.DomainId);

    private async Task<MockExamView> ToViewAsync(MockExam exam, Certification certification, CancellationToken ct)
    {
        var submitted = exam.Status == MockExamStatus.Submitted;
        var items = (await db.PracticeItems.Where(i => exam.ItemIds.Contains(i.Id)).ToListAsync(ct)).ToDictionary(i => i.Id);
        var objectiveDomains = ObjectiveDomains(certification);
        var domains = certification.Domains.ToDictionary(d => d.Id);
        var titles = submitted ? await DocumentTitlesAsync(items.Values, ct) : [];

        var questions = exam.ItemIds
            .Where(items.ContainsKey)
            .Select(id =>
            {
                var item = items[id];
                var selected = exam.SelectedFor(id);
                Guid? domainId = item.ObjectiveId is { } o && objectiveDomains.TryGetValue(o, out var d) ? d : null;
                return (DomainId: domainId, View: new MockQuestionView(
                    id,
                    domainId is { } did ? domains[did].Name : null,
                    item.Kind,
                    item.Origin,
                    item.Prompt,
                    item.Options,
                    item.CorrectOptions.Count,
                    selected,
                    submitted ? item.CorrectOptions : null,
                    submitted ? item.IsCorrect(selected) : null,
                    submitted ? item.Answer : null,
                    submitted
                        ? item.Sources.Select(s => new ItemSourceView(s.DocumentId, titles.GetValueOrDefault(s.DocumentId), s.Page, s.Quote)).ToList()
                        : null));
            })
            .ToList();

        MockExamResult? result = null;
        if (submitted)
        {
            var scores = certification.Domains
                .Select(d => new DomainScore(
                    d.Id,
                    d.Name,
                    d.Weight,
                    questions.Count(q => q.DomainId == d.Id && q.View.Correct == true),
                    questions.Count(q => q.DomainId == d.Id)))
                .Where(s => s.Total > 0)
                .ToList();
            var correct = questions.Count(q => q.View.Correct == true);
            result = new MockExamResult(correct, questions.Count, Percent(correct, questions.Count) ?? 0, scores);
        }

        var remaining = submitted ? 0 : (int)Math.Max(0, Math.Ceiling((exam.Deadline - clock.Now).TotalSeconds));
        return new MockExamView(
            exam.Id,
            exam.CertificationId,
            certification.ExamCode,
            exam.Status,
            exam.DurationMinutes,
            exam.StartedAt,
            exam.Deadline,
            remaining,
            exam.SubmittedAt,
            questions.Select(q => q.View).ToList(),
            result);
    }

    private async Task<Dictionary<Guid, string>> DocumentTitlesAsync(IEnumerable<PracticeItem> items, CancellationToken ct)
    {
        var ids = items.SelectMany(i => i.Sources).Select(s => s.DocumentId).Distinct().ToList();
        return ids.Count == 0 ? [] : (await db.Documents.Where(d => ids.Contains(d.Id)).ToListAsync(ct)).ToDictionary(d => d.Id, d => d.Title);
    }

    private static int? Percent(int? correct, int total) =>
        correct is { } c && total > 0 ? (int)Math.Round(100.0 * c / total, MidpointRounding.AwayFromZero) : null;
}
