using System.Net.Http.Json;
using CertCoach.Application.Activity;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Practice;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Practice;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class ActivityTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(StartTime.UtcDateTime);

    [Fact]
    public async Task Answering_an_item_starts_a_streak_and_shows_on_todays_square()
    {
        var client = await factory.CreateUserClientAsync();
        var before = await GetAsync(client);
        before.Streak.ShouldBe(new Streak(0, 0, false));

        var certification = await PostAsync<CertificationDetail>(client, "/api/certifications", new SaveCertificationRequest(
            "ACT-" + Random.Shared.Next(100, 999), "Activity", "Contoso", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Build", 100, 100, [new SkillAreaDraft("Apps", ["Build chat apps"])])]));
        var card = await PostAsync<PracticeItemView>(client, "/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, null, PracticeItemKind.Flashcard, "Front", null, null, "Back"));
        await PostAsync<AnswerResult>(client, $"/api/review/items/{card.Id}/answer", new AnswerRequest(null, ReviewGrade.Good));

        var after = await GetAsync(client);

        after.Today.ShouldBe(Today);
        after.Streak.ShouldBe(new Streak(1, 1, true));
        after.ReviewsThisWeek.ShouldBe(1);
        after.Days[^1].ShouldBe(new ActivityDay(Today, 1, 0));
        after.Days[0].Date.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        after.Days.Count.ShouldBeInRange((7 * (ActivityService.DefaultWeeks - 1)) + 1, 7 * ActivityService.DefaultWeeks);

        // Another learner's activity is their own.
        (await GetAsync(await factory.CreateUserClientAsync())).Streak.Current.ShouldBe(0);
    }

    private static async Task<ActivityView> GetAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ActivityView>("/api/activity", Json, Ct)).ShouldNotBeNull();

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
        where T : class
    {
        var response = await client.PostAsJsonAsync(url, body, Json, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct)).ShouldNotBeNull();
    }
}
