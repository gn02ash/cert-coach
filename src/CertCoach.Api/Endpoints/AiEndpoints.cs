using CertCoach.Application.Ai;

namespace CertCoach.Api.Endpoints;

internal static class AiEndpoints
{
    public static RouteGroupBuilder MapAiEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/ai/status", (AiCapabilities capabilities) => capabilities)
            .WithName("GetAiStatus")
            .WithTags("AI")
            .WithSummary("Which chat and embedding providers are active; offline mode has no chat model.");

        return api;
    }
}
