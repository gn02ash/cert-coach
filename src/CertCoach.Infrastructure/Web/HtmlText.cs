using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CertCoach.Application.Certifications.Outlines.Import;

namespace CertCoach.Infrastructure.Web;

/// <summary>
/// Turns an HTML page into the markdown-like text the outline parser reads: <c>#</c> headings, <c>-</c> list
/// items, table rows as <c>cell | cell</c>, one block per line. Navigation, headers, footers and scripts are dropped.
/// </summary>
internal static class HtmlText
{
    private static readonly HashSet<string> Skipped =
    [
        "script", "style", "noscript", "template", "svg", "iframe", "nav", "header", "footer", "aside", "form", "button",
        "select", "dialog",
    ];

    private static readonly HashSet<string> Blocks =
    [
        "p", "div", "section", "article", "main", "body", "ul", "ol", "table", "thead", "tbody", "tfoot", "blockquote",
        "pre", "dl", "dt", "dd", "figure", "figcaption", "details", "summary", "hr",
    ];

    public static WebPage Convert(string html, Uri url)
    {
        var document = new HtmlParser().ParseDocument(html);
        var baseUrl = document.QuerySelector("base[href]")?.GetAttribute("href") is { } href
            && Uri.TryCreate(url, href, out var resolved) ? resolved : url;

        // Study guide pages keep the content in <main>; the rest is navigation.
        var root = document.QuerySelector("main") ?? document.Body;
        var writer = new Writer();
        if (root is not null)
        {
            writer.Walk(root);
        }

        var title = Collapse(document.QuerySelector("h1")?.TextContent ?? "") is { Length: > 0 } h1 ? h1 : Collapse(document.Title ?? "");
        return new WebPage(url, title.Length > 0 ? title : null, writer.ToString(), Links(document, baseUrl));
    }

    private static List<PageLink> Links(IDocument document, Uri baseUrl)
    {
        var links = new List<PageLink>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in document.QuerySelectorAll("a[href]"))
        {
            if (!Uri.TryCreate(baseUrl, a.GetAttribute("href"), out var target) || target.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var withoutFragment = new UriBuilder(target) { Fragment = "" }.Uri;
            var text = Collapse(a.TextContent);
            if (seen.Add(withoutFragment.AbsoluteUri + "\n" + text))
            {
                links.Add(new PageLink(withoutFragment, text.Length > 0 ? text : Collapse(a.GetAttribute("aria-label") ?? "")));
            }
        }

        return links;
    }

    internal static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
            }
            else
            {
                if (space)
                {
                    builder.Append(' ');
                    space = false;
                }

                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private sealed class Writer
    {
        private readonly StringBuilder lines = new();
        private readonly StringBuilder inline = new();
        private string prefix = "";

        public void Walk(INode node)
        {
            if (node is IText text)
            {
                inline.Append(text.Data);
                return;
            }

            if (node is not IElement element)
            {
                return;
            }

            var tag = element.LocalName;
            if (Skipped.Contains(tag) || element.HasAttribute("hidden") || element.GetAttribute("aria-hidden") == "true")
            {
                return;
            }

            if (tag.Length == 2 && tag[0] == 'h' && tag[1] is >= '1' and <= '6')
            {
                Flush();
                Line(new string('#', tag[1] - '0') + " " + Collapse(element.TextContent));
                return;
            }

            switch (tag)
            {
                case "br":
                    Flush();
                    return;
                case "tr":
                    Flush();
                    Line(string.Join(" | ", element.Children
                        .Where(c => c.LocalName is "td" or "th")
                        .Select(c => Collapse(c.TextContent))));
                    return;
                case "li":
                    Flush();
                    prefix = "- ";
                    WalkChildren(element);
                    Flush();
                    return;
            }

            var block = Blocks.Contains(tag);
            if (block)
            {
                Flush();
            }

            WalkChildren(element);
            if (block)
            {
                Flush();
            }
        }

        public override string ToString()
        {
            Flush();
            return lines.ToString().TrimEnd();
        }

        private void WalkChildren(IElement element)
        {
            foreach (var child in element.ChildNodes)
            {
                Walk(child);
            }
        }

        private void Flush()
        {
            var text = Collapse(inline.ToString());
            inline.Clear();
            if (text.Length > 0)
            {
                Line(prefix + text);
                prefix = "";
            }
        }

        private void Line(string text)
        {
            if (text.Trim().Trim('#', '|').Trim().Length > 0)
            {
                lines.Append(text).Append('\n');
            }
        }
    }
}
