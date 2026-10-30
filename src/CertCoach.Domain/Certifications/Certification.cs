using CertCoach.Domain.Common;

namespace CertCoach.Domain.Certifications;

/// <summary>
/// A certification exam and its official skills outline: domains, skill areas and objectives.
/// </summary>
public sealed class Certification
{
    private readonly List<ExamDomain> _domains = [];

    private Certification()
    {
        ExamCode = null!;
        Name = null!;
        Vendor = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string ExamCode { get; private set; }
    public string Name { get; private set; }
    public string Vendor { get; private set; }
    public CertificationStatus Status { get; private set; }
    public string? ReplacedByExamCode { get; private set; }
    public Uri? OutlineUrl { get; private set; }
    public DateOnly? OutlineVersionDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<ExamDomain> Domains => _domains.OrderBy(d => d.Order).ToList();

    public static Certification Create(
        Guid ownerId, string examCode, string name, string vendor, DateTimeOffset now)
    {
        var certification = new Certification
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CreatedAt = now,
        };
        certification.UpdateDetails(examCode, name, vendor, null, null);
        return certification;
    }

    public void UpdateDetails(
        string examCode, string name, string vendor, Uri? outlineUrl, DateOnly? outlineVersionDate)
    {
        ExamCode = Required(examCode, nameof(examCode)).ToUpperInvariant();
        Name = Required(name, nameof(name));
        Vendor = Required(vendor, nameof(vendor));
        OutlineUrl = outlineUrl;
        OutlineVersionDate = outlineVersionDate;
    }

    public void SetStatus(CertificationStatus status, string? replacedByExamCode = null)
    {
        if (status == CertificationStatus.Replaced && string.IsNullOrWhiteSpace(replacedByExamCode))
        {
            throw new DomainException("A replaced certification must name the exam code that replaces it.");
        }

        Status = status;
        ReplacedByExamCode = status == CertificationStatus.Replaced
            ? replacedByExamCode!.Trim().ToUpperInvariant()
            : null;
    }

    public ExamDomain AddDomain(string name, int weightMinPercent, int weightMaxPercent)
    {
        var domain = new ExamDomain(Id, name, weightMinPercent, weightMaxPercent, _domains.Count);
        _domains.Add(domain);
        return domain;
    }

    /// <summary>Replaces the whole outline, e.g. after the exam's skills outline is updated.</summary>
    public void ClearOutline() => _domains.Clear();

    public int ObjectiveCount => _domains.Sum(d => d.SkillAreas.Sum(s => s.Objectives.Count));

    internal static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{name} is required.")
            : value.Trim();
}
