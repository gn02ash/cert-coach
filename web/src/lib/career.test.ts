import { describe, expect, it } from "vitest";
import type { RecommendationView } from "../api/client";
import { orderByAdvice, splitList } from "./career";

describe("splitList", () => {
  it("splits on commas, semicolons and line breaks, dropping blanks", () => {
    expect(splitList(" azure, c# ;python\n\n docker ,")).toEqual(["azure", "c#", "python", "docker"]);
  });
});

describe("orderByAdvice", () => {
  const items = ["A", "B", "C"].map((code) => ({ code }) as RecommendationView);

  it("keeps the score order without advice", () => {
    expect(orderByAdvice(items, undefined)).toBe(items);
  });

  it("puts advised items first in the advised order", () => {
    const advice = { overview: "", items: [{ code: "C", rationale: "" }, { code: "A", rationale: "" }] };
    expect(orderByAdvice(items, advice).map((i) => i.code)).toEqual(["C", "A", "B"]);
  });
});
