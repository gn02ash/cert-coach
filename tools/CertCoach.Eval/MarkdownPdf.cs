using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CertCoach.Eval;

/// <summary>
/// Typesets the corpus Markdown (#, ##, ### headings, paragraphs, bullets) as a PDF with a text layer,
/// page breaks and a running footer, so evaluation goes through the same extraction as an uploaded file.
/// </summary>
internal static class MarkdownPdf
{
    private const double Margin = 60;
    private const double BodySize = 11;
    private const double A4Width = 595;

    public static byte[] Render(string markdown, string footer)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        PdfPageBuilder? page = null;
        double y = 0;
        var pageNumber = 0;

        void NewPage()
        {
            page = builder.AddPage(PageSize.A4);
            pageNumber++;
            y = page.PageSize.Height - Margin;
            page.AddText($"{footer} - page {pageNumber}", 8, new PdfPoint(Margin, 30), font);
        }

        // keepWithNext reserves room for the start of the next paragraph, so a heading never ends a page.
        void Write(string text, double size, PdfDocumentBuilder.AddedFont face, double spaceBefore, double keepWithNext = 0)
        {
            var lines = Wrap(text, size).ToList();
            if (page is null || y - spaceBefore - size * 1.35 * Math.Min(lines.Count, 3) - keepWithNext < Margin)
            {
                NewPage();
            }
            else
            {
                y -= spaceBefore;
            }

            foreach (var line in lines)
            {
                if (y - size * 1.35 < Margin)
                {
                    NewPage();
                }

                page!.AddText(line, size, new PdfPoint(Margin, y - size), face);
                y -= size * 1.35;
            }
        }

        foreach (var (kind, text) in Blocks(markdown))
        {
            switch (kind)
            {
                case "#":
                    Write(text, 20, bold, 0);
                    break;
                case "##":
                    Write(text, 15, bold, 14, keepWithNext: 3 * BodySize * 1.35);
                    break;
                case "###":
                    Write(text, 13, bold, 10, keepWithNext: 3 * BodySize * 1.35);
                    break;
                default:
                    Write(text, BodySize, font, 7);
                    break;
            }
        }

        return builder.Build();
    }

    /// <summary>Headings and paragraphs; consecutive text lines join into one paragraph, bullets stand alone.</summary>
    private static IEnumerable<(string Kind, string Text)> Blocks(string markdown)
    {
        var paragraph = new List<string>();
        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            var heading = line.StartsWith('#') ? line[..line.IndexOf(' ', StringComparison.Ordinal)] : null;

            if (heading is not null || line.Length == 0 || line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (paragraph.Count > 0)
                {
                    yield return ("p", string.Join(' ', paragraph));
                    paragraph.Clear();
                }
            }

            if (heading is not null)
            {
                yield return (heading, line[(heading.Length + 1)..]);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                yield return ("p", line);
            }
            else if (line.Length > 0)
            {
                paragraph.Add(line);
            }
        }

        if (paragraph.Count > 0)
        {
            yield return ("p", string.Join(' ', paragraph));
        }
    }

    private static IEnumerable<string> Wrap(string text, double size)
    {
        // Helvetica averages about half an em per character; close enough to keep lines inside the margins.
        var maxChars = (int)((A4Width - 2 * Margin) / (size * 0.5));
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                yield return line;
                line = "";
            }

            line = line.Length == 0 ? word : $"{line} {word}";
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }
}
