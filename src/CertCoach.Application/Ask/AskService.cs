using System.Runtime.CompilerServices;
using System.Text;
using CertCoach.Application.Ai;
using CertCoach.Application.Search;
using CertCoach.Domain.Common;
using Microsoft.Extensions.AI;

namespace CertCoach.Application.Ask;

/// <summary>
/// Answers a question from the learner's documents: retrieve, decide whether anything is close enough to
/// answer from, then stream either a model-written answer with citations or, offline, quoted sentences.
/// </summary>
public sealed class AskService(SearchService search, IChatClient chat, AiCapabilities capabilities)
{
    public const int MaxSources = 6;
    public const int MaxQuestionLength = 2000;

    /// <summary>
    /// The best passage must be at least this similar to the question, or the answer is "not in your materials"
    /// without calling a model. Unrelated text scores about 0.1 with text-embedding-3; the eval tool measures this.
    /// </summary>
    public const double MinSimilarity = 0.3;

    /// <summary>The offline hashing vectors only measure shared words, which gives lower scores.</summary>
    public const double MinSimilarityOffline = 0.1;

    /// <summary>Questions this short are usually follow-ups ("and the cost?"), so the previous question joins the search.</summary>
    private const int FollowUpMaxWords = 8;

    private const int MaxOutputTokens = 4096;

    public async IAsyncEnumerable<AskEvent> AskAsync(AskRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var question = request.Question?.Trim() ?? "";
        if (question.Length == 0)
        {
            throw new DomainException("Ask a question.");
        }

        if (question.Length > MaxQuestionLength)
        {
            throw new DomainException($"Keep the question under {MaxQuestionLength} characters.");
        }

        var sources = await RetrieveAsync(question, request, ct);
        yield return new AskSourcesEvent(sources);

        if (sources.Count == 0)
        {
            yield return new AskDeltaEvent(
                "I couldn't find this in your materials. Try other words, or upload a document that covers it.");
            yield return new AskDoneEvent(AnswerKind.NotCovered, [], null);
            yield break;
        }

        if (!capabilities.ChatAvailable)
        {
            var quoted = QuotedAnswer.Build(question, sources);
            yield return new AskDeltaEvent(quoted);
            yield return new AskDoneEvent(
                AnswerKind.Quoted,
                Citations.Cited(quoted, sources.Count),
                "Offline mode: these are quotes from your documents, not a written answer. Configure a chat provider for AI answers.");
            yield break;
        }

        var answer = new StringBuilder();
        ChatFinishReason? finish = null;
        var options = new ChatOptions { MaxOutputTokens = MaxOutputTokens };

        await foreach (var update in chat.GetStreamingResponseAsync(AnswerPrompt.Build(question, sources, request.History), options, ct))
        {
            finish = update.FinishReason ?? finish;
            if (update.Text is { Length: > 0 } text)
            {
                answer.Append(text);
                yield return new AskDeltaEvent(text);
            }
        }

        var cited = Citations.Cited(answer.ToString(), sources.Count);
        if (finish == ChatFinishReason.ContentFilter)
        {
            yield return new AskDoneEvent(AnswerKind.Declined, cited, "The model declined to answer this question.");
        }
        else if (finish == ChatFinishReason.Length)
        {
            yield return new AskDoneEvent(AnswerKind.Ai, cited, "The answer was cut off because it got too long.");
        }
        else
        {
            yield return new AskDoneEvent(AnswerKind.Ai, cited, cited.Count == 0 ? "This answer cites no sources; check it against your documents." : null);
        }
    }

    private async Task<List<AskSource>> RetrieveAsync(string question, AskRequest request, CancellationToken ct)
    {
        var query = question;
        var previous = request.History?.LastOrDefault(t => t.Role == AskRole.User)?.Text;
        if (previous is not null && question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < FollowUpMaxWords)
        {
            query = $"{previous}\n{question}";
        }

        var result = await search.SearchAsync(new SearchRequest(query, request.CertificationId, MaxSources), ct);
        var threshold = result.Semantic ? MinSimilarity : MinSimilarityOffline;
        if (result.Hits.Max(h => h.Similarity) is not { } best || best < threshold)
        {
            return [];
        }

        return result.Hits
            .Select((h, i) => new AskSource(i + 1, h.ChunkId, h.DocumentId, h.DocumentTitle, h.HeadingPath, h.PageStart, h.PageEnd, h.Text))
            .ToList();
    }
}
