using CertCoach.Domain.Common;

namespace CertCoach.Domain.Careers;

/// <summary>
/// What recommendations know about the learner: skills, roles, experience and the certifications they hold.
/// It is read from a CV once and then edited by hand; the CV itself is never stored.
/// </summary>
public sealed class CareerProfile
{
    public const int MaxEntries = 100;
    public const int MaxEntryLength = 80;
    public const int MaxYears = 60;

    private List<string> _skills = [];
    private List<string> _roles = [];
    private List<string> _heldCertifications = [];

    private CareerProfile()
    {
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public IReadOnlyList<string> Skills => _skills;
    public IReadOnlyList<string> Roles => _roles;

    /// <summary>Exam codes or names, as the learner writes them.</summary>
    public IReadOnlyList<string> HeldCertifications => _heldCertifications;

    public int? YearsExperience { get; private set; }

    /// <summary>Where the learner wants to go, in their own words.</summary>
    public string? TargetRole { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CareerProfile Create(Guid ownerId) => new() { Id = Guid.CreateVersion7(), OwnerId = ownerId };

    public void Update(
        IEnumerable<string>? skills,
        IEnumerable<string>? roles,
        IEnumerable<string>? heldCertifications,
        int? yearsExperience,
        string? targetRole,
        DateTimeOffset now)
    {
        if (yearsExperience is < 0 or > MaxYears)
        {
            throw new DomainException($"Years of experience must be between 0 and {MaxYears}.");
        }

        targetRole = string.IsNullOrWhiteSpace(targetRole) ? null : targetRole.Trim();
        if (targetRole?.Length > MaxEntryLength)
        {
            throw new DomainException($"Keep the target role under {MaxEntryLength} characters.");
        }

        // New lists, so EF Core sees the arrays change.
        _skills = Clean(skills, "skills");
        _roles = Clean(roles, "roles");
        _heldCertifications = Clean(heldCertifications, "certifications");
        YearsExperience = yearsExperience;
        TargetRole = targetRole;
        UpdatedAt = now;
    }

    private static List<string> Clean(IEnumerable<string>? values, string what)
    {
        var cleaned = (values ?? [])
            .Select(v => string.Join(' ', (v ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cleaned.Count > MaxEntries)
        {
            throw new DomainException($"List at most {MaxEntries} {what}.");
        }

        return cleaned.Exists(v => v.Length > MaxEntryLength)
            ? throw new DomainException($"Keep each of the {what} under {MaxEntryLength} characters.")
            : cleaned;
    }
}
