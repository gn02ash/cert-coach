using System.Net;
using System.Net.Http.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Practice;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Practice;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

public class PracticeItemTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveCertificationRequest Certification(params string[] objectives) => new(
        "PRAC-" + Random.Shared.Next(100, 999), "Practice", "Contoso", null, null, CertificationStatus.Active, null,
        [new DomainDraft("Build it", 100, 100, [new SkillAreaDraft("Area", objectives)])]);

    [Fact]
    public async Task Written_items_round_trip_and_can_be_edited_suspended_and_deleted()
    {
        var client = await factory.CreateUserClientAsync();
        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", Certification("Merge rankings"));
        var objective = certification.Domains[0].SkillAreas[0].Objectives[0];

        var question = await SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, objective.Id, PracticeItemKind.MultipleChoice, "Which two merge rankings?", ["RRF", "Borda count", "HNSW", "BM25"], [1, 0], "Both fuse ranked lists."));
        var card = await SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, null, PracticeItemKind.Flashcard, "RRF constant?", null, null, "k = 60"));

        question.ObjectiveText.ShouldBe("Merge rankings");
        question.CorrectOptions.ShouldBe([0, 1]);
        question.Origin.ShouldBe(PracticeItemOrigin.Manual);
        question.DueOn.ShouldBeNull();

        var edited = await SendAsync<PracticeItemView>(client, HttpMethod.Put, $"/api/practice/items/{card.Id}", new UpdatePracticeItemRequest("RRF k?", null, null, "60"));
        edited.Prompt.ShouldBe("RRF k?");
        (await SendAsync<PracticeItemView>(client, HttpMethod.Put, $"/api/practice/items/{card.Id}/status", new SetPracticeItemStatusRequest(PracticeItemStatus.Suspended)))
            .Status.ShouldBe(PracticeItemStatus.Suspended);

        (await ListAsync(client, certification.Id)).Select(i => i.Id).ShouldBe([question.Id, card.Id]);
        (await ListAsync(client, certification.Id, objective.Id)).ShouldHaveSingleItem().Id.ShouldBe(question.Id);

        (await client.DeleteAsync($"/api/practice/items/{card.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(client, certification.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Invalid_items_and_other_learners_items_are_refused()
    {
        var client = await factory.CreateUserClientAsync();
        var other = await factory.CreateUserClientAsync();
        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", Certification("Merge rankings"));
        var card = await SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, null, PracticeItemKind.Flashcard, "Front", null, null, "Back"));

        var invalid = await client.PostAsJsonAsync("/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, null, PracticeItemKind.SingleChoice, "Q?", ["A", "B", "C"], [0, 1], ""), Json, Ct);
        var foreignObjective = await client.PostAsJsonAsync("/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, Guid.NewGuid(), PracticeItemKind.Flashcard, "Front", null, null, "Back"), Json, Ct);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        foreignObjective.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await other.GetAsync($"/api/practice/items?certificationId={certification.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/practice/items/{card.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Items_follow_their_objective_when_the_outline_is_edited()
    {
        var client = await factory.CreateUserClientAsync();
        var request = Certification("Merge rankings", "Chunk documents");
        var certification = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", request);
        var objectives = certification.Domains[0].SkillAreas[0].Objectives;
        foreach (var objective in objectives)
        {
            await SendAsync<PracticeItemView>(client, HttpMethod.Post, "/api/practice/items", new CreatePracticeItemRequest(
                certification.Id, objective.Id, PracticeItemKind.Flashcard, objective.Text, null, null, "Back"));
        }

        // "Chunk documents" is removed; "Merge rankings" moves to a new skill area.
        var edited = request with
        {
            Outline = [new DomainDraft("Build it", 100, 100, [new SkillAreaDraft("Other", ["Embed text"]), new SkillAreaDraft("Area", ["Merge  rankings"])])],
        };
        var updated = await SendAsync<CertificationDetail>(client, HttpMethod.Put, $"/api/certifications/{certification.Id}", edited);

        var items = await ListAsync(client, certification.Id);
        var merge = items.Single(i => i.Prompt == "Merge rankings");
        merge.ObjectiveId.ShouldBe(updated.Domains[0].SkillAreas[1].Objectives[0].Id);
        merge.ObjectiveText.ShouldBe("Merge  rankings");
        items.Single(i => i.Prompt == "Chunk documents").ObjectiveId.ShouldBeNull();
    }

    private static async Task<List<PracticeItemView>> ListAsync(HttpClient client, Guid certificationId, Guid? objectiveId = null) =>
        (await client.GetFromJsonAsync<List<PracticeItemView>>(
            $"/api/practice/items?certificationId={certificationId}" + (objectiveId is null ? "" : $"&objectiveId={objectiveId}"), Json, Ct))
        .ShouldNotBeNull();

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
