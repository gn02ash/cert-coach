using CertCoach.Domain.Common;
using CertCoach.Domain.Documents;

namespace CertCoach.Domain.Tests.Documents;

public class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static Document NewDocument(string fileName = "ai-103_study-notes.pdf") =>
        Document.Create(Guid.CreateVersion7(), fileName, 1024, "ABC123", null, Now);

    private static Document Processing()
    {
        var document = NewDocument();
        document.StartProcessing(Now);
        return document;
    }

    [Fact]
    public void A_new_document_is_pending_and_titled_after_its_file()
    {
        var document = NewDocument(@"C:\fakepath\ai-103_study-notes.pdf");

        document.Status.ShouldBe(DocumentStatus.Pending);
        document.FileName.ShouldBe("ai-103_study-notes.pdf");
        document.Title.ShouldBe("ai 103 study notes");
        document.ContentHash.ShouldBe("abc123");
        document.StorageKey.ShouldBe($"{document.OwnerId:N}/{document.Id:N}.pdf");
    }

    [Fact]
    public void Completing_records_the_result_and_takes_the_pdfs_title()
    {
        var document = Processing();

        document.CompleteProcessing(12, "  AI-103 Study Guide ", 40, "text-embedding-3-small", Now);

        document.Status.ShouldBe(DocumentStatus.Ready);
        (document.PageCount, document.ChunkCount, document.EmbeddingModel).ShouldBe((12, 40, "text-embedding-3-small"));
        document.Title.ShouldBe("AI-103 Study Guide");
        document.Attempts.ShouldBe(1);
    }

    [Fact]
    public void A_title_the_user_chose_is_kept()
    {
        var document = NewDocument();
        document.Update("My notes", null);
        document.StartProcessing(Now);

        document.CompleteProcessing(1, "Extracted title", 1, "m", Now);

        document.Title.ShouldBe("My notes");
    }

    [Fact]
    public void Transient_failures_retry_with_a_growing_delay_then_give_up()
    {
        var document = Processing();

        document.FailProcessing("timeout", retryable: true, Now);
        document.Status.ShouldBe(DocumentStatus.Pending);
        document.NextAttemptAt.ShouldBe(Now.AddSeconds(30));

        document.StartProcessing(Now);
        document.FailProcessing("timeout", retryable: true, Now);
        document.NextAttemptAt.ShouldBe(Now.AddSeconds(60));

        document.StartProcessing(Now);
        document.FailProcessing("timeout", retryable: true, Now);
        document.Status.ShouldBe(DocumentStatus.Failed);
        document.Attempts.ShouldBe(Document.MaxAttempts);
    }

    [Fact]
    public void A_file_that_cannot_be_read_fails_at_once()
    {
        var document = Processing();

        document.FailProcessing("No text found", retryable: false, Now);

        document.Status.ShouldBe(DocumentStatus.Failed);
        document.Error.ShouldBe("No text found");
    }

    [Fact]
    public void Requeueing_a_failed_document_resets_its_attempts()
    {
        var document = Processing();
        document.FailProcessing("bad", retryable: false, Now);

        document.Requeue();

        document.Status.ShouldBe(DocumentStatus.Pending);
        (document.Attempts, document.Error).ShouldBe((0, null));
    }

    [Fact]
    public void Only_pending_documents_can_be_claimed()
    {
        var document = Processing();

        Should.Throw<DomainException>(() => document.StartProcessing(Now));
        Should.Throw<DomainException>(() => document.Requeue());
    }

    [Fact]
    public void Titles_are_required_and_bounded()
    {
        var document = NewDocument();

        Should.Throw<DomainException>(() => document.Update(" ", null));
        Should.Throw<DomainException>(() => document.Update(new string('x', Document.MaxTitleLength + 1), null));
    }
}
