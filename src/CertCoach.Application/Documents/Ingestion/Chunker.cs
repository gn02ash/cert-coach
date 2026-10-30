using System.Text.RegularExpressions;

namespace CertCoach.Application.Documents.Ingestion;

/// <param name="TargetTokens">A chunk is closed once the next piece would take it past this size.</param>
/// <param name="OverlapTokens">Trailing sentences repeated at the start of the next chunk, so a fact split across the boundary is retrievable from either side.</param>
public sealed record ChunkingOptions(int TargetTokens = 450, int OverlapTokens = 64)
{
    public static ChunkingOptions Default { get; } = new();
}

/// <param name="HeadingPath">Section the chunk starts in, such as "Retrieval › Hybrid search".</param>
public sealed record ChunkDraft(int Index, string Text, string HeadingPath, int PageStart, int PageEnd, int TokenCount);

/// <summary>
/// Splits a document into retrieval-sized chunks that follow its structure: a top-level heading
/// always starts a new chunk, smaller headings do once the chunk is half full, and paragraphs
/// break before sentences, which break before words.
/// </summary>
public static partial class Chunker
{
    public const string PathSeparator = " › ";

    public static IReadOnlyList<ChunkDraft> Split(
        IReadOnlyList<DocumentBlock> blocks, ITokenCounter tokens, ChunkingOptions? options = null)
    {
        options ??= ChunkingOptions.Default;
        var builder = new Builder(tokens, options);

        foreach (var block in blocks)
        {
            if (block.Kind == BlockKind.Heading)
            {
                builder.Heading(block);
            }
            else
            {
                builder.Paragraph(block);
            }
        }

        builder.Flush(carryOverlap: false);
        return builder.Chunks;
    }

    /// <summary>A sentence or paragraph; <see cref="Paragraph"/> decides whether it joins its neighbour with a space or a blank line.</summary>
    private sealed record Piece(string Text, int Page, int Tokens, int Paragraph, bool IsOverlap = false);

    private sealed class Builder(ITokenCounter tokens, ChunkingOptions options)
    {
        private readonly string?[] _headings = new string?[7];
        private readonly List<Piece> _current = [];
        private int _paragraph;
        private string _currentPath = "";

        public List<ChunkDraft> Chunks { get; } = [];

        private int CurrentTokens => _current.Sum(p => p.Tokens);
        private bool HasNewContent => _current.Any(p => !p.IsOverlap);

        public void Heading(DocumentBlock heading)
        {
            var level = Math.Clamp(heading.HeadingLevel, 1, _headings.Length - 1);
            if (level == 1 || CurrentTokens >= options.TargetTokens / 2)
            {
                Flush(carryOverlap: false);
            }
            else if (HasNewContent)
            {
                // Too small to stand alone: keep the subheading as text so it isn't lost from the chunk.
                Add(new Piece(heading.Text, heading.Page, tokens.Count(heading.Text), ++_paragraph));
            }

            _headings[level] = heading.Text;
            Array.Clear(_headings, level + 1, _headings.Length - level - 1);
        }

        public void Paragraph(DocumentBlock block)
        {
            var paragraph = ++_paragraph;
            foreach (var piece in Pieces(block, paragraph))
            {
                if (HasNewContent && CurrentTokens + piece.Tokens > options.TargetTokens)
                {
                    Flush(carryOverlap: true);
                }

                Add(piece);
            }
        }

        public void Flush(bool carryOverlap)
        {
            if (!HasNewContent)
            {
                _current.Clear();
                return;
            }

            var text = string.Concat(_current.Select((p, i) =>
                i == 0 ? p.Text : (p.Paragraph == _current[i - 1].Paragraph ? " " : "\n\n") + p.Text));
            Chunks.Add(new ChunkDraft(
                Chunks.Count,
                text,
                _currentPath,
                _current.Min(p => p.Page),
                _current.Max(p => p.Page),
                tokens.Count(text)));

            var overlap = carryOverlap ? Overlap() : [];
            _current.Clear();
            _current.AddRange(overlap);
        }

        private void Add(Piece piece)
        {
            if (!HasNewContent)
            {
                _currentPath = string.Join(PathSeparator, _headings.Where(h => h is not null));
            }

            _current.Add(piece);
        }

        /// <summary>The last whole sentences of the chunk that fit in the overlap budget.</summary>
        private List<Piece> Overlap()
        {
            var sentences = _current
                .SelectMany(p => Sentences(p.Text).Select(s => p with { Text = s, Tokens = tokens.Count(s), IsOverlap = true }))
                .ToList();

            var result = new List<Piece>();
            var budget = options.OverlapTokens;
            for (var i = sentences.Count - 1; i > 0 && sentences[i].Tokens <= budget; i--)
            {
                budget -= sentences[i].Tokens;
                result.Insert(0, sentences[i]);
            }

            return result;
        }

        private IEnumerable<Piece> Pieces(DocumentBlock block, int paragraph)
        {
            var count = tokens.Count(block.Text);
            if (count <= options.TargetTokens)
            {
                yield return new Piece(block.Text, block.Page, count, paragraph);
                yield break;
            }

            foreach (var sentence in Sentences(block.Text))
            {
                var sentenceTokens = tokens.Count(sentence);
                if (sentenceTokens <= options.TargetTokens)
                {
                    yield return new Piece(sentence, block.Page, sentenceTokens, paragraph);
                    continue;
                }

                // A "sentence" this long is usually a table or list flattened into one line.
                foreach (var part in WordRuns(sentence))
                {
                    yield return new Piece(part, block.Page, tokens.Count(part), paragraph);
                }
            }
        }

        private IEnumerable<string> WordRuns(string text)
        {
            var run = new List<string>();
            var runTokens = 0;
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var wordTokens = tokens.Count(" " + word);
                if (run.Count > 0 && runTokens + wordTokens > options.TargetTokens)
                {
                    yield return string.Join(' ', run);
                    run.Clear();
                    runTokens = 0;
                }

                run.Add(word);
                runTokens += wordTokens;
            }

            if (run.Count > 0)
            {
                yield return string.Join(' ', run);
            }
        }
    }

    internal static string[] Sentences(string text) =>
        SentenceBoundary().Split(text.Trim()).Where(s => s.Length > 0).ToArray();

    // A sentence ends at . ! or ? followed by whitespace and a capital letter, digit or opening quote/bracket.
    [GeneratedRegex(@"(?<=[.!?][""')\]]?)\s+(?=[\p{Lu}\d""'(\[])")]
    private static partial Regex SentenceBoundary();
}
