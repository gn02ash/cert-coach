import { highlightParts } from "../lib/citations";

/** Marks the query's words in a passage, so it is easy to see why it matched. */
export function Highlight({ text, query }: { text: string; query: string }) {
  return (
    <>
      {highlightParts(text, query).map((part, i) => (part.match ? <mark key={i}>{part.text}</mark> : <span key={i}>{part.text}</span>))}
    </>
  );
}
