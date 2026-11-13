using CertCoach.Application.Abstractions;
using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using CertCoach.Domain.Resources;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Resources;

/// <summary>Microsoft's public catalogue of Learn modules, downloaded and cached. Null when it can't be reached.</summary>
public interface ILearnCatalog
{
    Task<LearnIndex?> GetAsync(CancellationToken ct);
}

public sealed record VideoResult(string Title, Uri Url, string Channel, string? Description);

/// <summary>YouTube search, available only when an API key is configured.</summary>
public interface IVideoSearch
{
    bool Available { get; }

    /// <summary>The videos found, or null when the search failed (for example, the daily quota is spent).</summary>
    Task<IReadOnlyList<VideoResult>?> SearchAsync(string query, int limit, CancellationToken ct);
}

/// <param name="Saved">True when this URL is already saved for the objective.</param>
public sealed record SuggestedResource(
    ResourceKind Kind, string Title, Uri Url, string Source, string? Summary, int? DurationMinutes, bool Saved);

/// <param name="YouTubeSearchUrl">Opens a YouTube search for the objective in the browser; always there.</param>
/// <param name="Notes">What couldn't be searched and why.</param>
public sealed record ResourceSuggestions(
    Guid ObjectiveId,
    string Objective,
    IReadOnlyList<SuggestedResource> Learn,
    IReadOnlyList<SuggestedResource> Videos,
    bool YouTubeAvailable,
    Uri YouTubeSearchUrl,
    IReadOnlyList<string> Notes);

public sealed record ResourceView(
    Guid Id,
    Guid CertificationId,
    Guid? ObjectiveId,
    ResourceKind Kind,
    string Title,
    Uri Url,
    string Source,
    string? Summary,
    int? DurationMinutes,
    bool Done,
    DateTimeOffset CreatedAt);

public sealed record SaveResourceRequest(
    Guid CertificationId, Guid? ObjectiveId, ResourceKind Kind, string Title, string Url, string? Source, string? Summary, int? DurationMinutes);

public sealed record SetResourceDoneRequest(bool Done);

/// <summary>Suggests study resources for an objective and keeps the ones the learner saves.</summary>
public sealed class ResourceService(ICertCoachDbContext db, ICurrentUser user, UserClock clock, ILearnCatalog learn, IVideoSearch videos)
{
    public const int MaxSuggestions = 5;

    public async Task<ResourceSuggestions> SuggestAsync(Guid objectiveId, CancellationToken ct)
    {
        var objective = await db.Certifications
            .Where(c => c.OwnerId == user.UserId)
            .SelectMany(c => c.Domains.SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives.Select(o => new { o.Id, o.Text, c.ExamCode, c.Vendor }))))
            .SingleOrDefaultAsync(o => o.Id == objectiveId, ct)
            ?? throw new NotFoundException("Objective");
        var saved = (await db.StudyResources.Where(r => r.OwnerId == user.UserId && r.ObjectiveId == objectiveId).Select(r => r.Url).ToListAsync(ct))
            .Select(u => u.AbsoluteUri)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        IReadOnlyList<SuggestedResource> learnModules = [];
        if (await learn.GetAsync(ct) is { } index)
        {
            learnModules = index.Search(objective.Text, MaxSuggestions)
                .Select(m => new SuggestedResource(
                    ResourceKind.LearnModule, m.Module.Title, m.Module.Url, "Microsoft Learn", m.Module.Summary, m.Module.DurationMinutes, saved.Contains(m.Module.Url.AbsoluteUri)))
                .ToList();
        }
        else
        {
            notes.Add("Microsoft Learn's catalogue couldn't be reached. Try again later.");
        }

        // The exam code narrows a search to videos made for this exam.
        var query = $"{objective.ExamCode} {objective.Text}";
        IReadOnlyList<SuggestedResource> found = [];
        if (videos.Available)
        {
            if (await videos.SearchAsync(query, MaxSuggestions, ct) is { } results)
            {
                found = results
                    .Select(v => new SuggestedResource(ResourceKind.Video, v.Title, v.Url, v.Channel, v.Description, null, saved.Contains(v.Url.AbsoluteUri)))
                    .ToList();
            }
            else
            {
                notes.Add("YouTube search isn't available right now; the daily quota may be spent. Use the search link instead.");
            }
        }

        return new ResourceSuggestions(
            objectiveId,
            objective.Text,
            learnModules,
            found,
            videos.Available,
            new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query)),
            notes);
    }

    public async Task<IReadOnlyList<ResourceView>> ListAsync(Guid certificationId, CancellationToken ct)
    {
        await EnsureCertificationAsync(certificationId, null, ct);
        return (await db.StudyResources
                .Where(r => r.OwnerId == user.UserId && r.CertificationId == certificationId)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct))
            .Select(ToView)
            .ToList();
    }

    /// <summary>Saves a suggestion or a link of the learner's own. Saving the same URL for an objective twice keeps one.</summary>
    public async Task<ResourceView> SaveAsync(SaveResourceRequest request, CancellationToken ct)
    {
        await EnsureCertificationAsync(request.CertificationId, request.ObjectiveId, ct);
        var resource = StudyResource.Create(
            user.UserId, request.CertificationId, request.ObjectiveId, request.Kind, request.Title, request.Url, request.Source, request.Summary,
            request.DurationMinutes, clock.Now);

        var existing = await db.StudyResources.FirstOrDefaultAsync(
            r => r.OwnerId == user.UserId && r.CertificationId == request.CertificationId && r.ObjectiveId == request.ObjectiveId && r.Url == resource.Url, ct);
        if (existing is not null)
        {
            return ToView(existing);
        }

        db.StudyResources.Add(resource);
        await db.SaveChangesAsync(ct);
        return ToView(resource);
    }

    public async Task<ResourceView> SetDoneAsync(Guid id, bool done, CancellationToken ct)
    {
        var resource = await LoadAsync(id, ct);
        resource.SetDone(done);
        await db.SaveChangesAsync(ct);
        return ToView(resource);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        db.StudyResources.Remove(await LoadAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task<StudyResource> LoadAsync(Guid id, CancellationToken ct) =>
        await db.StudyResources.SingleOrDefaultAsync(r => r.Id == id && r.OwnerId == user.UserId, ct)
        ?? throw new NotFoundException("Resource");

    private async Task EnsureCertificationAsync(Guid certificationId, Guid? objectiveId, CancellationToken ct)
    {
        if (!await db.Certifications.AnyAsync(c => c.Id == certificationId && c.OwnerId == user.UserId, ct))
        {
            throw new NotFoundException("Certification");
        }

        if (objectiveId is { } id && !await db.Certifications
                .Where(c => c.Id == certificationId)
                .SelectMany(c => c.Domains.SelectMany(d => d.SkillAreas.SelectMany(s => s.Objectives)))
                .AnyAsync(o => o.Id == id, ct))
        {
            throw new DomainException("That objective isn't in this certification's outline.");
        }
    }

    private static ResourceView ToView(StudyResource r) => new(
        r.Id, r.CertificationId, r.ObjectiveId, r.Kind, r.Title, r.Url, r.Source, r.Summary, r.DurationMinutes, r.Done, r.CreatedAt);
}
