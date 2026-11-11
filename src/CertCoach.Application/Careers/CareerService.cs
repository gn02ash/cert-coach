using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Application.Careers.Catalogue;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Domain.Careers;
using CertCoach.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Careers;

/// <param name="Exists">False until a profile has been saved; the lists are then empty.</param>
public sealed record ProfileView(
    bool Exists,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> HeldCertifications,
    int? YearsExperience,
    string? TargetRole,
    DateTimeOffset? UpdatedAt);

public sealed record SaveProfileRequest(
    IReadOnlyList<string>? Skills, IReadOnlyList<string>? Roles, IReadOnlyList<string>? HeldCertifications, int? YearsExperience, string? TargetRole);

public sealed record ReadCvTextRequest(string Text);

/// <param name="AddedCertificationId">Set when the learner already has this exam in their certifications.</param>
public sealed record RecommendationView(
    string Code,
    string Name,
    string Vendor,
    CertificationLevel Level,
    string Summary,
    Uri Url,
    int Score,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> NewSkills,
    string? RoleMatch,
    bool MatchesTargetRole,
    string? NextStepAfter,
    IReadOnlyList<string> UsuallyAfter,
    Guid? AddedCertificationId);

/// <param name="CatalogueCheckedOn">When the built-in catalogue was last checked against the vendors' pages.</param>
public sealed record RecommendationsView(DateOnly CatalogueCheckedOn, int CatalogueSize, IReadOnlyList<RecommendationView> Items);

/// <summary>
/// The learner's career profile and the certifications recommended for it. A CV is read into a draft and then
/// discarded: only the profile the learner saves is stored.
/// </summary>
public sealed class CareerService(
    ICertCoachDbContext db,
    ICurrentUser user,
    UserClock clock,
    IPdfTextExtractor pdf,
    CareerAi ai,
    AiCapabilities capabilities)
{
    public const int MaxRecommendations = 8;
    public const long MaxCvBytes = 5 * 1024 * 1024;
    public const int MaxCvTextLength = 100_000;

    private static CertificationCatalogue Catalogue => CertificationCatalogue.Default;

    public async Task<ProfileView> GetProfileAsync(CancellationToken ct) => ToView(await FindAsync(ct));

    public async Task<ProfileView> SaveProfileAsync(SaveProfileRequest request, CancellationToken ct)
    {
        var profile = await FindAsync(ct);
        if (profile is null)
        {
            profile = CareerProfile.Create(user.UserId);
            db.CareerProfiles.Add(profile);
        }

        profile.Update(
            request.Skills?.Select(SkillVocabulary.Canonical),
            request.Roles?.Select(r => r.Trim().ToLowerInvariant()),
            request.HeldCertifications,
            request.YearsExperience,
            request.TargetRole,
            clock.Now);
        await db.SaveChangesAsync(ct);
        return ToView(profile);
    }

    public async Task DeleteProfileAsync(CancellationToken ct)
    {
        if (await FindAsync(ct) is { } profile)
        {
            db.CareerProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Reads a CV's text into a draft profile. Nothing is stored.</summary>
    public async Task<ProfileDraft> ReadCvAsync(string text, CancellationToken ct)
    {
        text = text?.Trim() ?? "";
        if (text.Length < 50)
        {
            throw new DomainException("Paste the text of your CV, or upload it as a PDF.");
        }

        if (text.Length > MaxCvTextLength)
        {
            throw new DomainException("That is too long for a CV. Paste at most 100,000 characters.");
        }

        var offline = CvProfileExtractor.Extract(text, Catalogue, clock.Today.Year);
        if (!capabilities.ChatAvailable)
        {
            return offline;
        }

        var read = await ai.ReadCvAsync(text, ct);
        return read is null
            ? offline with { Notes = ["The model couldn't read this CV, so it was read without AI.", .. offline.Notes] }
            // Exam codes are matched exactly offline, so keep any the model missed.
            : read with { HeldCertifications = read.HeldCertifications.Union(offline.HeldCertifications, StringComparer.OrdinalIgnoreCase).ToList() };
    }

    /// <summary>Reads a CV uploaded as a PDF. The file is read in memory and never written to storage.</summary>
    public async Task<ProfileDraft> ReadCvPdfAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxCvBytes)
        {
            throw new DomainException($"The file is larger than {MaxCvBytes / 1024 / 1024} MB.");
        }

        buffer.Position = 0;
        ExtractedDocument document;
        try
        {
            document = pdf.Extract(buffer);
        }
        catch (InvalidDocumentException)
        {
            throw new DomainException("That file isn't a readable PDF. Paste the text of your CV instead.");
        }

        if (!document.HasText)
        {
            throw new DomainException("This PDF is a scan with no text to read. Paste the text of your CV instead.");
        }

        return await ReadCvAsync(string.Join('\n', document.Blocks.Select(b => b.Text)), ct);
    }

    public async Task<RecommendationsView> RecommendAsync(CancellationToken ct)
    {
        var ranked = await RankAsync(ct);
        var added = (await db.Certifications.Where(c => c.OwnerId == user.UserId).Select(c => new { c.ExamCode, c.Id }).ToListAsync(ct))
            .ToDictionary(c => c.ExamCode, c => c.Id, StringComparer.OrdinalIgnoreCase);

        return new RecommendationsView(Catalogue.CheckedOn, Catalogue.Entries.Count, ranked.Scored
            .Select(s => new RecommendationView(
                s.Entry.Code,
                s.Entry.Name,
                s.Entry.Vendor,
                s.Entry.Level,
                s.Entry.Summary,
                s.Entry.Url,
                s.Score,
                s.MatchedSkills,
                s.NewSkills,
                s.RoleMatch,
                s.MatchesTargetRole,
                s.NextStepAfter,
                s.UsuallyAfter,
                added.TryGetValue(s.Entry.Code, out var id) ? id : null))
            .ToList());
    }

    /// <summary>The model's order and reasons for the same candidates <see cref="RecommendAsync"/> returns.</summary>
    public async Task<AdviceView> AdviseAsync(CancellationToken ct)
    {
        if (!capabilities.ChatAvailable)
        {
            throw new DomainException("AI advice needs a chat provider. Set AI_CHAT_PROVIDER, or use the scores and reasons shown.");
        }

        var (facts, scored) = await RankAsync(ct);
        return scored.Count == 0
            ? throw new DomainException("There is nothing to advise on yet. Add skills or roles to your profile first.")
            : await ai.AdviseAsync(facts, scored, ct);
    }

    private async Task<(ProfileFacts Facts, IReadOnlyList<ScoredCertification> Scored)> RankAsync(CancellationToken ct)
    {
        var profile = await FindAsync(ct) ?? throw new DomainException("Save your profile first: read your CV or fill it in by hand.");
        var facts = new ProfileFacts(profile.Skills, profile.Roles, profile.HeldCertifications, profile.YearsExperience, profile.TargetRole);
        return (facts, RecommendationScorer.Rank(facts, Catalogue, MaxRecommendations));
    }

    private Task<CareerProfile?> FindAsync(CancellationToken ct) =>
        db.CareerProfiles.SingleOrDefaultAsync(p => p.OwnerId == user.UserId, ct);

    private static ProfileView ToView(CareerProfile? p) =>
        p is null
            ? new ProfileView(false, [], [], [], null, null, null)
            : new ProfileView(true, p.Skills, p.Roles, p.HeldCertifications, p.YearsExperience, p.TargetRole, p.UpdatedAt);
}
