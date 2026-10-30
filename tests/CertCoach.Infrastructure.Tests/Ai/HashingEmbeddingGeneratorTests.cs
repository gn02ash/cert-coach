using CertCoach.Application.Ai;
using CertCoach.Infrastructure.Ai;

namespace CertCoach.Infrastructure.Tests.Ai;

public class HashingEmbeddingGeneratorTests
{
    [Fact]
    public void Vectors_have_the_stored_size_and_unit_length()
    {
        var vector = HashingEmbeddingGenerator.Embed("Configure retrieval-augmented generation with Azure AI Search");

        vector.Length.ShouldBe(AiCapabilities.EmbeddingDimensions);
        MathF.Sqrt(vector.Sum(v => v * v)).ShouldBe(1f, tolerance: 1e-5f);
    }

    [Fact]
    public void The_same_text_always_gives_the_same_vector()
    {
        HashingEmbeddingGenerator.Embed("Prompt caching cuts cost").ShouldBe(HashingEmbeddingGenerator.Embed("Prompt caching cuts cost"));
    }

    [Fact]
    public void Text_sharing_vocabulary_is_closer_than_unrelated_text()
    {
        var query = HashingEmbeddingGenerator.Embed("How do I chunk documents for embeddings?");
        var related = HashingEmbeddingGenerator.Embed("Split each document into chunks before computing the embedding of each chunk.");
        var unrelated = HashingEmbeddingGenerator.Embed("Assign Azure RBAC roles to a managed identity.");

        Cosine(query, related).ShouldBeGreaterThan(Cosine(query, unrelated) + 0.1f);
    }

    [Fact]
    public void Case_punctuation_and_plurals_do_not_matter()
    {
        var a = HashingEmbeddingGenerator.Embed("Embeddings, policies!");
        var b = HashingEmbeddingGenerator.Embed("embedding policy");

        Cosine(a, b).ShouldBe(1f, tolerance: 1e-5f);
    }

    [Fact]
    public void Stop_words_carry_no_weight()
    {
        HashingEmbeddingGenerator.Embed("What is the").ShouldAllBe(v => v == 0f);
        HashingEmbeddingGenerator.Embed("What is the quota?").ShouldBe(HashingEmbeddingGenerator.Embed("quota"));
    }

    [Fact]
    public void Empty_text_gives_a_zero_vector()
    {
        HashingEmbeddingGenerator.Embed(" - ").ShouldAllBe(v => v == 0f);
    }

    [Fact]
    public async Task Generates_one_embedding_per_input_in_order()
    {
        using var generator = new HashingEmbeddingGenerator();

        var result = await generator.GenerateAsync(["alpha", "beta"], cancellationToken: TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        result[1].Vector.ToArray().ShouldBe(HashingEmbeddingGenerator.Embed("beta"));
        result[0].ModelId.ShouldBe(HashingEmbeddingGenerator.ModelId);
    }

    private static float Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum();
}
