using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Practice.Readiness;

/// <summary>Readiness per domain from the learner's review log. See <see cref="ReadinessCalculator"/> for the formula.</summary>
public sealed class ReadinessService(ICertCoachDbContext db, ICurrentUser user, UserClock clock)
{
    public async Task<ReadinessReport> GetAsync(Guid certificationId, CancellationToken ct)
    {
        var certification = await db.Certifications
            .Include(c => c.Domains).ThenInclude(d => d.SkillAreas).ThenInclude(s => s.Objectives)
            .SingleOrDefaultAsync(c => c.Id == certificationId && c.OwnerId == user.UserId, ct)
            ?? throw new NotFoundException("Certification");

        var since = clock.Now.AddDays(-ReadinessCalculator.WindowDays);

        // Joined to the item for its current objective, so answers follow items when the outline is edited.
        var logs = await db.ReviewLogs
            .Where(r => r.OwnerId == user.UserId && r.CertificationId == certificationId && r.ReviewedAt >= since)
            .Join(db.PracticeItems.Where(i => i.ObjectiveId != null), r => r.ItemId, i => i.Id,
                (r, i) => new { ObjectiveId = i.ObjectiveId!.Value, r.Correct, r.Grade, r.ReviewedAt })
            .ToListAsync(ct);

        var answers = logs
            .Select(l => new ReadinessAnswer(l.ObjectiveId, l.Correct, l.Correct ?? l.Grade >= ReviewGrade.Hard, l.ReviewedAt))
            .ToList();
        var domains = certification.Domains
            .Select(d => new ReadinessDomain(d.Id, d.Name, d.Weight, d.SkillAreas.SelectMany(s => s.Objectives).Select(o => o.Id).ToList()))
            .ToList();
        return ReadinessCalculator.Compute(domains, answers, clock.Now);
    }
}
