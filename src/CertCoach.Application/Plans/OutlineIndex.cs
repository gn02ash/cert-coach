using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;

namespace CertCoach.Application.Plans;

/// <summary>Resolves the objective and domain IDs stored on sessions into readable text.</summary>
internal sealed class OutlineIndex
{
    private readonly Dictionary<Guid, SessionObjectiveView> _objectives;
    private readonly Dictionary<Guid, string> _domains;

    public OutlineIndex(Certification certification)
    {
        _domains = certification.Domains.ToDictionary(d => d.Id, d => d.Name);
        _objectives = certification.Domains
            .SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives.Select(o =>
                new SessionObjectiveView(o.Id, o.Text, s.Name, d.Id, d.Name))))
            .ToDictionary(o => o.Id);
    }

    public SessionView ToView(StudySession session) =>
        ToView(session.ToDraftView(), session.Id, session.Sequence, session.Status, session.CompletedAt);

    public SessionView ToView(SessionDraft draft, Guid? id, int sequence, SessionStatus status, DateTimeOffset? completedAt)
    {
        var objectives = draft.ObjectiveIds.Where(_objectives.ContainsKey).Select(o => _objectives[o]).ToList();
        var domainId = draft.FocusDomainId ?? objectives.FirstOrDefault()?.DomainId;
        var domainName = domainId is { } d ? _domains.GetValueOrDefault(d) : null;

        var title = draft.Kind switch
        {
            SessionKind.Review => $"Review: {domainName}",
            SessionKind.MockExam => "Mock exam",
            _ when objectives.Count == 0 => "Study session",
            _ when objectives.Count == 1 => objectives[0].Text,
            _ => $"{objectives[0].Text} (+{objectives.Count - 1} more)",
        };

        return new SessionView(
            id, sequence, draft.Date, draft.DurationMinutes, draft.Kind, status, title, domainId, domainName, objectives, completedAt);
    }
}

internal static class StudySessionExtensions
{
    public static SessionDraft ToDraftView(this StudySession s) =>
        new(s.ScheduledDate, s.DurationMinutes, s.Kind, s.ObjectiveIds, s.FocusDomainId);
}
