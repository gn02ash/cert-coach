/** "p. 4" for one page, "pp. 4–5" for a range. */
export function pageLabel(start: number, end: number) {
  return start === end ? `p. ${start}` : `pp. ${start}–${end}`;
}

const stopWords = new Set([
  "the", "and", "for", "are", "but", "not", "you", "your", "with", "how", "what", "when", "where", "which", "who",
  "why", "does", "this", "that", "from", "into", "can", "should", "would", "about", "there", "their", "them", "its",
]);

/**
 * Splits a passage into plain and matching parts. A word matches when it starts with a query word of three
 * or more letters, so "embedding" also marks "embeddings".
 */
export function highlightParts(text: string, query: string): { text: string; match: boolean }[] {
  const words = [...new Set(query.toLowerCase().match(/[\p{L}\p{N}]{3,}/gu) ?? [])].filter((w) => !stopWords.has(w));
  if (words.length === 0) return [{ text, match: false }];

  const escaped = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));
  const pattern = new RegExp(`(?<![\\p{L}\\p{N}])((?:${escaped.join("|")})[\\p{L}\\p{N}]*)`, "giu");

  const parts: { text: string; match: boolean }[] = [];
  let last = 0;
  for (const m of text.matchAll(pattern)) {
    if (m.index > last) parts.push({ text: text.slice(last, m.index), match: false });
    parts.push({ text: m[0], match: true });
    last = m.index + m[0].length;
  }
  if (last < text.length) parts.push({ text: text.slice(last), match: false });
  return parts;
}
