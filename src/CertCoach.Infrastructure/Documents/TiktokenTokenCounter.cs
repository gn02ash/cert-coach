using CertCoach.Application.Documents.Ingestion;
using Microsoft.ML.Tokenizers;

namespace CertCoach.Infrastructure.Documents;

/// <summary>cl100k_base, the encoding of the text-embedding-3 models. Other providers count close enough for chunk sizing.</summary>
internal sealed class TiktokenTokenCounter : ITokenCounter
{
    private static readonly TiktokenTokenizer Tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    public int Count(string text) => Tokenizer.CountTokens(text);
}
