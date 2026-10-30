import { describe, expect, it } from "vitest";
import { parseAnswer, parseInline } from "./answer";

const sources = new Set([1, 2, 3]);

describe("parseInline", () => {
  it("turns [n] into citations and keeps the text around them", () => {
    expect(parseInline("Merge rankings [1][3].", sources)).toEqual([
      { kind: "text", text: "Merge rankings " },
      { kind: "cite", n: 1 },
      { kind: "cite", n: 3 },
      { kind: "text", text: "." },
    ]);
  });

  it("expands grouped citations and drops unknown ones", () => {
    expect(parseInline("Both [1, 9] apply [7].", sources)).toEqual([
      { kind: "text", text: "Both " },
      { kind: "cite", n: 1 },
      { kind: "text", text: " apply." },
    ]);
  });

  it("reads bold text", () => {
    expect(parseInline("Use **hybrid search** here", sources)).toEqual([
      { kind: "text", text: "Use " },
      { kind: "bold", text: "hybrid search" },
      { kind: "text", text: " here" },
    ]);
  });

  it("leaves other brackets alone", () => {
    expect(parseInline("array[i] and [a]", sources)).toEqual([{ kind: "text", text: "array[i] and [a]" }]);
  });
});

describe("parseAnswer", () => {
  it("splits paragraphs and bullet lists", () => {
    const blocks = parseAnswer("Short answer [1].\nStill the same paragraph.\n\n- first [2]\n- second\n\nAfter.", sources);

    expect(blocks.map((b) => b.kind)).toEqual(["paragraph", "list", "paragraph"]);
    expect(blocks[0]).toEqual({
      kind: "paragraph",
      inlines: [
        { kind: "text", text: "Short answer " },
        { kind: "cite", n: 1 },
        { kind: "text", text: ". Still the same paragraph." },
      ],
    });
    expect(blocks[1]).toMatchObject({ kind: "list", items: [[{ text: "first " }, { n: 2 }], [{ text: "second" }]] });
  });

  it("handles a partial answer while it is still streaming", () => {
    expect(parseAnswer("Hybrid search mer", sources)).toEqual([
      { kind: "paragraph", inlines: [{ kind: "text", text: "Hybrid search mer" }] },
    ]);
  });
});
