namespace CertCoach.Application.Certifications.Outlines.Import;

public sealed record ImportOutlineRequest(string Url);

public enum OutlineImportMethod
{
    /// <summary>The deterministic parser read the page.</summary>
    Parser,

    /// <summary>The page had the outline in a layout only the chat model could read.</summary>
    Ai,

    /// <summary>The chat model browsed the site to find the outline.</summary>
    Agent,
}

/// <summary>
/// An outline imported from a URL, with what the pages said about the exam. Fields the pages didn't give are
/// null; the editor fills only the empty ones.
/// </summary>
/// <param name="SourceUrl">The page the outline came from, which may differ from the URL the learner gave.</param>
/// <param name="PagesRead">Every page fetched, in order, so the learner can check them.</param>
public sealed record OutlineImport(
    OutlineDraft Draft,
    Uri SourceUrl,
    string? ExamCode,
    string? Name,
    string? Vendor,
    DateOnly? OutlineVersionDate,
    IReadOnlyList<Uri> PagesRead,
    OutlineImportMethod Method);
