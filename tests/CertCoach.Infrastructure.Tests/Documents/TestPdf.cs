using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CertCoach.Infrastructure.Tests.Documents;

/// <summary>Writes small PDFs with real text layers, so extraction is tested without binary fixtures.</summary>
internal sealed class TestPdf
{
    public const double TitleSize = 20;
    public const double HeadingSize = 15;
    public const double BodySize = 11;

    private const double Margin = 60;
    private const int WrapAt = 85;

    private readonly List<List<(string Text, double Size)>> _pages = [];

    public string? Footer { get; init; }

    public TestPdf Page(params (string Text, double Size)[] items)
    {
        _pages.Add([.. items]);
        return this;
    }

    public byte[] Build()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        for (var p = 0; p < _pages.Count; p++)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = page.PageSize.Height - Margin;

            foreach (var (text, size) in _pages[p])
            {
                // Paragraphs get a blank line before them, as layout analysis expects.
                y -= size * 0.8;
                foreach (var line in Wrap(text))
                {
                    page.AddText(line, size, new PdfPoint(Margin, y), font);
                    y -= size * 1.3;
                }
            }

            if (Footer is not null)
            {
                page.AddText($"{Footer} {p + 1}", 9, new PdfPoint(Margin, 30), font);
            }
        }

        return builder.Build();
    }

    private static IEnumerable<string> Wrap(string text)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > WrapAt)
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
