using CertCoach.Application.Practice;
using CertCoach.Application.Practice.Exams;
using CertCoach.Application.Practice.Generation;
using CertCoach.Application.Practice.Readiness;

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

        var review = api.MapGroup("/review").WithTags("Review");

        review.MapGet("/", (Guid? certificationId, ReviewService service, CancellationToken ct) => service.GetQueueAsync(certificationId, ct))
            .WithName("GetReviewQueue")
            .WithSummary("Today's items: everything due, then new items up to the daily limit.");

        review.MapGet("/quiz", (Guid[] objectiveIds, ReviewService service, CancellationToken ct) => service.GetQuizAsync(objectiveIds, ct))
            .WithName("GetQuiz")
            .WithSummary("A short quiz on the given objectives, such as a study session's.");

        review.MapPost("/items/{id:guid}/answer", (Guid id, AnswerRequest request, ReviewService service, CancellationToken ct) =>
                service.AnswerAsync(id, request, ct))
            .WithName("AnswerItem")
            .WithSummary("Answer a question or grade a flashcard. Returns the answer and when the item comes back.");

        api.MapGet("/readiness/{certificationId:guid}", (Guid certificationId, ReadinessService service, CancellationToken ct) =>
                service.GetAsync(certificationId, ct))
            .WithName("GetReadiness")
            .WithTags("Review")
            .WithSummary("An estimate of readiness per exam domain: objectives recalled × recent accuracy, from your practice answers.")
            .WithDescription("A domain needs 10 question answers in the last 90 days for a score. Answers lose half their weight every 14 days, and accuracy is shrunk toward 50% so a few answers can't read as certain.");

        var exams = api.MapGroup("/mock-exams").WithTags("Mock exams");

        exams.MapPost("/", (StartMockExamRequest request, MockExamService service, CancellationToken ct) => service.StartAsync(request, ct))
            .WithName("StartMockExam")
            .WithSummary("Start a timed exam with questions in proportion to the domain weights, or resume the one in progress.");

        exams.MapGet("/", (Guid certificationId, MockExamService service, CancellationToken ct) => service.ListAsync(certificationId, ct))
            .WithName("ListMockExams");

        exams.MapGet("/{id:guid}", (Guid id, MockExamService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetMockExam")
            .WithSummary("The exam. Answers and results appear once it is submitted; an exam past its time is submitted when read.");

        exams.MapPut("/{id:guid}/answers/{itemId:guid}", async (Guid id, Guid itemId, SaveMockAnswerRequest request, MockExamService service, CancellationToken ct) =>
            {
                await service.SaveAnswerAsync(id, itemId, request, ct);
                return TypedResults.NoContent();
            })
            .WithName("SaveMockAnswer");

        exams.MapPost("/{id:guid}/submit", (Guid id, MockExamService service, CancellationToken ct) => service.SubmitAsync(id, ct))
            .WithName("SubmitMockExam");

        var runs = api.MapGroup("/practice/generations").WithTags("Practice");

        runs.MapPost("/", async (GenerateItemsRequest request, GenerationService service, CancellationToken ct) =>
            {
                var run = await service.StartAsync(request, ct);
                return TypedResults.Accepted($"/api/practice/generations/{run.Id}", run);
            })
            .WithName("StartGeneration")
            .WithSummary("Write questions and flashcards in the background for a certification, a domain or an objective.")
            .WithDescription("With a chat provider, items are written from your documents with citations, or from the model's knowledge when no document covers an objective. Offline, they are fill-in-the-blank items from your documents.");

        runs.MapGet("/", (Guid certificationId, GenerationService service, CancellationToken ct) => service.ListAsync(certificationId, ct))
            .WithName("ListGenerations")
            .WithSummary("The most recent generation runs for a certification, newest first.");

        runs.MapGet("/{id:guid}", (Guid id, GenerationService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetGeneration");

        runs.MapPost("/{id:guid}/cancel", (Guid id, GenerationService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithName("CancelGeneration")
            .WithSummary("Stop a run before its next objective. Items already written are kept.");

        return api;
    }
}
