using System.Text.RegularExpressions;
using CertCoach.Application.Ai;
using CertCoach.Domain.Common;

namespace CertCoach.Application.Certifications.Outlines.Import;

/// <summary>
/// Creates an outline draft from a URL: an exam page, a study guide or an exam guide PDF. Tries the cheapest
/// reader that works, in order:
/// <list type="number">
/// <item>The deterministic parser, on the page and then on its "study guide" links (offline, and enough for
/// Microsoft Learn).</item>
/// <item>The AI parser, on a page that has weights but a layout the parser can't read.</item>
/// <item>An agent that browses the same site with a read-only fetch tool to find the outline.</item>
/// </list>
/// At most <see cref="MaxPages"/> pages are fetched in all. The learner reviews the draft before saving.
/// </summary>
public sealed partial class OutlineImporter(
    IWebPageFetcher fetcher, AiOutlineParser aiParser, OutlineAgent agent, AiCapabilities capabilities)
{
    public const int MaxPages = 5;

    public async Task<OutlineImport> ImportAsync(string url, CancellationToken ct)
    {
        var start = ParseUrl(url);
        var session = new FetchSession(fetcher, ct);

        WebPage first;
        try
        {
            first = await session.FetchAsync(start);
        }
        catch (PageFetchException e)
        {
            throw new DomainException(e.Message);
        }

        for (var page = first; page is not null; page = await NextStudyGuideAsync(session, page, start))
        {
            var draft = OutlineParser.Parse(page.Text);
            if (IsComplete(draft))
            {
                return Result(draft, page, session, OutlineImportMethod.Parser);
            }
        }

        if (!capabilities.ChatAvailable)
        {
            throw new DomainException(
                "No skills outline found at this URL. Try the exam's study guide page, paste the outline instead, " +
                "or set AI_CHAT_PROVIDER so an AI agent can look for it.");
        }

        if (session.Pages.LastOrDefault(p => Weight().IsMatch(p.Text)) is { } weighted)
        {
            var draft = await aiParser.ParseAsync(Focus(weighted.Text), ct);
            if (IsComplete(draft))
            {
                return Result(draft, weighted, session, OutlineImportMethod.Ai);
            }
        }

        var found = await agent.FindAsync(first, session, ct);
        if (found is null || found.Draft.Domains.Count == 0)
        {
            throw new DomainException(
                "The AI agent couldn't find a skills outline on this site. Open the exam's study guide and paste the outline instead.");
        }

        return Result(found.Draft, found.Page, session, OutlineImportMethod.Agent);
    }

    /// <summary>Every domain has at least one objective. An exam overview page lists only the domains.</summary>
    internal static bool IsComplete(OutlineDraft draft) =>
        draft.Domains.Count >= 2 && draft.Domains.All(d => d.SkillAreas.Any(s => s.Objectives.Count > 0));

    /// <summary>Links on the same site that likely lead to the full outline, best first.</summary>
    internal static IEnumerable<PageLink> StudyGuideLinks(WebPage page, Uri start) =>
        page.Links
            .Where(l => SameSite(l.Url, start) && !SamePage(l.Url, page.Url))
            .Select(l => (Link: l, Score: StudyGuideScore(l)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Link)
            .Concat(MicrosoftLearnStudyGuides(page, start));

    /// <summary>
    /// Microsoft Learn exam and certification pages add their study guide link with JavaScript, so it isn't in
    /// the HTML, but every study guide lives at the same address pattern. The exam code comes from the URL the
    /// learner gave (exam pages redirect to their certification), then from the page.
    /// </summary>
    internal static IEnumerable<PageLink> MicrosoftLearnStudyGuides(WebPage page, Uri start)
    {
        if (!page.Url.Host.Equals(MicrosoftLearnHost, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var segments = page.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var locale = segments.Length > 0 && Locale().IsMatch(segments[0]) ? segments[0] : "en-us";
        return PageFacts.ExamCodes(start.AbsolutePath.ToUpperInvariant())
            .Concat(PageFacts.ExamCodes(page.Title ?? ""))
            .Concat(PageFacts.ExamCodes(page.Text))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Select(code => new PageLink(
                new Uri($"https://{MicrosoftLearnHost}/{locale}/credentials/certifications/resources/study-guides/{code.ToLowerInvariant()}"),
                $"Study guide for Exam {code}"));
    }

    private const string MicrosoftLearnHost = "learn.microsoft.com";

    /// <summary>Same host, or hosts sharing the last two labels (learn.microsoft.com and www.microsoft.com).</summary>
    internal static bool SameSite(Uri a, Uri b)
    {
        static string Site(Uri u) => string.Join('.', u.Host.ToLowerInvariant().Split('.').TakeLast(2));
        return a.Scheme is "http" or "https" && Site(a) == Site(b);
    }

    internal static bool SamePage(Uri a, Uri b) =>
        Uri.Compare(a, b, UriComponents.HttpRequestUrl, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>The AI parser takes at most 30,000 characters; start a little before the first weight.</summary>
    internal static string Focus(string text)
    {
        if (text.Length <= AiOutlineParser.MaxInputLength)
        {
            return text;
        }

        var from = Math.Max(0, Weight().Match(text).Index - 2_000);
        return text.Substring(from, Math.Min(AiOutlineParser.MaxInputLength, text.Length - from));
    }

    private static Uri ParseUrl(string url)
    {
        url = url?.Trim() ?? "";
        if (url.Length > 0 && !url.Contains("://", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new DomainException("Enter the full address of a web page or PDF, starting with https://.");
        }

        return uri;
    }

    private static async Task<WebPage?> NextStudyGuideAsync(FetchSession session, WebPage page, Uri start)
    {
        foreach (var link in StudyGuideLinks(page, start))
        {
            if (session.Remaining == 0)
            {
                return null;
            }

            if (session.Pages.Any(p => SamePage(p.Url, link.Url)))
            {
                continue;
            }

            try
            {
                return await session.FetchAsync(link.Url);
            }
            catch (PageFetchException)
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static int StudyGuideScore(PageLink link)
    {
        var text = link.Text.ToLowerInvariant();
        var path = link.Url.AbsolutePath.ToLowerInvariant();
        var score = 0;
        score += text.Contains("study guide", StringComparison.Ordinal) || text.Contains("exam guide", StringComparison.Ordinal) ? 3 : 0;
        score += text.Contains("skills measured", StringComparison.Ordinal) ? 2 : 0;
        score += path.Contains("study-guide", StringComparison.Ordinal) || path.Contains("exam-guide", StringComparison.Ordinal) ? 2 : 0;
        score += score > 0 && path.EndsWith(".pdf", StringComparison.Ordinal) ? 1 : 0;
        return score;
    }

    private static OutlineImport Result(OutlineDraft draft, WebPage page, FetchSession session, OutlineImportMethod method) =>
        new(
            draft,
            page.Url,
            PageFacts.ExamCode(page) ?? session.Pages.Select(PageFacts.ExamCode).FirstOrDefault(c => c is not null),
            PageFacts.Name(page),
            PageFacts.Vendor(page.Url),
            PageFacts.OutlineVersionDate(page),
            session.Pages.Select(p => p.Url).ToList(),
            method);

    [GeneratedRegex(@"\d{1,3}\s*%")]
    private static partial Regex Weight();

    [GeneratedRegex("^[a-z]{2}-[a-z]{2}$")]
    private static partial Regex Locale();
}

/// <summary>The pages fetched for one import: a shared budget, with repeat requests served from memory.</summary>
public sealed class FetchSession(IWebPageFetcher fetcher, CancellationToken ct)
{
    private readonly List<WebPage> pages = [];
    private readonly Dictionary<string, WebPage> byRequest = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WebPage> Pages => pages;

    public int Remaining => OutlineImporter.MaxPages - pages.Count;

    /// <exception cref="PageFetchException">Also thrown when the page budget is spent.</exception>
    public async Task<WebPage> FetchAsync(Uri url)
    {
        var key = url.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
        if (byRequest.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // A link to where an earlier request was redirected.
        if (pages.Find(p => OutlineImporter.SamePage(p.Url, url)) is { } landed)
        {
            return landed;
        }

        if (Remaining <= 0)
        {
            throw new PageFetchException($"Read {OutlineImporter.MaxPages} pages already, the limit for one import.");
        }

        var page = await fetcher.FetchAsync(url, ct);
        pages.Add(page);
        byRequest[key] = page;
        return page;
    }
}
