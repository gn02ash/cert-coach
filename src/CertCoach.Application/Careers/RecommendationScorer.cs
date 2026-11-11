using CertCoach.Application.Careers.Catalogue;

namespace CertCoach.Application.Careers;

/// <summary>What the scorer needs from a profile.</summary>
public sealed record ProfileFacts(
    IReadOnlyList<string> Skills, IReadOnlyList<string> Roles, IReadOnlyList<string> HeldCertifications, int? YearsExperience, string? TargetRole);

/// <param name="Score">0–100: skills, role and path make up the fit, which the level's suitability then scales.</param>
/// <param name="MatchedSkills">Skills the certification covers that the profile already has.</param>
/// <param name="NewSkills">Skills it covers that the profile lacks: what studying for it would add.</param>
/// <param name="RoleMatch">The profile role it suits, with the target role taking precedence; null if none.</param>
/// <param name="NextStepAfter">A held certification this one commonly follows.</param>
/// <param name="UsuallyAfter">Certifications commonly taken first that the learner doesn't hold.</param>
public sealed record ScoredCertification(
    CatalogueEntry Entry,
    int Score,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> NewSkills,
    string? RoleMatch,
    bool MatchesTargetRole,
    string? NextStepAfter,
    IReadOnlyList<string> UsuallyAfter);

/// <summary>
/// Ranks the catalogue for a profile with fixed, explainable rules, so recommendations work offline and every
/// score can be traced. The fit is how much of the certification's content the learner already knows (its
/// platform, such as Azure or AWS, counting three times), whether it suits their role or the one they want, and
/// whether it follows from a certification they hold. The fit is then scaled by how well the level suits their
/// experience, so a fundamentals exam can't top a senior's list just because they know all of it. Held
/// certifications are left out, as are fundamentals from a vendor whose higher-level certification they hold.
/// </summary>
public static class RecommendationScorer
{
    public const int SkillPoints = 50;
    public const int RolePoints = 25;
    public const int PathPoints = 10;

    /// <summary>The share of the fit a certification keeps at the worst level fit.</summary>
    public const double LevelFloor = 0.4;

    /// <summary>Knowing an exam's platform matters more than any one topic on it.</summary>
    private static readonly HashSet<string> Platforms = ["azure", "aws", "google cloud", "microsoft 365", "microsoft fabric", "power platform"];

    public static IReadOnlyList<ScoredCertification> Rank(ProfileFacts profile, CertificationCatalogue catalogue, int limit)
    {
        var skills = SkillVocabulary.Expand(profile.Skills);
        var targetRoles = profile.TargetRole is { } target ? SkillVocabulary.FindRoles(target).ToHashSet() : [];
        var currentRoles = profile.Roles
            .SelectMany(r => SkillVocabulary.Roles.Contains(r, StringComparer.OrdinalIgnoreCase) ? [r.ToLowerInvariant()] : SkillVocabulary.FindRoles(r))
            .ToHashSet();

        var held = profile.HeldCertifications
            .Select(h => catalogue.Find(h) ?? catalogue.Entries.FirstOrDefault(e => h.Contains(e.Name, StringComparison.OrdinalIgnoreCase) || h.Contains(e.Code, StringComparison.OrdinalIgnoreCase)))
            .OfType<CatalogueEntry>()
            .ToList();
        var heldCodes = held.Select(h => h.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vendorsBeyondFundamentals = held.Where(h => h.Level != CertificationLevel.Fundamentals).Select(h => h.Vendor).ToHashSet();

        return catalogue.Entries
            .Where(e => !heldCodes.Contains(e.Code))
            .Where(e => !(e.Level == CertificationLevel.Fundamentals && vendorsBeyondFundamentals.Contains(e.Vendor)))
            .Select(e => Score(e, skills, currentRoles, targetRoles, heldCodes, profile.YearsExperience))
            // Something must connect it to the learner: a skill, a role, or a certification they hold.
            .Where(s => s.MatchedSkills.Count > 0 || s.RoleMatch is not null || s.NextStepAfter is not null)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Entry.Level)
            .ThenBy(s => s.Entry.Code, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    private static ScoredCertification Score(
        CatalogueEntry entry, HashSet<string> skills, HashSet<string> currentRoles, HashSet<string> targetRoles, HashSet<string> heldCodes, int? years)
    {
        var matched = entry.Skills.Where(skills.Contains).ToList();
        var missing = entry.Skills.Where(s => !skills.Contains(s)).ToList();
        static int Weight(string skill) => Platforms.Contains(skill) ? 3 : 1;
        var skillShare = entry.Skills.Count == 0 ? 0 : (double)matched.Sum(Weight) / entry.Skills.Sum(Weight);

        var targetMatch = entry.Roles.FirstOrDefault(targetRoles.Contains);
        var currentMatch = entry.Roles.FirstOrDefault(currentRoles.Contains);
        var roleShare = targetMatch is not null ? 1 : currentMatch is not null ? 0.7 : 0;

        var nextStepAfter = entry.After.FirstOrDefault(heldCodes.Contains);
        var pathShare = nextStepAfter is not null ? 1 : entry.After.Count == 0 ? 0.5 : 0;

        var fit = ((SkillPoints * skillShare) + (RolePoints * roleShare) + (PathPoints * pathShare)) / (SkillPoints + RolePoints + PathPoints);
        var score = 100 * fit * (LevelFloor + ((1 - LevelFloor) * LevelFit(entry.Level, years)));
        return new ScoredCertification(
            entry,
            (int)Math.Round(score, MidpointRounding.AwayFromZero),
            matched,
            missing,
            targetMatch ?? currentMatch,
            targetMatch is not null,
            nextStepAfter,
            nextStepAfter is null ? entry.After : []);
    }

    /// <summary>How well a level suits the experience, 0–1. Unknown experience is neutral for every level.</summary>
    internal static double LevelFit(CertificationLevel level, int? years) => (level, years) switch
    {
        (_, null) => 0.6,
        (CertificationLevel.Fundamentals, <= 1) => 1,
        (CertificationLevel.Fundamentals, <= 3) => 0.5,
        (CertificationLevel.Fundamentals, _) => 0.15,
        (CertificationLevel.Associate, < 1) => 0.5,
        (CertificationLevel.Associate, <= 6) => 1,
        (CertificationLevel.Associate, _) => 0.7,
        (_, < 2) => 0.2,
        (_, < 4) => 0.7,
        _ => 1,
    };
}
