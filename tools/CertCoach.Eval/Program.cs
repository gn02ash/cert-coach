// Retrieval evaluation: typesets the synthetic corpus in eval/corpus as PDFs, ingests them through the real
// pipeline (extraction, chunking, embeddings, PostgreSQL), runs every question in eval/questions.jsonl in
// each search mode, and reports recall@k, MRR and how well the "not in your materials" gate works. It also
// writes offline practice items for the sample outline's objectives and checks every one is grounded.
//
//   dotnet run --project tools/CertCoach.Eval -- [--connection <postgres>] [--out <file.md>]
//                                               [--min-recall5 <0..1>] [--min-mrr <0..1>] [--min-grounded <0..1>]
//                                               [--chunk-tokens <n>]
//   dotnet run --project tools/CertCoach.Eval -- --dry-run     (chunk the corpus and check the labels; no database)
//   dotnet run --project tools/CertCoach.Eval -- --export <dir> (write the corpus PDFs, e.g. to try the app with)
//
// Without --connection it starts a throwaway pgvector container (needs Docker). Embeddings use the same
// settings as the app: AI_EMBEDDINGS_* from the environment or the repository's .env, offline by default.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CertCoach.Application;
using CertCoach.Application.Abstractions;
using CertCoach.Application.Ai;
using CertCoach.Application.Documents.Ingestion;
using CertCoach.Domain.Documents;
using CertCoach.Domain.Users;
using CertCoach.Eval;
using CertCoach.Infrastructure;
using CertCoach.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

var options = EvalOptions.Parse(args);
var root = RepositoryRoot();
var questions = File.ReadLines(Path.Combine(root, "eval", "questions.jsonl"))
    .Where(l => l.Trim().Length > 0)
    .Select(l => JsonSerializer.Deserialize<EvalQuestion>(l, EvalQuestion.Json)!)
    .ToList();

if (options.Export is { } exportDirectory)
{
    Directory.CreateDirectory(exportDirectory);
    foreach (var path in Directory.GetFiles(Path.Combine(root, "eval", "corpus"), "*.md"))
    {
        var target = Path.Combine(exportDirectory, Path.GetFileNameWithoutExtension(path) + ".pdf");
        await File.WriteAllBytesAsync(target, MarkdownPdf.Render(await File.ReadAllTextAsync(path), "DEMO-101 study notes"));
        Console.WriteLine(target);
    }

    return 0;
}

if (options.DryRun)
{
    return DryRun.Run(root, questions, options.ChunkTokens);
}

PostgreSqlContainer? container = null;
var connection = options.Connection;
if (connection is null)
{
    Console.Error.WriteLine("Starting a pgvector container...");
    container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
    await container.StartAsync();
    connection = container.GetConnectionString();
}

var storage = Path.Combine(Path.GetTempPath(), "certcoach-eval", Guid.NewGuid().ToString("N"));
try
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(DotEnv.AiSettings(Path.Combine(root, ".env")))
        .AddInMemoryCollection(DotEnv.AiSettings(Environment.GetEnvironmentVariables()))
        .AddEnvironmentVariables()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CertCoach"] = connection,
            ["Storage:Path"] = storage,
            ["Ingestion:Enabled"] = "false",
            ["Ai:Chat:Provider"] = "Offline", // Retrieval only; no chat model is called.
            ["Ingestion:Chunking:TargetTokens"] = options.ChunkTokens?.ToString(CultureInfo.InvariantCulture),
        })
        .Build();

    var userId = Guid.CreateVersion7();
    var services = new ServiceCollection()
        .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning))
        .AddSingleton<IConfiguration>(configuration)
        .AddApplication()
        .AddInfrastructure(configuration)
        .AddScoped<ICurrentUser>(_ => new EvalUser(userId))
        .BuildServiceProvider();

    await services.MigrateDatabaseAsync();
    var capabilities = services.GetRequiredService<AiCapabilities>();
    var chunking = services.GetRequiredService<ChunkingOptions>();
    Console.Error.WriteLine($"Embeddings: {capabilities.EmbeddingModel} ({capabilities.EmbeddingProvider}), chunks of ~{chunking.TargetTokens} tokens");

    var documents = await Corpus.IngestAsync(services, userId, Path.Combine(root, "eval", "corpus"));
    var result = await Evaluator.RunAsync(services, questions, documents);
    var generation = await GenerationCheck.RunAsync(services, root);
    var report = Report.Markdown(result, capabilities, chunking) + Report.Generation(generation);

    Console.WriteLine(report);
    if (options.Out is { } outPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, report);
    }

    var hybrid = result.Modes.Single(m => m.Mode == CertCoach.Application.Search.SearchMode.Hybrid);
    var failures = new List<string>();
    if (options.MinRecall5 is { } minRecall && hybrid.RecallAt5 < minRecall)
    {
        failures.Add($"Hybrid recall@5 {hybrid.RecallAt5:P0} is below {minRecall:P0}.");
    }

    if (options.MinMrr is { } minMrr && hybrid.Mrr < minMrr)
    {
        failures.Add($"Hybrid MRR@10 {hybrid.Mrr:F2} is below {minMrr:F2}.");
    }

    if (options.MinGrounded is { } minGrounded && generation.GroundedShare < minGrounded)
    {
        failures.Add($"Only {generation.GroundedShare:P0} of generated items are grounded in their passage; the minimum is {minGrounded:P0}.");
    }

    failures.ForEach(Console.Error.WriteLine);
    return failures.Count == 0 ? 0 : 1;
}
finally
{
    if (container is not null)
    {
        await container.DisposeAsync();
    }

    if (Directory.Exists(storage))
    {
        Directory.Delete(storage, recursive: true);
    }
}

