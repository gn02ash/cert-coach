using CertCoach.Application.Ai;
using CertCoach.Application.Certifications.Outlines;
using CertCoach.Domain.Common;
using Microsoft.Extensions.AI;

namespace CertCoach.Application.Tests.Certifications;

public class AiOutlineParserTests
{
    private static readonly AiCapabilities Online = new("Fake", "fake", "Offline", "offline", ChatAvailable: true, SemanticSearch: false);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Json = """
        {"domains":[
          {"name":" Plan the solution ","weightMinPercent":25,"weightMaxPercent":20,"skillAreas":[
            {"name":"Choose services","objectives":["Pick a model "," ","Estimate cost"]},
            {"name":"Empty area","objectives":[]}]},
          {"name":"Build it","weightMinPercent":75,"weightMaxPercent":80,"skillAreas":[
            {"name":"","objectives":["Write prompts"]}]},
          {"name":"Unweighted","weightMinPercent":0,"weightMaxPercent":0,"skillAreas":[
            {"name":"Area","objectives":["Something"]}]}
        ]}
        """;

    [Fact]
    public async Task Maps_the_models_json_to_a_checked_draft()
    {
        var chat = new FakeChatClient(Json);

        var draft = await new AiOutlineParser(chat, Online).ParseAsync("pasted outline", Ct);

        draft.Domains.Select(d => (d.Name, d.WeightMinPercent, d.WeightMaxPercent)).ShouldBe(
        [
            ("Plan the solution", 20, 25),
            ("Build it", 75, 80),
            ("Unweighted", 0, 0),
        ]);
        draft.Domains[0].SkillAreas.ShouldHaveSingleItem().Objectives.ShouldBe(["Pick a model", "Estimate cost"]);
        draft.Domains[1].SkillAreas.Single().Name.ShouldBe(OutlineParser.DefaultSkillAreaName);
        draft.Warnings.ShouldContain("Domain \"Unweighted\" has no weight in the text. Fill it in.");
        draft.Warnings[^1].ShouldBe(AiOutlineParser.ReviewWarning);
    }

    [Fact]
    public async Task Asks_for_json_matching_a_schema_with_the_text_marked_as_data()
    {
        var chat = new FakeChatClient(Json);

        await new AiOutlineParser(chat, Online).ParseAsync("Plan (10%)\n- Do it", Ct);

        chat.Options!.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>().Schema.ShouldNotBeNull();
        chat.Messages![0].Role.ShouldBe(ChatRole.System);
        chat.Messages[^1].Text.ShouldBe("<outline>\nPlan (10%)\n- Do it\n</outline>");
    }

    [Fact]
    public async Task Needs_a_chat_provider()
    {
        var offline = Online with { ChatAvailable = false };

        await Should.ThrowAsync<DomainException>(() => new AiOutlineParser(new FakeChatClient(Json), offline).ParseAsync("x", Ct));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"domains\":null}")]
    public async Task An_unreadable_answer_is_reported_not_thrown_as_a_server_error(string answer)
    {
        var parser = new AiOutlineParser(new FakeChatClient(answer), Online);

        var error = await Should.ThrowAsync<DomainException>(() => parser.ParseAsync("x", Ct));
        error.Message.ShouldContain("could not be read");
    }

    [Fact]
    public async Task Rejects_text_too_long_to_be_just_an_outline()
    {
        var parser = new AiOutlineParser(new FakeChatClient(Json), Online);

        await Should.ThrowAsync<DomainException>(() => parser.ParseAsync(new string('x', AiOutlineParser.MaxInputLength + 1), Ct));
    }

    private sealed class FakeChatClient(string answer) : IChatClient
    {
        public List<ChatMessage>? Messages { get; private set; }
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Messages = messages.ToList();
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { FinishReason = ChatFinishReason.Stop });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
