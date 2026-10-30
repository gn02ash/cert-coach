export type Inline = { kind: "text"; text: string } | { kind: "bold"; text: string } | { kind: "cite"; n: number };
export type Block = { kind: "paragraph"; inlines: Inline[] } | { kind: "list"; items: Inline[][] };

/**
 * Parses the small Markdown subset the answer prompt allows (paragraphs, bullets, bold) plus [n] citations,
 * into a tree React can render without injecting HTML. Citations to unknown sources are dropped.
 */
export function parseAnswer(text: string, validSources: ReadonlySet<number>): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: Inline[][] | null = null;

  const flushParagraph = () => {
    if (paragraph.length > 0) blocks.push({ kind: "paragraph", inlines: parseInline(paragraph.join(" "), validSources) });
    paragraph = [];
  };
  const flushList = () => {
    if (list) blocks.push({ kind: "list", items: list });
    list = null;
  };

  for (const raw of text.split("\n")) {
    const line = raw.trim();
    const bullet = /^(?:[-*•]|\d+[.)])\s+(.*)$/.exec(line);
    if (bullet) {
      flushParagraph();
      (list ??= []).push(parseInline(bullet[1]!, validSources));
    } else if (line === "") {
      flushParagraph();
      flushList();
    } else {
      flushList();
      paragraph.push(line);
    }
  }

  flushParagraph();
  flushList();
  return blocks;
}

export function parseInline(text: string, validSources: ReadonlySet<number>): Inline[] {
  const inlines: Inline[] = [];
  const pattern = /\*\*(.+?)\*\*|\[(\d{1,3}(?:\s*,\s*\d{1,3})*)\]/g;
  let last = 0;

  const pushText = (value: string) => {
    if (value.length === 0) return;
    const previous = inlines[inlines.length - 1];
    if (previous?.kind === "text") previous.text += value;
    else inlines.push({ kind: "text", text: value });
  };

  for (const match of text.matchAll(pattern)) {
    pushText(text.slice(last, match.index));
    if (match[1] !== undefined) {
      inlines.push({ kind: "bold", text: match[1] });
    } else {
      for (const n of match[2]!.split(",").map((s) => Number(s.trim()))) {
        if (validSources.has(n)) inlines.push({ kind: "cite", n });
      }
    }
    last = match.index + match[0].length;
  }

  pushText(text.slice(last));

  // Dropping an invalid citation can leave "word ." behind.
  for (const inline of inlines) {
    if (inline.kind === "text") inline.text = inline.text.replace(/ +([.,;:])/g, "$1");
  }
  return inlines;
}
