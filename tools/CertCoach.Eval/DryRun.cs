using System.Text.RegularExpressions;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CertCoach.Eval;

/// <summary>Typesets, extracts and chunks the corpus without a database, and checks every label can be found.</summary>
internal static partial class DryRun
{
    public static int Run(string root, IReadOnlyList<EvalQuestion> questions, int? chunkTokens)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ingestion:Chunking:TargetTokens"] = chunkTokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .Build();
        using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        var extractor = services.GetRequiredService<IPdfTextExtractor>();
        var tokens = services.GetRequiredService<ITokenCounter>();
        var chunking = services.GetRequiredService<ChunkingOptions>();

        var chunks = new Dictionary<string, IReadOnlyList<ChunkDraft>>();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "eval", "corpus"), "*.md").Order(StringComparer.Ordinal))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            using var pdf = new MemoryStream(MarkdownPdf.Render(File.ReadAllText(path), "DEMO-101 study notes"));
            var extracted = extractor.Extract(pdf);
            chunks[stem] = Chunker.Split(extracted.Blocks, tokens, chunking);

            Console.WriteLine($"{stem}: \"{extracted.Title}\", {extracted.PageCount} pages, {chunks[stem].Count} chunks");
            foreach (var chunk in chunks[stem])
            {
                Console.WriteLine($"  #{chunk.Index} p.{chunk.PageStart}-{chunk.PageEnd} {chunk.TokenCount,4} tokens  {chunk.HeadingPath}");
            }
        }

        var missing = questions
            .Where(q => !q.Unanswerable)
            .Where(q => !chunks[q.Document!].Any(c => Normalize(c.Text).Contains(Normalize(q.Evidence!), StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Console.WriteLine($"{questions.Count} questions; {missing.Count} with evidence no chunk contains in full");
        missing.ForEach(q => Console.WriteLine($"  {q.Id}: {q.Evidence}"));
        return 0;
    }

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
