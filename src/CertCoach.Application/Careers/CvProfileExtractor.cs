using System.Globalization;
using System.Text.RegularExpressions;
using CertCoach.Application.Careers.Catalogue;

namespace CertCoach.Application.Careers;

public enum CvReadMethod
{
    /// <summary>Known skills, job titles and exam codes matched in the text. No model.</summary>
    Offline,

    /// <summary>Read by the chat model.</summary>
    Ai,
}

/// <summary>What was read from a CV, for the learner to check and edit before saving. Nothing else of the CV is kept.</summary>
public sealed record ProfileDraft(
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> HeldCertifications,
    int? YearsExperience,
    CvReadMethod Method,
    IReadOnlyList<string> Notes);

/// <summary>
/// Reads a CV without a model: skills from the vocabulary, roles from job titles, held certifications from exam
/// codes and catalogue names, and experience from "N years" or the span of the years mentioned.
/// </summary>
public static partial class CvProfileExtractor
{
    public const int MaxSkills = 40;

    public static ProfileDraft Extract(string text, CertificationCatalogue catalogue, int currentYear)
    {
        var skills = SkillVocabulary.FindSkills(text).Take(MaxSkills).ToList();
        var roles = SkillVocabulary.FindRoles(text).Take(4).ToList();

        var held = ExamCode().Matches(text).Select(m => m.Value.ToUpperInvariant())
            .Concat(catalogue.Entries.Where(e => text.Contains(e.Name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Code))
            .Concat(catalogue.Entries.Where(e => !e.Code.Contains('-', StringComparison.Ordinal) && Regex.IsMatch(text, $@"\b{Regex.Escape(e.Code)}\b")).Select(e => e.Code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var notes = new List<string>();
        if (skills.Count == 0)
        {
            notes.Add("No known skills were found in this text. Add yours by hand.");
        }

        notes.Add("Read without AI, by matching known skills, job titles and exam codes. Check the result and add what's missing.");
        return new ProfileDraft(skills, roles, held, Years(text, currentYear), CvReadMethod.Offline, notes);
    }

    /// <summary>The largest "N years" stated, or else the span from the earliest year mentioned to the latest (or now).</summary>
    internal static int? Years(string text, int currentYear)
    {
        var stated = StatedYears().Matches(text)
            .Select(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture))
            .Where(n => n is > 0 and <= 45)
            .DefaultIfEmpty(0)
            .Max();
        if (stated > 0)
        {
            return stated;
        }

        var years = CalendarYear().Matches(text)
            .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
            .Where(y => y >= currentYear - 45 && y <= currentYear)
            .ToList();
        if (years.Count < 2)
        {
            return null;
        }

        var end = Present().IsMatch(text) ? currentYear : years.Max();
        return Math.Max(0, end - years.Min());
    }

    /// <summary>Codes in the shape most vendors use, such as AZ-104 or SAA-C03, so retired exams are picked up too.</summary>
    [GeneratedRegex(@"\b[A-Z]{2,3}-(?:\d{3}|C\d{2})\b")]
    private static partial Regex ExamCode();

    [GeneratedRegex(@"(?<n>\d{1,2})\s*\+?\s*(?:years|yrs)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StatedYears();

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex CalendarYear();

    [GeneratedRegex(@"\b(present|current|now|today)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Present();
}
