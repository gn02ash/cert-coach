using System.Text;
using System.Text.RegularExpressions;
using CertCoach.Application.Documents.Ingestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace CertCoach.Infrastructure.Documents;

/// <summary>
/// Reads text with PdfPig's layout analysis. Lines set noticeably larger than the body text become
/// headings, ranked by size, and lines repeated in the page margins (running headers, page numbers) are dropped.
/// </summary>
internal sealed partial class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <summary>A line this much larger than the body size is a heading.</summary>
    private const double HeadingRatio = 1.15;

    private const int MaxHeadingLength = 150;
    private const int MaxHeadingLevels = 3;
    private const int MaxRepeatedLineLength = 100;

    /// <summary>Running headers and footers sit in the top or bottom 7% of the page.</summary>
    private const double MarginZone = 0.07;

    /// <param name="InMargin">Whether the line sits where running headers and footers go.</param>
    private sealed record Line(int Page, string Text, double Size, bool InMargin);

    public ExtractedDocument Extract(Stream pdf)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDocumentException("The file could not be read as a PDF.", e);
        }

        using (document)
        {
            var lines = new List<List<Line>>();
            foreach (var page in document.GetPages())
            {
                lines.AddRange(ReadBlocks(page));
            }

            var bodySize = BodySize(lines.SelectMany(b => b));
            var repeated = RepeatedLines(lines.SelectMany(b => b), document.NumberOfPages);
            var levels = HeadingLevels(lines.SelectMany(b => b), bodySize);

            var blocks = new List<DocumentBlock>();
            foreach (var block in lines)
            {
                AddBlock(block.Where(l => !(l.InMargin && repeated.Contains(Normalize(l.Text)))).ToList(), bodySize, levels, blocks);
            }

            var title = document.Information.Title is { Length: > 0 } t && !string.IsNullOrWhiteSpace(t)
                ? t.Trim()
                : blocks.FirstOrDefault(b => b is { Kind: BlockKind.Heading, HeadingLevel: 1 })?.Text;

            return new ExtractedDocument(document.NumberOfPages, title, blocks);
        }
    }

    private static IEnumerable<List<Line>> ReadBlocks(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
        if (words.Count == 0)
        {
            yield break;
        }

        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(blocks))
        {
            yield return block.TextLines
                .Select(l => new Line(
                    page.Number,
                    l.Text.Trim(),
                    Median(l.Words.SelectMany(w => w.Letters).Select(Size)),
                    InMargin(l.BoundingBox.Bottom, l.BoundingBox.Top, page.Height)))
                .Where(l => l.Text.Length > 0)
                .ToList();
        }
    }

    /// <summary>Splits a layout block into headings and paragraphs, since a heading often sits in the same block as its first paragraph.</summary>
    private static void AddBlock(List<Line> lines, double bodySize, IReadOnlyDictionary<double, int> levels, List<DocumentBlock> blocks)
    {
        var i = 0;
        while (i < lines.Count)
        {
            var level = HeadingLevel(lines[i], bodySize, levels);
            var start = i;
            while (i < lines.Count && HeadingLevel(lines[i], bodySize, levels) == level
                   && (level == 0 || Math.Abs(lines[i].Size - lines[start].Size) < 0.5))
            {
                i++;
            }

            var text = JoinLines(lines.Skip(start).Take(i - start).Select(l => l.Text));
            blocks.Add(level > 0
                ? new DocumentBlock(lines[start].Page, BlockKind.Heading, text, level)
                : new DocumentBlock(lines[start].Page, BlockKind.Paragraph, text));
        }
    }

    private static int HeadingLevel(Line line, double bodySize, IReadOnlyDictionary<double, int> levels) =>
        IsHeading(line, bodySize) ? levels[Round(line.Size)] : 0;

    private static bool IsHeading(Line line, double bodySize) =>
        line.Size >= bodySize * HeadingRatio && line.Text.Length <= MaxHeadingLength && line.Text.Any(char.IsLetter);

    /// <summary>The largest heading size is level 1; anything beyond the third size shares level 3.</summary>
    private static Dictionary<double, int> HeadingLevels(IEnumerable<Line> lines, double bodySize) =>
        lines.Where(l => IsHeading(l, bodySize))
            .Select(l => Round(l.Size))
            .Distinct()
            .OrderDescending()
            .Select((size, i) => (size, level: Math.Min(i + 1, MaxHeadingLevels)))
            .ToDictionary(x => x.size, x => x.level);

    /// <summary>The size most of the characters are set in.</summary>
    private static double BodySize(IEnumerable<Line> lines) =>
        lines.GroupBy(l => Round(l.Size))
            .Select(g => (size: g.Key, chars: g.Sum(l => l.Text.Length)))
            .OrderByDescending(x => x.chars)
            .Select(x => x.size)
            .FirstOrDefault(defaultValue: 0);

    private static bool InMargin(double bottom, double top, double pageHeight) =>
        top <= pageHeight * MarginZone || bottom >= pageHeight * (1 - MarginZone);

    /// <summary>Short margin lines that appear on at least half the pages (and at least three), with page numbers ignored.</summary>
    private static HashSet<string> RepeatedLines(IEnumerable<Line> lines, int pageCount)
    {
        var threshold = Math.Max(3, (int)Math.Ceiling(pageCount / 2.0));
        return lines.Where(l => l.InMargin && l.Text.Length <= MaxRepeatedLineLength)
            .GroupBy(l => Normalize(l.Text))
            .Where(g => g.Select(l => l.Page).Distinct().Count() >= threshold)
            .Select(g => g.Key)
            .ToHashSet();
    }

    private static string Normalize(string text) => Digits().Replace(text, "#");

    /// <summary>Joins wrapped lines, rejoining words hyphenated across a line break.</summary>
    private static string JoinLines(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (text.Length > 0 && text[^1] == '-' && line.Length > 0 && char.IsLower(line[0]))
            {
                text.Length--;
            }
            else if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(line);
        }

        return text.ToString();
    }

    private static double Size(Letter letter) => letter.PointSize;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    private static double Round(double size) => Math.Round(size * 2) / 2;

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
