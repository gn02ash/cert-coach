using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CertCoach.Application.Certifications;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Application.Practice;
using CertCoach.Application.Users;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Practice;
using static CertCoach.Api.IntegrationTests.CertCoachApiFactory;

namespace CertCoach.Api.IntegrationTests;

/// <summary>The MCP endpoint over real HTTP: token sign-in, the tool list, and tools acting as the token's owner.</summary>
public class McpTests(CertCoachApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_a_valid_token_opens_the_mcp_endpoint_and_a_token_opens_nothing_else()
    {
        var browser = await factory.CreateUserClientAsync();
        var created = await PostAsync<CreatedApiToken>(browser, "/api/tokens", new CreateApiTokenRequest("Claude Code", 30));
        created.Token.ShouldStartWith("cc_pat_");
        created.Details.Prefix.ShouldBe(created.Token[..11]);

        (await CallRawAsync(factory.CreateClient(), null, "tools/list")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallRawAsync(factory.CreateClient(), "cc_pat_not-a-real-token", "tools/list")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The browser session is not accepted on /mcp, and the token is not accepted on the browser API.
        (await CallRawAsync(browser, null, "tools/list")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var withToken = factory.CreateClient();
        withToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        (await withToken.GetAsync("/api/tokens", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var tools = await CallAsync(created.Token, "tools/list");
        tools.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ShouldBe(
            ["list_certifications", "get_today_session", "get_readiness", "search_materials", "quiz_me", "answer_question", "complete_session"],
            ignoreOrder: true);

        var listed = (await browser.GetFromJsonAsync<List<ApiTokenView>>("/api/tokens", Json, Ct)).ShouldNotBeNull().ShouldHaveSingleItem();
        listed.LastUsedAt.ShouldNotBeNull();

        (await browser.DeleteAsync($"/api/tokens/{created.Details.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CallRawAsync(factory.CreateClient(), created.Token, "tools/list")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await browser.GetFromJsonAsync<List<ApiTokenView>>("/api/tokens", Json, Ct)).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task Tools_quiz_the_tokens_owner_record_answers_and_see_no_one_elses_data()
    {
        var browser = await factory.CreateUserClientAsync();
        var token = (await PostAsync<CreatedApiToken>(browser, "/api/tokens", new CreateApiTokenRequest("Assistant"))).Token;
        var certification = await PostAsync<CertificationDetail>(browser, "/api/certifications", new SaveCertificationRequest(
            "MCP-101", "MCP", "Contoso", null, null, CertificationStatus.Active, null,
            [new DomainDraft("Build", 100, 100, [new SkillAreaDraft("Apps", ["Build chat apps"])])]));
        var objective = certification.Domains[0].SkillAreas[0].Objectives[0].Id;
        await PostAsync<PracticeItemView>(browser, "/api/practice/items", new CreatePracticeItemRequest(
            certification.Id, objective, PracticeItemKind.SingleChoice, "Which merges rankings?", ["RRF", "BM25", "HNSW"], [0], "RRF fuses lists."));

        (await ToolAsync(token, "list_certifications", new { })).ShouldContain("MCP-101");
        (await ToolAsync(token, "get_today_session", new { })).ShouldContain("\"practiceItemsDue\":1");

        var quiz = await ToolAsync(token, "quiz_me", new { examCode = "mcp-101", count = 3 });
        quiz.ShouldContain("Which merges rankings?");
        quiz.ShouldNotContain("RRF fuses lists."); // No answers before the learner has answered.
        var itemId = JsonDocument.Parse(quiz).RootElement.GetProperty("items")[0].GetProperty("itemId").GetString();

        var answer = await ToolAsync(token, "answer_question", new { itemId, selectedOptions = (int[])[1] });
        answer.ShouldContain("\"correct\":false");
        answer.ShouldContain("RRF fuses lists.");
        (await browser.GetFromJsonAsync<ReviewSet>($"/api/review?certificationId={certification.Id}", Json, Ct))!.ReviewedToday.ShouldBe(1);

        // Errors come back as readable messages, and another learner's token can't reach this learner's item.
        (await ToolAsync(token, "get_readiness", new { examCode = "NOPE-1" }, expectError: true)).ShouldContain("MCP-101");
        var otherBrowser = await factory.CreateUserClientAsync();
        var otherToken = (await PostAsync<CreatedApiToken>(otherBrowser, "/api/tokens", new CreateApiTokenRequest("Other"))).Token;
        (await ToolAsync(otherToken, "answer_question", new { itemId, selectedOptions = (int[])[0] }, expectError: true)).ShouldContain("not found");
        (await ToolAsync(otherToken, "list_certifications", new { })).ShouldNotContain("MCP-101");
    }

    [Fact]
    public async Task Tokens_are_limited_named_and_only_their_hash_is_stored()
    {
        var browser = await factory.CreateUserClientAsync();

        (await browser.PostAsJsonAsync("/api/tokens", new CreateApiTokenRequest(" "), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await browser.PostAsJsonAsync("/api/tokens", new CreateApiTokenRequest("Too long", 5000), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        for (var i = 0; i < ApiTokenService.MaxActiveTokens; i++)
        {
            await PostAsync<CreatedApiToken>(browser, "/api/tokens", new CreateApiTokenRequest($"Token {i}"));
        }

        (await browser.PostAsJsonAsync("/api/tokens", new CreateApiTokenRequest("One too many"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var list = await browser.GetStringAsync("/api/tokens", Ct);
        list.ShouldNotContain("tokenHash", Case.Insensitive);
    }

    private async Task<string> ToolAsync(string token, string name, object arguments, bool expectError = false)
    {
        var result = await CallAsync(token, "tools/call", new { name, arguments });
        var isError = result.TryGetProperty("isError", out var e) && e.GetBoolean();
        var text = string.Concat(result.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString()));
        isError.ShouldBe(expectError, text);
        return text;
    }

    private async Task<JsonElement> CallAsync(string token, string method, object? parameters = null)
    {
        var response = await CallRawAsync(factory.CreateClient(), token, method, parameters);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        // The reply is plain JSON or a one-message event stream.
        var json = body.TrimStart().StartsWith('{') ? body : body.Split('\n').First(l => l.StartsWith("data:", StringComparison.Ordinal))["data:".Length..];
        var message = JsonDocument.Parse(json).RootElement;
        message.TryGetProperty("error", out var error).ShouldBeFalse(error.ToString());
        return message.GetProperty("result");
    }

    private static Task<HttpResponseMessage> CallRawAsync(HttpClient client, string? token, string method, object? parameters = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client.SendAsync(request, Ct);
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
