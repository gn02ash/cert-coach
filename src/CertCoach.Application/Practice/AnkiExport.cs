using System.Text;
using CertCoach.Domain.Practice;

namespace CertCoach.Application.Practice;

/// <summary>One item to export, with the tags it gets in Anki.</summary>
public sealed record ExportItem(PracticeItem Item, string? Domain);

/// <summary>
/// Writes practice items as a text file Anki imports (File → Import): tab-separated front, back and tags, with
/// the header lines that tell Anki how to read it. A question's options go on the front and its correct letters
/// and explanation on the back.
/// </summary>
public static class AnkiExport
{
    public const string ContentType = "text/plain; charset=utf-8";

    private const string Letters = "ABCDEFGH";

    public static string Build(string examCode, IEnumerable<ExportItem> items)
    {
        var text = new StringBuilder();
        text.Append("#separator:tab\n#html:false\n#columns:Front\tBack\tTags\n#tags column:3\n");
        foreach (var (item, domain) in items)
        {
            text.Append(Field(Front(item))).Append('\t')
                .Append(Field(Back(item))).Append('\t')
                .Append(Field(string.Join(' ', new[] { Tag(examCode), domain is null ? null : Tag(domain) }.OfType<string>())))
                .Append('\n');
        }

        return text.ToString();
    }

    private static string Front(PracticeItem item) =>
        item.Kind == PracticeItemKind.Flashcard
            ? item.Prompt
            : item.Prompt + "\n\n" + string.Join('\n', item.Options.Select((o, i) => $"{Letters[i]}. {o}"));

    private static string Back(PracticeItem item)
    {
        if (item.Kind == PracticeItemKind.Flashcard)
        {
            return item.Answer;
        }

        var correct = string.Join('\n', item.CorrectOptions.Select(i => $"{Letters[i]}. {item.Options[i]}"));
        return item.Answer.Length > 0 ? correct + "\n\n" + item.Answer : correct;
    }

    /// <summary>Quoted, with quotes doubled, so tabs and line breaks inside a field survive.</summary>
    internal static string Field(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Anki tags are separated by spaces, so a tag can't contain one.</summary>
    internal static string Tag(string value) =>
        string.Join('-', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace("\"", "", StringComparison.Ordinal);
}
