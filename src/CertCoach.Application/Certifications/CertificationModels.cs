using CertCoach.Application.Certifications.Outlines;
using CertCoach.Domain.Certifications;

namespace CertCoach.Application.Certifications;

public sealed record CertificationSummary(
    Guid Id,
    string ExamCode,
    string Name,
    string Vendor,
    CertificationStatus Status,
    string? ReplacedByExamCode,
    int DomainCount,
    int ObjectiveCount,
    Guid? ActivePlanId);

public sealed record CertificationDetail(
    Guid Id,
    string ExamCode,
    string Name,
    string Vendor,
    CertificationStatus Status,
    string? ReplacedByExamCode,
    Uri? OutlineUrl,
    DateOnly? OutlineVersionDate,
    Guid? ActivePlanId,
    IReadOnlyList<DomainView> Domains);

public sealed record DomainView(
    Guid Id, string Name, int WeightMinPercent, int WeightMaxPercent, IReadOnlyList<SkillAreaView> SkillAreas);

public sealed record SkillAreaView(Guid Id, string Name, IReadOnlyList<ObjectiveView> Objectives);

public sealed record ObjectiveView(Guid Id, string Text);

/// <summary>Creates or replaces a certification, including its whole outline.</summary>
public sealed record SaveCertificationRequest(
    string ExamCode,
    string Name,
    string Vendor,
    Uri? OutlineUrl,
    DateOnly? OutlineVersionDate,
    CertificationStatus Status,
    string? ReplacedByExamCode,
    IReadOnlyList<DomainDraft> Outline);

public sealed record ParseOutlineRequest(string Text);
