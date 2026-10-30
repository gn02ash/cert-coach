using System.Globalization;
using System.Text.RegularExpressions;

namespace CertCoach.Application.Certifications.Outlines;

/// <summary>
/// Parses a pasted "skills measured" outline, such as a Microsoft Learn study guide, into domains,
/// skill areas and objectives. Deterministic and offline; the result is a draft the learner edits.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A line ending in a weight such as <c>(15–20%)</c> or <c>(10%)</c> starts a domain. A domain
/// listed twice (a "skills at a glance" summary, then the details) is merged into one.</item>
/// <item>Inside a domain, a heading (<c>###</c>) or an unmarked line followed by a bullet starts a
/// skill area; bullets and other lines are objectives.</item>
/// <item>A heading at the same level as the domain headings without a weight, such as "Study
/// resources", ends the outline section.</item>
/// </list>
/// </remarks>
public static partial class OutlineParser
{
    public const string DefaultSkillAreaName = "General";

    public static OutlineDraft Parse(string text)
    {
        var lines = (text ?? string.Empty)
            .Split('\n')
            .Select(Classify)
            .Where(l => l.Content.Length > 0)
            .ToList();

        var domains = new List<DomainBuilder>();
        var warnings = new List<string>();
        DomainBuilder? domain = null;
        SkillAreaBuilder? area = null;
        int? domainHeadingLevel = null;
        var sawMarkers = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (TryParseDomain(line.Content, out var name, out var min, out var max))
            {
                domain = domains.Find(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                if (domain is null)
                {
                    domain = new DomainBuilder(name, min, max);
                    domains.Add(domain);
                }

                area = null;
                if (line.HeadingLevel > 0)
                {
                    domainHeadingLevel = line.HeadingLevel;
                }

                continue;
            }

            if (domain is null)
            {
                continue; // Preamble such as "Audience profile" or "Skills at a glance".
            }

            if (line.HeadingLevel > 0 && line.HeadingLevel <= domainHeadingLevel)
            {
                // A sibling section like "Study resources" ends the outline.
                domain = null;
                continue;
            }

            sawMarkers |= line.HeadingLevel > 0 || line.IsBullet;
            var startsSkillArea = line.HeadingLevel > 0
                || (!line.IsBullet && i + 1 < lines.Count && lines[i + 1].IsBullet);

            if (startsSkillArea)
            {
                area = new SkillAreaBuilder(line.Content);
                domain.SkillAreas.Add(area);
            }
            else
            {
                if (area is null)
                {
                    area = new SkillAreaBuilder(DefaultSkillAreaName);
                    domain.SkillAreas.Add(area);
                }

                area.Objectives.Add(line.Content);
            }
        }

        AddWarnings(domains, sawMarkers, warnings);

        return new OutlineDraft(
            domains
                .Select(d => new DomainDraft(
                    d.Name,
                    d.Min,
                    d.Max,
                    d.SkillAreas
                        .Where(s => s.Objectives.Count > 0)
                        .Select(s => new SkillAreaDraft(s.Name, s.Objectives.ToList()))
                        .ToList()))
                .ToList(),
            warnings);
    }

    private static void AddWarnings(List<DomainBuilder> domains, bool sawMarkers, List<string> warnings)
    {
        if (domains.Count == 0)
        {
            warnings.Add("No domains found. Each domain line needs a weight, for example \"Plan a solution (15–20%)\".");
            return;
        }

        foreach (var d in domains.Where(d => d.SkillAreas.All(s => s.Objectives.Count == 0)))
        {
            warnings.Add($"Domain \"{d.Name}\" has no objectives.");
        }

        var midpointTotal = domains.Sum(d => (d.Min + d.Max) / 2m);
        if (midpointTotal is < 90 or > 110)
        {
            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Domain weights add up to about {midpointTotal:0}% instead of 100%. Check the percentages."));
        }

        if (!sawMarkers && domains.Any(d => d.SkillAreas.Count > 0))
        {
            warnings.Add("No headings or bullets found, so every line was treated as an objective. Group them into skill areas in the editor.");
        }
    }

    private static bool TryParseDomain(string content, out string name, out int min, out int max)
    {
        name = string.Empty;
        min = max = 0;

        var match = WeightRange().Match(content);
        if (match.Success)
        {
            min = int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture);
            max = int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            match = SingleWeight().Match(content);
            if (!match.Success)
            {
                return false;
            }

            min = max = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }

        if (min > max)
        {
            (min, max) = (max, min);
        }

        name = content[..match.Index].Trim().TrimEnd('-', '–', '—', ':').Trim();
        return name.Length > 0 && max <= 100;
    }

    private static Line Classify(string raw)
    {
        var text = raw.Trim();
        var level = 0;
        while (level < text.Length && text[level] == '#')
        {
            level++;
        }

        if (level > 0)
        {
            return new Line(Clean(text[level..]), level, IsBullet: false);
        }

        var bullet = BulletMarker().Match(text);
        return bullet.Success
            ? new Line(Clean(text[bullet.Length..]), 0, IsBullet: true)
            : new Line(Clean(text), 0, IsBullet: false);
    }

    /// <summary>Strips markdown emphasis and a trailing colon.</summary>
    private static string Clean(string text) => text.Replace("**", string.Empty, StringComparison.Ordinal).Trim().TrimEnd(':').Trim();

    [GeneratedRegex(@"\(?\s*(?<min>\d{1,3})\s*%?\s*(?:-|–|—|to)\s*(?<max>\d{1,3})\s*%\s*\)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WeightRange();

    [GeneratedRegex(@"\(?\s*(?<value>\d{1,3})\s*%\s*\)?\s*$")]
    private static partial Regex SingleWeight();

    [GeneratedRegex(@"^(?:[-*+•▪◦]|\d{1,2}[.)])\s+")]
    private static partial Regex BulletMarker();

    private sealed record Line(string Content, int HeadingLevel, bool IsBullet);

    private sealed class DomainBuilder(string name, int min, int max)
    {
        public string Name { get; } = name;
        public int Min { get; } = min;
        public int Max { get; } = max;
        public List<SkillAreaBuilder> SkillAreas { get; } = [];
    }

    private sealed class SkillAreaBuilder(string name)
    {
        public string Name { get; } = name;
        public List<string> Objectives { get; } = [];
    }
}
