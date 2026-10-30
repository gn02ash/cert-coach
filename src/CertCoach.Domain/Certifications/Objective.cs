namespace CertCoach.Domain.Certifications;

/// <summary>
/// A leaf-level exam objective. Study sessions, document chunks and quiz questions are tagged here.
/// </summary>
public sealed class Objective
{
    private Objective()
    {
        Text = null!;
    }

    internal Objective(Guid skillAreaId, string text, int order)
    {
        Id = Guid.CreateVersion7();
        SkillAreaId = skillAreaId;
        Text = Certification.Required(text, nameof(text));
        Order = order;
    }

    public Guid Id { get; private set; }
    public Guid SkillAreaId { get; private set; }
    public string Text { get; private set; }
    public int Order { get; private set; }
}
