using CertCoach.Application.Search;

namespace CertCoach.Api.Endpoints;

internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", (string q, Guid? certificationId, int? limit, SearchMode? mode, SearchService service, CancellationToken ct) =>
                service.SearchAsync(new SearchRequest(q, certificationId, limit ?? SearchService.DefaultLimit, mode ?? SearchMode.Hybrid), ct))
            .WithName("Search")
            .WithTags("Search")
            .WithSummary("Find the passages in your documents that best match a question or topic.");

        return api;
    }
}
