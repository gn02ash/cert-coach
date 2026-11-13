using CertCoach.Api.Infrastructure;
using CertCoach.Application.Resources;

namespace CertCoach.Api.Endpoints;

internal static class ResourceEndpoints
{
    public static RouteGroupBuilder MapResourceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/resources").WithTags("Resources");

        group.MapGet("/suggestions", (Guid objectiveId, ResourceService service, CancellationToken ct) => service.SuggestAsync(objectiveId, ct))
            .RequireRateLimiting(RateLimits.Costly)
            .WithName("SuggestResources")
            .WithSummary("Microsoft Learn modules and YouTube videos that cover an objective.")
            .WithDescription("Learn modules come from Microsoft's public catalogue. YouTube results need YOUTUBE_API_KEY; a search link is always returned.");

        group.MapGet("/", (Guid certificationId, ResourceService service, CancellationToken ct) => service.ListAsync(certificationId, ct))
            .WithName("ListResources");

        group.MapPost("/", async (SaveResourceRequest request, ResourceService service, CancellationToken ct) =>
            {
                var saved = await service.SaveAsync(request, ct);
                return TypedResults.Created($"/api/resources/{saved.Id}", saved);
            })
            .WithName("SaveResource")
            .WithSummary("Save a suggested resource, or a link of your own, against an objective.");

        group.MapPut("/{id:guid}/done", (Guid id, SetResourceDoneRequest request, ResourceService service, CancellationToken ct) =>
                service.SetDoneAsync(id, request.Done, ct))
            .WithName("SetResourceDone");

        group.MapDelete("/{id:guid}", async (Guid id, ResourceService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteResource");

        return api;
    }
}
