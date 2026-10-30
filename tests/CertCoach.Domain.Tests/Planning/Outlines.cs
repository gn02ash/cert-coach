using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using CertificationEntity = CertCoach.Domain.Certifications.Certification;

namespace CertCoach.Domain.Tests.Planning;

/// <summary>Synthetic outlines and dates for scheduler tests.</summary>
internal static class Outlines
{
    /// <summary>A Monday, so weekly patterns are easy to reason about.</summary>
    public static readonly DateOnly Monday = new(2026, 11, 2);

    public static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    public static OutlineDomain Weighted(decimal weight, int objectives) =>
        new(Guid.NewGuid(), weight, Enumerable.Range(0, objectives).Select(_ => Guid.NewGuid()).ToList());

    /// <summary>Three domains weighted 50/30/20 with 4, 3 and 2 objectives.</summary>
    public static IReadOnlyList<OutlineDomain> ThreeDomains() =>
        [Weighted(50, 4), Weighted(30, 3), Weighted(20, 2)];

    public static PlanSettings Settings(int weeks, decimal hoursPerWeek = 5m, StudyDays days = StudyDays.Everyday) =>
        new(Monday, Monday.AddDays(weeks * 7), hoursPerWeek, days);

    /// <summary>A certification shaped like <see cref="ThreeDomains"/>.</summary>
    public static Certification Certification(Guid ownerId)
    {
        var certification = CertificationEntity.Create(ownerId, "DEMO-101", "Demo Cert", "Contoso", Now);
        AddDomain(certification, "Plan solutions", 45, 55, 4);
        AddDomain(certification, "Build solutions", 25, 35, 3);
        AddDomain(certification, "Monitor solutions", 15, 25, 2);
        return certification;
    }

    private static void AddDomain(Certification certification, string name, int min, int max, int objectives)
    {
        var area = certification.AddDomain(name, min, max).AddSkillArea($"{name} skills");
        for (var i = 1; i <= objectives; i++)
        {
            area.AddObjective($"{name} objective {i}");
        }
    }
}
