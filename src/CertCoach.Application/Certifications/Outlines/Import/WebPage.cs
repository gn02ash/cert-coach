namespace CertCoach.Application.Certifications.Outlines.Import;

/// <summary>A fetched page as text: headings as <c>#</c> lines and list items as <c>-</c> bullets, like pasted markdown.</summary>
/// <param name="Url">Where the page ended up after redirects.</param>
public sealed record WebPage(Uri Url, string? Title, string Text, IReadOnlyList<PageLink> Links);

public sealed record PageLink(Uri Url, string Text);

/// <summary>Fetches public web pages and PDFs. Never fetches from private networks.</summary>
public interface IWebPageFetcher
{
    /// <exception cref="PageFetchException">The page could not be fetched or read; the message is for the learner.</exception>
    Task<WebPage> FetchAsync(Uri url, CancellationToken ct);
}

public sealed class PageFetchException(string message, Exception? inner = null) : Exception(message, inner);
