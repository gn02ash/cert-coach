using CertCoach.Domain.Common;

namespace CertCoach.Domain.Resources;

public enum ResourceKind
{
    /// <summary>A Microsoft Learn training module.</summary>
    LearnModule,

    /// <summary>A YouTube video.</summary>
    Video,

    /// <summary>Any other page the learner wants to keep.</summary>
    Link,
}

/// <summary>
/// Something to study from outside the learner's own documents, saved against an objective: a Microsoft Learn
/// module, a video or a link. Only the reference is stored; the content stays where it is published.
/// </summary>
public sealed class StudyResource
{
    public const int MaxTitleLength = 300;
    public const int MaxUrlLength = 2048;
    public const int MaxSummaryLength = 1000;
    public const int MaxSourceLength = 100;

    private StudyResource()
    {
        Title = null!;
        Url = null!;
        Source = null!;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CertificationId { get; private set; }

    /// <summary>Null for a resource that covers the whole certification, or whose objective left the outline.</summary>
    public Guid? ObjectiveId { get; private set; }

    public ResourceKind Kind { get; private set; }
    public string Title { get; private set; }
    public Uri Url { get; private set; }

    /// <summary>Who publishes it: "Microsoft Learn", a YouTube channel, or the site's host.</summary>
    public string Source { get; private set; }

    public string? Summary { get; private set; }
    public int? DurationMinutes { get; private set; }
    public bool Done { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static StudyResource Create(
        Guid ownerId,
        Guid certificationId,
        Guid? objectiveId,
        ResourceKind kind,
        string title,
        string url,
        string? source,
        string? summary,
        int? durationMinutes,
        DateTimeOffset now)
    {
        title = title?.Trim() ?? "";
        if (title.Length is 0 or > MaxTitleLength)
        {
            throw new DomainException($"Give the resource a title of at most {MaxTitleLength} characters.");
        }

        if (url is null || url.Length > MaxUrlLength || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new DomainException("Give the resource a full web address, starting with https://.");
        }

        // A suggestion's kind is checked against where it points, so a link can't pass as a Learn module.
        var host = uri.Host.ToLowerInvariant();
        var hostFits = kind switch
        {
            ResourceKind.LearnModule => host == "learn.microsoft.com",
            ResourceKind.Video => host is "www.youtube.com" or "youtube.com" or "youtu.be" or "m.youtube.com",
            _ => true,
        };
        if (!hostFits)
        {
            throw new DomainException(kind == ResourceKind.LearnModule
                ? "A Microsoft Learn module must be on learn.microsoft.com."
                : "A video must be on YouTube. Save other pages as links.");
        }

        source = string.IsNullOrWhiteSpace(source) ? host : source.Trim();
        summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        return new StudyResource
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CertificationId = certificationId,
            ObjectiveId = objectiveId,
            Kind = kind,
            Title = title,
            Url = uri,
            Source = source.Length > MaxSourceLength ? source[..MaxSourceLength] : source,
            Summary = summary is { Length: > MaxSummaryLength } ? summary[..MaxSummaryLength] : summary,
            DurationMinutes = durationMinutes is > 0 and < 100_000 ? durationMinutes : null,
            CreatedAt = now,
        };
    }

    public void SetDone(bool done) => Done = done;
}
