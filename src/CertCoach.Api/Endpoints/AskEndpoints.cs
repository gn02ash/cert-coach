using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using CertCoach.Application.Ask;

namespace CertCoach.Api.Endpoints;

internal static class AskEndpoints
{
    public static RouteGroupBuilder MapAskEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/ask", (AskRequest request, AskService service, CancellationToken ct) =>
                TypedResults.ServerSentEvents(ToSse(service.AskAsync(request, ct), ct)))
            .WithName("Ask")
            .WithTags("Ask")
            .WithSummary("Answer a question from your documents, streamed as server-sent events: sources, then deltas, then done.");

        return api;
    }

    /// <summary>Each event goes out under its type name; the payload is typed as object so its own properties are serialized.</summary>
    private static async IAsyncEnumerable<SseItem<object>> ToSse(
        IAsyncEnumerable<AskEvent> events, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var e in events.WithCancellation(ct))
        {
            yield return new SseItem<object>(e, e.Type);
        }
    }
}
