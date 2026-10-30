using CertCoach.Application.Documents.Ingestion;
using CertCoach.Infrastructure.Documents;
using static CertCoach.Infrastructure.Tests.Documents.TestPdf;

namespace CertCoach.Infrastructure.Tests.Documents;

public class PdfPigTextExtractorTests
{
    private const string Intro =
        "Retrieval-augmented generation grounds a model's answer in your own documents. " +
        "The application retrieves the passages most related to a question and passes them to the model with the question.";

    private const string Hybrid =
        "Hybrid search runs a vector search and a keyword search, then merges both rankings with reciprocal rank fusion.";

    private static ExtractedDocument Extract(TestPdf pdf)
    {
        using var stream = new MemoryStream(pdf.Build());
        return new PdfPigTextExtractor().Extract(stream);
    }

    [Fact]
    public void Reads_headings_by_size_and_paragraphs_in_order()
    {
        var document = Extract(new TestPdf()
            .Page(("Study notes", TitleSize), ("Retrieval", HeadingSize), (Intro, BodySize))
            .Page(("Hybrid search", HeadingSize), (Hybrid, BodySize)));

        document.PageCount.ShouldBe(2);
        document.Blocks.Select(b => (b.Kind, b.HeadingLevel, b.Page)).ShouldBe(
        [
            (BlockKind.Heading, 1, 1),
            (BlockKind.Heading, 2, 1),
            (BlockKind.Paragraph, 0, 1),
            (BlockKind.Heading, 2, 2),
            (BlockKind.Paragraph, 0, 2),
        ]);
        document.Blocks[2].Text.ShouldBe(Intro);
        document.Blocks[4].Text.ShouldBe(Hybrid);
    }

    [Fact]
    public void Uses_the_first_top_level_heading_as_the_title_when_metadata_has_none()
    {
        Extract(new TestPdf().Page(("Study notes", TitleSize), (Intro, BodySize))).Title.ShouldBe("Study notes");
    }

    [Fact]
    public void Drops_running_footers_and_page_numbers_but_keeps_repeated_body_text()
    {
        var pdf = new TestPdf { Footer = "DEMO-101 notes, page" };
        for (var i = 0; i < 4; i++)
        {
            pdf.Page(($"Section {i}", HeadingSize), (Intro, BodySize));
        }

        var document = Extract(pdf);

        document.Blocks.ShouldNotContain(b => b.Text.Contains("DEMO-101", StringComparison.Ordinal));
        document.Blocks.Count(b => b.Kind == BlockKind.Paragraph).ShouldBe(4);
        document.Blocks.Count(b => b.Kind == BlockKind.Heading).ShouldBe(4);
    }

    [Fact]
    public void A_pdf_without_text_reports_no_text()
    {
        var document = Extract(new TestPdf().Page());

        document.PageCount.ShouldBe(1);
        document.HasText.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_files_that_are_not_pdfs()
    {
        using var stream = new MemoryStream("not a pdf"u8.ToArray());

        Should.Throw<InvalidDocumentException>(() => new PdfPigTextExtractor().Extract(stream));
    }

    [Fact]
    public void Extracted_blocks_chunk_with_page_and_heading_citations()
    {
        var document = Extract(new TestPdf()
            .Page(("Study notes", TitleSize), ("Retrieval", HeadingSize), (Intro, BodySize))
            .Page(("Hybrid search", HeadingSize), (Hybrid, BodySize)));

        var chunks = Chunker.Split(document.Blocks, new TiktokenTokenCounter(), new ChunkingOptions(TargetTokens: 40, OverlapTokens: 0));

        chunks.Select(c => (c.HeadingPath, c.PageStart)).ShouldBe(
        [
            ("Study notes › Retrieval", 1),
            ("Study notes › Hybrid search", 2),
        ]);
    }

    [Fact]
    public void Counts_tokens_with_the_embedding_models_encoding()
    {
        new TiktokenTokenCounter().Count("hello world").ShouldBe(2);
    }
}
