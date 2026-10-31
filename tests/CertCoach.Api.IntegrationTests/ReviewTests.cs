using System.Net;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Dashboard;
using CertCoach.Application.Practice;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Practice;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class ReviewTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(StartTime.UtcDateTime);

    [Fact]
    public async Task Answers_reschedule_items_and_hide_them_from_today()
    {
        var (client, certification) = await SetUpAsync();
        var objective = certification.Domains[0].SkillAreas[0].Objectives[0].Id;
        var question = await CreateAsync(client, new(certification.Id, objective, PracticeItemKind.SingleChoice, "Which merges rankings?", ["RRF", "BM25", "HNSW"], [0], "RRF fuses lists."));
        var card = await CreateAsync(client, new(certification.Id, objective, PracticeItemKind.Flashcard, "RRF constant?", null, null, "k = 60"));

        var queue = await GetAsync<ReviewSet>(client, $"/api/review?certificationId={certification.Id}");
        queue.NewAvailable.ShouldBe(2);
        queue.Cards.Count.ShouldBe(2);
        var questionCard = queue.Cards.Single(c => c.Id == question.Id);
        questionCard.Back.ShouldBeNull(); // No peeking at the answer.
        questionCard.ChooseCount.ShouldBe(1);
        questionCard.Domain.ShouldBe("Retrieval");
        queue.Cards.Single(c => c.Id == card.Id).Intervals.ShouldBe(new ReviewIntervals(1, 1, 1, 1));

        var wrong = await AnswerAsync(client, question.Id, new AnswerRequest([1], null));
        wrong.Correct.ShouldBe(false);
        wrong.CorrectOptions.ShouldBe([0]);
        wrong.Answer.ShouldBe("RRF fuses lists.");
        wrong.DueOn.ShouldBe(Today.AddDays(1));

        var graded = await AnswerAsync(client, card.Id, new AnswerRequest(null, ReviewGrade.Good));
        graded.Correct.ShouldBeNull();
        graded.IntervalDays.ShouldBe(1);

        var after = await GetAsync<ReviewSet>(client, $"/api/review?certificationId={certification.Id}");
        after.Cards.ShouldBeEmpty();
        after.ReviewedToday.ShouldBe(2);
        after.NewLeftToday.ShouldBe(ReviewService.NewPerDay - 2);
        (await GetAsync<DashboardView>(client, "/api/dashboard")).ReviewDue.ShouldBe(0);
    }

    [Fact]
    public async Task New_items_are_limited_per_day_and_suspended_ones_are_left_out()
    {
        var (client, certification) = await SetUpAsync();
        var items = new List<PracticeItemView>();
        for (var i = 0; i < ReviewService.NewPerDay + 3; i++)
        {
            items.Add(await CreateAsync(client, new(certification.Id, null, PracticeItemKind.Flashcard, $"Card {i}", null, null, "Back")));
        }

        await SendAsync<PracticeItemView>(client, HttpMethod.Put, $"/api/practice/items/{items[0].Id}/status", new SetPracticeItemStatusRequest(PracticeItemStatus.Suspended));

        var queue = await GetAsync<ReviewSet>(client, "/api/review");
        queue.NewAvailable.ShouldBe(ReviewService.NewPerDay + 2);
        queue.Cards.Count.ShouldBe(ReviewService.NewPerDay);
        queue.Cards.ShouldNotContain(c => c.Id == items[0].Id);
        (await GetAsync<DashboardView>(client, "/api/dashboard")).ReviewDue.ShouldBe(ReviewService.NewPerDay);
    }

    [Fact]
    public async Task A_quiz_covers_only_the_given_objectives_and_bad_answers_are_refused()
    {
        var (client, certification) = await SetUpAsync();
        var objectives = certification.Domains[0].SkillAreas[0].Objectives;
        var inQuiz = await CreateAsync(client, new(certification.Id, objectives[0].Id, PracticeItemKind.SingleChoice, "In?", ["A", "B", "C"], [2], ""));
        await CreateAsync(client, new(certification.Id, objectives[1].Id, PracticeItemKind.Flashcard, "Out?", null, null, "Back"));

        var quiz = await GetAsync<ReviewSet>(client, $"/api/review/quiz?objectiveIds={objectives[0].Id}");
        quiz.Cards.ShouldHaveSingleItem().Id.ShouldBe(inQuiz.Id);

        (await client.PostAsJsonAsync($"/api/review/items/{inQuiz.Id}/answer", new AnswerRequest(null, ReviewGrade.Good), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/review/items/{Guid.NewGuid()}/answer", new AnswerRequest([0], null), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AnswerAsync(client, inQuiz.Id, new AnswerRequest([2], null, ReviewSource.Quiz))).Correct.ShouldBe(true);
    }

    private async Task<(HttpClient, CertificationDetail)> SetUpAsync()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", new SaveCertificationRequest(
            "REV-" + Random.Shared.Next(100, 999), "Review", "Contoso", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Retrieval", 100, 100, [new SkillAreaDraft("Search", ["Merge rankings", "Chunk documents"])])]));
        return (client, certification);
    }

    private static Task<PracticeItemView> CreateAsync(HttpClient client, CreatePracticeItemRequest request) =>
        SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", request);

    private static Task<AnswerResult> AnswerAsync(HttpClient client, Guid id, AnswerRequest request) =>
        SendAsync<AnswerResult>(client, HttpMethod.Post, $"/api/review/items/{id}/answer", request);

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
        where T : class =>
        (await client.GetFromJsonAsync<T>(url, Json, Ct)).ShouldNotBeNull();

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object body)
        where T : class
    {
        var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) }, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct)).ShouldNotBeNull();
    }
}
