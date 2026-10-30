namespace CertCoach.Domain.Certifications;

/// <summary>A group of objectives inside a domain, e.g. "Select the appropriate Azure AI services".</summary>
public sealed class SkillArea
{
    private readonly List<Objective> _objectives = [];

    private SkillArea()
    {
        Name = null!;
    }

    internal SkillArea(Guid domainId, string name, int order)
    {
        Id = Guid.CreateVersion7();
        DomainId = domainId;
        Name = Certification.Required(name, nameof(name));
        Order = order;
    }

    public Guid Id { get; private set; }
    public Guid DomainId { get; private set; }
    public string Name { get; private set; }
    public int Order { get; private set; }

    public IReadOnlyList<Objective> Objectives => _objectives.OrderBy(o => o.Order).ToList();

    public Objective AddObjective(string text)
    {
        var objective = new Objective(Id, text, _objectives.Count);
        _objectives.Add(objective);
        return objective;
    }
}
