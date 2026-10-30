namespace CertCoach.Application.Certifications.Outlines;

/// <summary>A parsed exam outline the learner reviews and edits before saving.</summary>
public sealed record OutlineDraft(IReadOnlyList<DomainDraft> Domains, IReadOnlyList<string> Warnings)
{
    public int ObjectiveCount => Domains.Sum(d => d.SkillAreas.Sum(s => s.Objectives.Count));
}

public sealed record DomainDraft(
    string Name, int WeightMinPercent, int WeightMaxPercent, IReadOnlyList<SkillAreaDraft> SkillAreas);

public sealed record SkillAreaDraft(string Name, IReadOnlyList<string> Objectives);
