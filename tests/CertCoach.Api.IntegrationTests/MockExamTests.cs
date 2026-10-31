using System.Net;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Plans;
using CertCoach.Application.Practice;
using CertCoach.Application.Practice.Exams;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Practice;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class MockExamTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(StartTime.UtcDateTime);

    [Fact]
    public async Task An_exam_hides_answers_until_submitted_then_scores_each_domain_and_reschedules_the_questions()
    {
        var (client, certification) = await SetUpAsync();

        var exam = await SendAsync<MockExamView>(client, HttpMethod.Post, "/api/mock-exams", new StartMockExamRequest(certification.Id, QuestionCount: 6, DurationMinutes: 10));
        exam.Questions.Count.ShouldBe(6);
        exam.Questions.ShouldAllBe(q => q.CorrectOptions == null && q.Answer == null && q.Kind != PracticeItemKind.Flashcard);
        exam.Questions.Count(q => q.Domain == "Build").ShouldBe(4); // 60% of 6, rounded.
        exam.RemainingSeconds.ShouldBe(600);

        // Resuming returns the same exam rather than starting another.
        (await SendAsync<MockExamView>(client, HttpMethod.Post, "/api/mock-exams", new StartMockExamRequest(certification.Id))).Id.ShouldBe(exam.Id);

        // Every question's correct answer is option 0 here: answer two right, one wrong, leave the rest blank.
        await SaveAsync(client, exam.Id, exam.Questions[0].ItemId, [0]);
        await SaveAsync(client, exam.Id, exam.Questions[1].ItemId, [0]);
        await SaveAsync(client, exam.Id, exam.Questions[2].ItemId, [1]);
        (await GetAsync<MockExamView>(client, $"/api/mock-exams/{exam.Id}")).Questions[2].Selected.ShouldBe([1]);

        var result = await SendAsync<MockExamView>(client, HttpMethod.Post, $"/api/mock-exams/{exam.Id}/submit", new { });
        result.Status.ShouldBe(MockExamStatus.Submitted);
        result.Result.ShouldNotBeNull();
        (result.Result.Correct, result.Result.Total, result.Result.Percent).ShouldBe((2, 6, 33));
        result.Result.Domains.Sum(d => d.Total).ShouldBe(6);
        result.Questions[0].Correct.ShouldBe(true);
        result.Questions[0].CorrectOptions.ShouldBe([0]);
        result.Questions[5].Correct.ShouldBe(false);

        var items = await GetAsync<List<PracticeItemView>>(client, $"/api/practice/items?certificationId={certification.Id}");
        var inExam = exam.Questions.Select(q => q.ItemId).ToHashSet();
        items.Where(i => inExam.Contains(i.Id)).ShouldAllBe(i => i.DueOn == Today.AddDays(1));
        items.Where(i => !inExam.Contains(i.Id)).ShouldAllBe(i => i.DueOn == null);
        (await client.PutAsJsonAsync($"/api/mock-exams/{exam.Id}/answers/{exam.Questions[3].ItemId}", new SaveMockAnswerRequest([0]), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var history = await GetAsync<List<MockExamSummary>>(client, $"/api/mock-exams?certificationId={certification.Id}");
        history.ShouldHaveSingleItem().Percent.ShouldBe(33);
    }

    [Fact]
    public async Task An_exam_for_a_mock_exam_session_completes_it()
    {
        var (client, certification) = await SetUpAsync();
        var plan = await SendAsync<PlanView>(client, HttpMethod.Post, "/api/plans", new PlanRequest(
            certification.Id, null, Today.AddDays(28), 5m, Enum.GetValues<DayOfWeek>(), null));
        var session = plan.Sessions.First(s => s.Kind == SessionKind.MockExam);
        var learn = plan.Sessions.First(s => s.Kind == SessionKind.Learn);

        var exam = await SendAsync<MockExamView>(client, HttpMethod.Post, "/api/mock-exams", new StartMockExamRequest(null, session.Id, 5, 10));
        await SendAsync<MockExamView>(client, HttpMethod.Post, $"/api/mock-exams/{exam.Id}/submit", new { });

        var after = await GetAsync<PlanView>(client, $"/api/plans/{plan.Id}");
        after.Sessions.Single(s => s.Id == session.Id).Status.ShouldBe(SessionStatus.Completed);
        (await client.PostAsJsonAsync("/api/mock-exams", new StartMockExamRequest(null, learn.Id), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_few_questions_is_explained()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", Outline());

        var response = await client.PostAsJsonAsync("/api/mock-exams", new StartMockExamRequest(certification.Id), Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Generate more");
    }

    private static SaveCertificationRequest Outline() => new(
        "MOCK-" + Random.Shared.Next(100, 999), "Mock", "Contoso", null, null, CertificationStatus.Active, null,
        [new DomainDraft("Build", 60, 60, [new SkillAreaDraft("Apps", ["Build chat apps"])]),
         new DomainDraft("Operate", 40, 40, [new SkillAreaDraft("Ops", ["Monitor apps"])])]);

    /// <summary>A certification with 5 questions per domain (correct answer always option 0) and a flashcard.</summary>
    private async Task<(HttpClient, CertificationDetail)> SetUpAsync()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", Outline());
        foreach (var domain in certification.Domains)
        {
            var objective = domain.SkillAreas[0].Objectives[0].Id;
            for (var i = 0; i < 5; i++)
            {
                await SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", new CreatePracticeItemRequest(
                    certification.Id, objective, PracticeItemKind.SingleChoice, $"{domain.Name} question {i}?", ["Right", "Wrong", "Also wrong"], [0], "Because."));
            }

            await SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", new CreatePracticeItemRequest(
                certification.Id, objective, PracticeItemKind.Flashcard, $"{domain.Name} card", null, null, "Back"));
        }

        return (client, certification);
    }

    private static async Task SaveAsync(HttpClient client, Guid examId, Guid itemId, int[] selected) =>
        (await client.PutAsJsonAsync($"/api/mock-exams/{examId}/answers/{itemId}", new SaveMockAnswerRequest(selected), Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

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
