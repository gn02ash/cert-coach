using System.Globalization;
using System.Text.RegularExpressions;

namespace CertCoach.Application.Certifications.Outlines.Import;

/// <summary>Facts about the exam that study guide pages state in predictable places: the title, the URL, a date line.</summary>
public static partial class PageFacts
{
    /// <summary>"AI-103", "AZ-104", "SAA-C03": from the title first, then the URL path.</summary>
    public static string? ExamCode(WebPage page) =>
        FirstExamCode(page.Title) ?? FirstExamCode(page.Url.AbsolutePath.ToUpperInvariant());

    /// <summary>
    /// "Study guide for Exam AI-103: Developing AI Apps on Azure" gives "Developing AI Apps on Azure";
    /// other titles lose a " | Site name" suffix.
    /// </summary>
    public static string? Name(WebPage page)
    {
        var title = page.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var pipe = title.IndexOf(" | ", StringComparison.Ordinal);
        if (pipe > 0)
        {
            title = title[..pipe].Trim();
        }

        var afterCode = AfterExamCode().Match(title);
        return afterCode.Success ? afterCode.Groups["name"].Value.Trim() : title;
    }

    /// <summary>The vendor for the sites that publish most exam outlines; null for others.</summary>
    public static string? Vendor(Uri url)
    {
        var host = url.Host.ToLowerInvariant();
        bool On(string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

        return On("microsoft.com") ? "Microsoft"
            : On("amazon.com") || On("awsstatic.com") ? "Amazon Web Services"
            : On("google.com") ? "Google Cloud"
            : null;
    }

    /// <summary>The "skills measured as of April 18, 2026" date, when the page has one.</summary>
    public static DateOnly? OutlineVersionDate(WebPage page)
    {
        foreach (Match match in AsOfDate().Matches(page.Text))
        {
            if (DateOnly.TryParseExact(
                    match.Groups["date"].Value.Replace(",", "", StringComparison.Ordinal),
                    ["MMMM d yyyy", "MMM d yyyy", "yyyy-MM-dd"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>Every distinct exam code in the text, in order.</summary>
    public static IEnumerable<string> ExamCodes(string text) =>
        ExamCodePattern().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal);

    private static string? FirstExamCode(string? text) =>
        text is not null && ExamCodePattern().Match(text) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"\b[A-Z]{2,4}-[A-Z]?\d{2,3}\b")]
    private static partial Regex ExamCodePattern();

    [GeneratedRegex(@"\b[A-Z]{2,4}-[A-Z]?\d{2,3}\s*:\s*(?<name>.+)$")]
    private static partial Regex AfterExamCode();

    [GeneratedRegex(@"\bas of\s+(?<date>[A-Z][a-z]+ \d{1,2},? \d{4}|\d{4}-\d{2}-\d{2})")]
    private static partial Regex AsOfDate();
}
