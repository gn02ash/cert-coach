using System.Text.Json;
using System.Text.Json.Serialization;

namespace CertCoach.Application.Careers.Catalogue;

public enum CertificationLevel
{
    Fundamentals,
    Associate,
    Professional,
    Specialty,
}

/// <summary>A certification recommendations can choose from.</summary>
/// <param name="Code">The exam code, or a short ID for vendors that don't number their exams.</param>
/// <param name="Roles">The roles it suits, from <see cref="SkillVocabulary.Roles"/>.</param>
/// <param name="Skills">What it covers, as canonical skill names.</param>
/// <param name="After">Certifications commonly taken before it; holding one makes this the natural next step.</param>
/// <param name="Url">The vendor's official page, which for Microsoft is the study guide the outline importer reads.</param>
public sealed record CatalogueEntry(
    string Code,
    string Name,
    string Vendor,
    CertificationLevel Level,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> After,
    Uri Url,
    string Summary);

/// <summary>
/// The built-in list of public certifications, read once from an embedded JSON file. Recommendations only ever
/// rank entries from here, so neither the scorer nor a model can suggest an exam that doesn't exist. It is a
/// snapshot checked against the vendors' pages on <see cref="CheckedOn"/>; exams change, so each entry links
/// to its official page.
/// </summary>
public sealed class CertificationCatalogue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Lazy<CertificationCatalogue> Lazy = new(Load);

    private readonly Dictionary<string, CatalogueEntry> _byCode;

    private CertificationCatalogue(DateOnly checkedOn, IReadOnlyList<CatalogueEntry> entries)
    {
        CheckedOn = checkedOn;
        Entries = entries;
        _byCode = entries.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);
    }

    public static CertificationCatalogue Default => Lazy.Value;

    public DateOnly CheckedOn { get; }

    public IReadOnlyList<CatalogueEntry> Entries { get; }

    public CatalogueEntry? Find(string code) => _byCode.GetValueOrDefault(code.Trim());

    private static CertificationCatalogue Load()
    {
        var assembly = typeof(CertificationCatalogue).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("catalogue.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        var file = JsonSerializer.Deserialize<CatalogueFile>(stream, Json)!;
        return new CertificationCatalogue(file.CheckedOn, file.Certifications);
    }

    private sealed record CatalogueFile(DateOnly CheckedOn, List<CatalogueEntry> Certifications);
}
