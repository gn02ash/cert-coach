using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CertCoach.Application.Resources;
using Microsoft.Extensions.Logging;

namespace CertCoach.Infrastructure.Resources;

/// <summary>
/// Microsoft's public Learn catalogue (no key needed): about 3,500 modules in one download, kept in memory for a
/// day. A failed download is retried after a few minutes, and callers get null meanwhile.
/// </summary>
internal sealed partial class MicrosoftLearnCatalog(IHttpClientFactory http, TimeProvider time, ILogger<MicrosoftLearnCatalog> logger)
    : ILearnCatalog, IDisposable
{
    public const string ClientName = "learn-catalog";
    public static readonly Uri CatalogUrl = new("https://learn.microsoft.com/api/catalog/?type=modules&locale=en-us");

    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private LearnIndex? _index;
    private DateTimeOffset _refreshAt = DateTimeOffset.MinValue;

    public async Task<LearnIndex?> GetAsync(CancellationToken ct)
    {
        if (time.GetUtcNow() < _refreshAt)
        {
            return _index;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (time.GetUtcNow() < _refreshAt)
            {
                return _index;
            }

            try
            {
                using var client = http.CreateClient(ClientName);
                var catalog = await client.GetFromJsonAsync<Catalog>(CatalogUrl, ct);
                _index = new LearnIndex((catalog?.Modules ?? [])
                    .Where(m => m.Title is { Length: > 0 } && m.Url is not null && m.Url.Host == "learn.microsoft.com")
                    .Select(m => new LearnModule(
                        m.Uid ?? m.Url!.AbsolutePath,
                        m.Title!,
                        m.Summary ?? "",
                        // Without the tracking query string the API adds.
                        new UriBuilder(m.Url!) { Query = "" }.Uri,
                        m.DurationInMinutes,
                        m.Levels ?? []))
                    .ToList());
                _refreshAt = time.GetUtcNow() + KeepFor;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
            {
                // Keep serving the previous catalogue, if there is one.
                LogDownloadFailed(e);
                _refreshAt = time.GetUtcNow() + RetryAfter;
            }

            return _index;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not download the Microsoft Learn catalogue")]
    private partial void LogDownloadFailed(Exception exception);

    private sealed record Catalog(List<CatalogModule>? Modules);

    private sealed record CatalogModule(
        string? Uid,
        string? Title,
        string? Summary,
        Uri? Url,
        [property: JsonPropertyName("duration_in_minutes")] int? DurationInMinutes,
        List<string>? Levels);
}

/// <summary>
/// YouTube Data API search. The key goes in a header, not the URL, so request logging never records it, and
/// results are remembered for a day because each search costs 100 of the 10,000 daily quota units.
/// </summary>
internal sealed partial class YouTubeVideoSearch(IHttpClientFactory http, string? apiKey, TimeProvider time, ILogger<YouTubeVideoSearch> logger)
    : IVideoSearch
{
    public const string ClientName = "youtube";
    private const int MaxRemembered = 500;
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<VideoResult> Results)> _remembered = new(StringComparer.OrdinalIgnoreCase);

    public bool Available => !string.IsNullOrWhiteSpace(apiKey);

    public async Task<IReadOnlyList<VideoResult>?> SearchAsync(string query, int limit, CancellationToken ct)
    {
        if (!Available)
        {
            return null;
        }

        var key = $"{limit}|{query}";
        if (_remembered.TryGetValue(key, out var hit) && time.GetUtcNow() - hit.At < KeepFor)
        {
            return hit.Results;
        }

        try
        {
            using var client = http.CreateClient(ClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&safeSearch=strict&relevanceLanguage=en&maxResults={limit}&q={Uri.EscapeDataString(query)}");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogSearchFailed((int)response.StatusCode);
                return null;
            }

            var page = await response.Content.ReadFromJsonAsync<SearchPage>(ct);
            var results = (page?.Items ?? [])
                .Where(i => i.Id?.VideoId is { Length: > 0 } && i.Snippet?.Title is { Length: > 0 })
                .Select(i => new VideoResult(
                    // The API returns titles with HTML entities.
                    WebUtility.HtmlDecode(i.Snippet!.Title!),
                    new Uri("https://www.youtube.com/watch?v=" + Uri.EscapeDataString(i.Id!.VideoId!)),
                    WebUtility.HtmlDecode(i.Snippet.ChannelTitle ?? "YouTube"),
                    i.Snippet.Description is { Length: > 0 } d ? WebUtility.HtmlDecode(d) : null))
                .ToList();

            if (_remembered.Count >= MaxRemembered)
            {
                _remembered.Clear();
            }

            _remembered[key] = (time.GetUtcNow(), results);
            return results;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            LogSearchFailed(0);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "YouTube search failed with status {Status}")]
    private partial void LogSearchFailed(int status);

    private sealed record SearchPage(List<SearchItem>? Items);

    private sealed record SearchItem(SearchId? Id, Snippet? Snippet);

    private sealed record SearchId(string? VideoId);

    private sealed record Snippet(string? Title, string? ChannelTitle, string? Description);
}
