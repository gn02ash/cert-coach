using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Application.Ask;
using CertCoach.Application.Search;
using CertCoach.Domain.Common;
using CertCoach.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Practice.Generation;

/// <param name="HadMaterial">Whether the learner's documents covered the objective.</param>
public sealed record ObjectiveResult(int Created, int Rejected, bool HadMaterial);

/// <summary>
/// Writes items for one objective and adds the valid ones to the context; the caller saves them together with
/// the job's progress. Passages come from the same hybrid search and relevance gate as Ask.
/// </summary>
public sealed class ItemGenerator(
    ICertCoachDbContext db, ICurrentUser user, SearchService search, AiItemWriter writer, AiCapabilities capabilities, UserClock clock)
{
    public const int PassagesPerObjective = 4;

    public async Task<ObjectiveResult> GenerateAsync(ObjectiveContext objective, int count, CancellationToken ct)
    {
        var passages = await PassagesAsync(objective, ct);
        IReadOnlyList<ItemDraft> drafts = capabilities.ChatAvailable
            ? await writer.WriteAsync(objective, passages, count, ct)
            : passages.Count > 0 ? ClozeWriter.Write(objective, passages, count) : [];

        var origin = passages.Count > 0 ? PracticeItemOrigin.Documents : PracticeItemOrigin.ModelKnowledge;

        // Across the certification: passages often cover several objectives, and the same item under two is a duplicate.
        var existing = await db.PracticeItems
            .Where(i => i.OwnerId == user.UserId && i.CertificationId == objective.CertificationId)
            .Select(i => new { i.Fingerprint, i.Sources })
            .ToListAsync(ct);
        var seen = existing.Select(i => i.Fingerprint).ToHashSet();

        // Offline items are one sentence each: blanking another word of a used sentence only repeats it.
        var usedSentences = capabilities.ChatAvailable
            ? null
            : existing.SelectMany(i => i.Sources).Select(s => s.Quote).OfType<string>().Select(Collapse).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (created, rejected) = (0, 0);
        foreach (var draft in drafts.Take(count))
        {
            if (ToItem(draft, objective, passages, origin, user.UserId, clock.Now) is { } item
                && seen.Add(item.Fingerprint)
                && (usedSentences is null || draft.Quote is null || usedSentences.Add(Collapse(draft.Quote))))
            {
                db.PracticeItems.Add(item);
                created++;
            }
            else
            {
                rejected++;
            }
        }

        return new ObjectiveResult(created, rejected, passages.Count > 0);
    }

    private async Task<IReadOnlyList<AskSource>> PassagesAsync(ObjectiveContext objective, CancellationToken ct)
    {
        var result = await search.SearchAsync(new SearchRequest(objective.Objective, objective.CertificationId, PassagesPerObjective), ct);
        var threshold = result.Semantic ? AskService.MinSimilarity : AskService.MinSimilarityOffline;
        if (result.Hits.Max(h => h.Similarity) is not { } best || best < threshold)
        {
            return [];
        }

        return result.Hits
            .Select((h, i) => new AskSource(i + 1, h.ChunkId, h.DocumentId, h.DocumentTitle, h.HeadingPath, h.PageStart, h.PageEnd, h.Text))
            .ToList();
    }

    /// <summary>
    /// A valid item, or null. A document-based item must cite at least one real passage; its quote is kept only
    /// where it appears in the passage, so the learner can trust it.
    /// </summary>
    internal static PracticeItem? ToItem(
        ItemDraft draft, ObjectiveContext objective, IReadOnlyList<AskSource> passages, PracticeItemOrigin origin, Guid ownerId, DateTimeOffset now)
    {
        List<ItemSource> sources = [];
        if (origin == PracticeItemOrigin.Documents)
        {
            var cited = draft.SourceNumbers.Distinct().Where(n => n >= 1 && n <= passages.Count).Select(n => passages[n - 1]).ToList();
            if (cited.Count == 0)
            {
                return null;
            }

            sources = cited
                .Select(p => new ItemSource(p.DocumentId, p.ChunkId, p.PageStart, Appears(draft.Quote, p.Text) ? draft.Quote!.Trim() : null))
                .ToList();
        }

        var flashcard = draft.Kind == PracticeItemKind.Flashcard;
        try
        {
            return PracticeItem.Create(
                ownerId,
                objective.CertificationId,
                objective.ObjectiveId,
                draft.Kind,
                origin,
                draft.Prompt,
                flashcard ? null : draft.Options,
                flashcard ? null : draft.CorrectOptions,
                draft.Answer,
                sources,
                now);
        }
        catch (DomainException)
        {
            return null;
        }
    }

    internal static bool Appears(string? quote, string text) =>
        !string.IsNullOrWhiteSpace(quote) && Collapse(text).Contains(Collapse(quote), StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
