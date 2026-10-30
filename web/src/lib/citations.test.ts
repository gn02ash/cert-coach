import { describe, expect, it } from "vitest";
import { highlightParts, pageLabel } from "./citations";

describe("pageLabel", () => {
  it("shows one page or a range", () => {
    expect(pageLabel(4, 4)).toBe("p. 4");
    expect(pageLabel(4, 5)).toBe("pp. 4–5");
  });
});

describe("highlightParts", () => {
  const marked = (text: string, query: string) =>
    highlightParts(text, query)
      .filter((p) => p.match)
      .map((p) => p.text);

  it("marks query words and their longer forms, ignoring case", () => {
    expect(marked("Embeddings and embedding models", "What is an EMBEDDING?")).toEqual(["Embeddings", "embedding"]);
  });

  it("ignores stop words and words shorter than three letters", () => {
    expect(marked("How does the cache work in AI", "how does the ai cache")).toEqual(["cache"]);
  });

  it("matches only at the start of a word", () => {
    expect(marked("Unsearchable but searchable", "search")).toEqual(["searchable"]);
  });

  it("keeps all of the text, in order", () => {
    const text = "Use (hybrid) search? Yes: hybrid.";
    expect(
      highlightParts(text, "hybrid")
        .map((p) => p.text)
        .join(""),
    ).toBe(text);
  });

  it("treats regex characters in the query as text", () => {
    expect(marked("c++ and c# notes", "c++ (notes)")).toEqual(["notes"]);
  });
});
