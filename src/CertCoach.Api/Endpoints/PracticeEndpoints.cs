using CertCoach.Application.Practice;

namespace CertCoach.Api.Endpoints;

internal static class PracticeEndpoints
{
    public static RouteGroupBuilder MapPracticeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/practice/items").WithTags("Practice");

        group.MapGet("/", (Guid certificationId, Guid? objectiveId, PracticeService service, CancellationToken ct) =>
                service.ListAsync(certificationId, objectiveId, ct))
            .WithName("ListPracticeItems")
            .WithSummary("A certification's practice questions and flashcards, optionally for one objective.");

        group.MapPost("/", async (CreatePracticeItemRequest request, PracticeService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created($"/api/practice/items/{created.Id}", created);
            })
            .WithName("CreatePracticeItem")
            .WithSummary("Write your own question or flashcard.");

        group.MapPut("/{id:guid}", (Guid id, UpdatePracticeItemRequest request, PracticeService service, CancellationToken ct) =>
                service.UpdateAsync(id, request, ct))
            .WithName("UpdatePracticeItem")
            .WithSummary("Edit an item. Its review schedule is kept.");

        group.MapPut("/{id:guid}/status", (Guid id, SetPracticeItemStatusRequest request, PracticeService service, CancellationToken ct) =>
                service.SetStatusAsync(id, request, ct))
            .WithName("SetPracticeItemStatus")
            .WithSummary("Suspend an item to leave it out of reviews and exams, or make it active again.");

        group.MapDelete("/{id:guid}", async (Guid id, PracticeService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeletePracticeItem");

        return api;
    }
}
