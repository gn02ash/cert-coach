using System.Net;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Practice;
using CertCoach.Application.Practice.Readiness;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Practice;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class ReadinessTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Answers_build_a_score_for_their_domain_only()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await PostAsync<CertificationDetail>(client, "/api/certifications", new SaveCertificationRequest(
            "READY-" + Random.Shared.Next(100, 999), "Ready", "Contoso", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Build", 60, 60, [new SkillAreaDraft("Apps", ["Build chat apps"])]),
             new DomainDraft("Operate", 40, 40, [new SkillAreaDraft("Ops", ["Monitor apps"])])]));
        var objective = certification.Domains[0].SkillAreas[0].Objectives[0].Id;

        for (var i = 0; i < ReadinessCalculator.MinAnswers; i++)
        {
            var item = await PostAsync<PracticeItemView>(client, "/api/practice/items", new CreatePracticeItemRequest(
                certification.Id, objective, PracticeItemKind.SingleChoice, $"Question {i}?", ["Right", "Wrong", "Other"], [0], ""));
            await PostAsync<AnswerResult>(client, $"/api/review/items/{item.Id}/answer", new AnswerRequest([0], null));
        }

        var report = (await client.GetFromJsonAsync<ReadinessReport>($"/api/readiness/{certification.Id}", Json, Ct)).ShouldNotBeNull();

        var build = report.Domains.Single(d => d.Name == "Build");
        (build.Score, build.Coverage, build.Accuracy, build.Answers).ShouldBe((86, 100, 100, 10));
        report.Domains.Single(d => d.Name == "Operate").Score.ShouldBeNull();
        report.Overall.ShouldBeNull();
        report.DomainsScored.ShouldBe(1);

        var other = await factory.CreateUserClientAsync();
        (await other.GetAsync($"/api/readiness/{certification.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

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
