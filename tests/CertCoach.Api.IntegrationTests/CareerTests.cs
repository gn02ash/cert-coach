using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CertCoach.Application.Ai;
using CertCoach.Application.Careers;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Domain.Certifications;
using CertCoach.Infrastructure.Tests.Documents;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Api.IntegrationTests;

public class CareerTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SaveProfileRequest Developer = new(
        ["C#", "ASP.NET Core", "Python", "Azure", "Docker", "Postgres", "RAG", "prompt engineering", "CI/CD"], ["developer"], ["AZ-900"], 6, "AI engineer");

    [Fact]
    public async Task A_cv_is_read_into_a_draft_and_only_the_saved_profile_is_kept()
    {
        var client = await factory.CreateUserClientAsync();
        var cv = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "sample-cv.md"), Ct);

        var draft = await SendAsync<ProfileDraft>(client, HttpMethod.Post, "/api/career/cv/text", new ReadCvTextRequest(cv));

        draft.Method.ShouldBe(CvReadMethod.Offline);
        draft.Skills.ShouldContain("asp.net core");
        draft.HeldCertifications.ShouldBe(["AZ-900"]);
        draft.YearsExperience.ShouldBe(6);
        (await GetAsync<ProfileView>(client, "/api/career/profile")).Exists.ShouldBeFalse();

        var saved = await SendAsync<ProfileView>(client, HttpMethod.Put, "/api/career/profile",
            new SaveProfileRequest(draft.Skills, draft.Roles, draft.HeldCertifications, draft.YearsExperience, " AI engineer "));
        saved.Exists.ShouldBeTrue();
        saved.TargetRole.ShouldBe("AI engineer");

        (await client.DeleteAsync("/api/career/profile", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<ProfileView>(client, "/api/career/profile")).Exists.ShouldBeFalse();
        (await client.GetAsync("/api/career/recommendations", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_pdf_cv_is_read_and_other_files_are_refused()
    {
        var client = await factory.CreateUserClientAsync();
        var pdf = new TestPdf()
            .Page(("Sam Example", TitleSize), ("Experience", HeadingSize),
                ("Data engineer with 4 years of experience building pipelines in Python and Spark on AWS, with Airflow and Snowflake.", BodySize))
            .Build();

        var draft = await UploadAsync<ProfileDraft>(client, pdf);
        var notPdf = await UploadRawAsync(client, "plain text, not a PDF"u8.ToArray());

        draft.Skills.ShouldBeSubsetOf(["python", "spark", "aws", "airflow", "snowflake"]);
        draft.Skills.Count.ShouldBe(5);
        draft.Roles.ShouldBe(["data engineer"]);
        draft.YearsExperience.ShouldBe(4);
        notPdf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/career/cv/text", new ReadCvTextRequest("too short"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recommendations_rank_the_catalogue_for_the_profile_and_belong_to_the_learner()
    {
        var client = await factory.CreateUserClientAsync();
        await SendAsync<ProfileView>(client, HttpMethod.Put, "/api/career/profile", Developer);
        var added = await SendAsync<CertificationDetail>(client, HttpMethod.Post, "/api/certifications", new SaveCertificationRequest(
            "ai-103", "Developing AI Apps and Agents on Azure", "Microsoft", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Build", 100, 100, [new SkillAreaDraft("Apps", ["Build agents"])])]));

        var recommendations = await GetAsync<RecommendationsView>(client, "/api/career/recommendations");

        recommendations.Items.Count.ShouldBe(CareerService.MaxRecommendations);
        recommendations.CatalogueSize.ShouldBeGreaterThan(40);
        recommendations.Items.Take(3).ShouldContain(i => i.Code == "AI-200");
        var ai200 = recommendations.Items.Single(i => i.Code == "AI-200");
        ai200.NextStepAfter.ShouldBe("AZ-900");
        ai200.MatchesTargetRole.ShouldBeTrue();
        ai200.Url.Host.ShouldBe("learn.microsoft.com");
        recommendations.Items.ShouldNotContain(i => i.Code == "AZ-900");
        recommendations.Items.Single(i => i.Code == "AI-103").AddedCertificationId.ShouldBe(added.Id);
        recommendations.Items.Where(i => i.Code != "AI-103").ShouldAllBe(i => i.AddedCertificationId == null);

        // Offline there is no advice, and another learner has no profile.
        (await client.PostAsync("/api/career/recommendations/advice", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync<ProfileView>(await factory.CreateUserClientAsync(), "/api/career/profile")).Exists.ShouldBeFalse();
        (await client.PutAsJsonAsync("/api/career/profile", Developer with { YearsExperience = 99 }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task With_a_chat_model_the_cv_is_read_by_it_and_advice_keeps_to_the_candidates()
    {
        var chat = new ScriptedChatClient(prompt => prompt.Contains("<cv>", StringComparison.Ordinal)
            ? """{"skills":["Azure","C#","RAG"],"roles":["developer"],"heldCertifications":[],"yearsExperience":5}"""
            : """{"overview":"Go for AI.","items":[{"code":"AI-103","rationale":"You already build RAG."},{"code":"NOPE-1","rationale":"x"}]}""");
        var withChat = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<IChatClient>(chat);
            services.AddSingleton(new AiCapabilities("Fake", "fake-model", "Offline", "offline-hashing-v2", ChatAvailable: true, SemanticSearch: false));
        }));
        var user = await factory.CreateUserClientAsync();
        var client = withChat.CreateClient();
        foreach (var header in user.DefaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        var draft = await SendAsync<ProfileDraft>(client, HttpMethod.Post, "/api/career/cv/text",
            new ReadCvTextRequest("Developer for five years on Azure with C# and RAG. Holds AZ-900 since 2021."));
        await SendAsync<ProfileView>(client, HttpMethod.Put, "/api/career/profile",
            new SaveProfileRequest(draft.Skills, draft.Roles, draft.HeldCertifications, draft.YearsExperience, "AI engineer"));
        var advice = await SendAsync<AdviceView>(client, HttpMethod.Post, "/api/career/recommendations/advice", new { });

        draft.Method.ShouldBe(CvReadMethod.Ai);
        draft.Skills.ShouldBe(["azure", "c#", "rag"]);
        draft.HeldCertifications.ShouldBe(["AZ-900"]); // The model missed it; the exact code match didn't.
        advice.Overview.ShouldBe("Go for AI.");
        advice.Items.ShouldHaveSingleItem().ShouldBe(new AdviceItem("AI-103", "You already build RAG."));
    }

    private static Task<T> UploadAsync<T>(HttpClient client, byte[] file)
        where T : class =>
        ReadAsync<T>(UploadRawAsync(client, file));

    private static Task<HttpResponseMessage> UploadRawAsync(HttpClient client, byte[] file)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "cv.pdf");
        return client.PostAsync("/api/career/cv/file", form, Ct);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
        where T : class =>
        (await client.GetFromJsonAsync<T>(url, Json, Ct)).ShouldNotBeNull();

    private static Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object body)
        where T : class =>
        ReadAsync<T>(client.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) }, Ct));

    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> request)
        where T : class
    {
        var response = await request;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{response.RequestMessage!.RequestUri} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct)).ShouldNotBeNull();
    }

    private sealed class ScriptedChatClient(Func<string, string> answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer(messages.Last().Text))) { FinishReason = ChatFinishReason.Stop });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
