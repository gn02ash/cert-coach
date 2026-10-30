using CertCoach.Domain.Common;

namespace CertCoach.Domain.Certifications;

/// <summary>
/// A top-level exam domain, e.g. "Plan and manage an Azure AI solution (15–20%)".
/// Weights are stored as the published range.
/// </summary>
public sealed class ExamDomain
{
    private readonly List<SkillArea> _skillAreas = [];

    private ExamDomain()
    {
        Name = null!;
    }

    internal ExamDomain(Guid certificationId, string name, int weightMinPercent, int weightMaxPercent, int order)
    {
        if (weightMinPercent < 0 || weightMaxPercent > 100 || weightMinPercent > weightMaxPercent)
        {
            throw new DomainException(
                $"Invalid weight range {weightMinPercent}–{weightMaxPercent}% for domain '{name}'.");
        }

        Id = Guid.CreateVersion7();
        CertificationId = certificationId;
        Name = Certification.Required(name, nameof(name));
        WeightMinPercent = weightMinPercent;
        WeightMaxPercent = weightMaxPercent;
        Order = order;
    }

    public Guid Id { get; private set; }
    public Guid CertificationId { get; private set; }
    public string Name { get; private set; }
    public int WeightMinPercent { get; private set; }
    public int WeightMaxPercent { get; private set; }
    public int Order { get; private set; }

    /// <summary>The midpoint of the published range, used to weight study time.</summary>
    public decimal Weight => (WeightMinPercent + WeightMaxPercent) / 2m;

    public IReadOnlyList<SkillArea> SkillAreas => _skillAreas.OrderBy(s => s.Order).ToList();

    public IEnumerable<Objective> Objectives => SkillAreas.SelectMany(s => s.Objectives);

    public SkillArea AddSkillArea(string name)
    {
        var skillArea = new SkillArea(Id, name, _skillAreas.Count);
        _skillAreas.Add(skillArea);
        return skillArea;
    }
}