static string RepositoryRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "CertCoach.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run the tool from inside the repository.");
}

namespace CertCoach.Eval
{
    internal sealed record EvalOptions(
        string? Connection, string? Out, double? MinRecall5, double? MinMrr, double? MinGrounded, int? ChunkTokens, bool DryRun, string? Export)
    {
        public static EvalOptions Parse(string[] args)
        {
            string? Value(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            double? Number(string name) => Value(name) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : null;

            return new EvalOptions(
                Value("--connection"),
                Value("--out"),
                Number("--min-recall5"),
                Number("--min-mrr"),
                Number("--min-grounded"),
                Value("--chunk-tokens") is { } c ? int.Parse(c, CultureInfo.InvariantCulture) : null,
                args.Contains("--dry-run"),
                Value("--export"));
        }
    }

    internal sealed record EvalQuestion(string Id, string Question, string? Document, string? Evidence, bool Unanswerable)
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    }

    internal sealed class EvalUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }

    internal static class Corpus
    {
        /// <summary>Ingests every corpus file through the real pipeline; returns document IDs by file stem.</summary>
        public static async Task<Dictionary<string, Guid>> IngestAsync(IServiceProvider services, Guid userId, string directory)
        {
            var now = DateTimeOffset.UtcNow;
            await using (var scope = services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
                db.Users.Add(User.Create("eval", userId.ToString(), "Evaluation", null, now, userId));
                await db.SaveChangesAsync();
            }

            var ids = new Dictionary<string, Guid>();
            foreach (var path in Directory.GetFiles(directory, "*.md").Order(StringComparer.Ordinal))
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                var pdf = MarkdownPdf.Render(await File.ReadAllTextAsync(path), "DEMO-101 study notes");

                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CertCoachDbContext>();
                var files = scope.ServiceProvider.GetRequiredService<IFileStore>();

                var document = Document.Create(userId, stem + ".pdf", pdf.Length, Convert.ToHexStringLower(SHA256.HashData(pdf)), null, now);
                await files.SaveAsync(document.StorageKey, new MemoryStream(pdf), CancellationToken.None);
                document.StartProcessing(now);
                db.Documents.Add(document);
                await db.SaveChangesAsync();

                await scope.ServiceProvider.GetRequiredService<DocumentIngestionService>().ProcessAsync(document.Id, CancellationToken.None);
                db.ChangeTracker.Clear();
                var processed = await db.Documents.FindAsync(document.Id);
                if (processed!.Status != DocumentStatus.Ready)
                {
                    throw new InvalidOperationException($"{stem} failed to process: {processed.Error}");
                }

                Console.Error.WriteLine($"  {stem}: {processed.PageCount} pages, {processed.ChunkCount} chunks");
                ids[stem] = document.Id;
            }

            return ids;
        }
    }
}
