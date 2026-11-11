import { describe, expect, it } from "vitest";
import { activityLevel, describeDay, toWeeks } from "./activity";

const day = (date: string, reviews = 0, sessions = 0) => ({ date, reviews, sessions });

describe("toWeeks", () => {
  it("groups days in sevens and keeps a short last week", () => {
    const days = Array.from({ length: 10 }, (_, i) => day(`2026-11-${String(i + 2).padStart(2, "0")}`));
    expect(toWeeks(days).map((w) => w.length)).toEqual([7, 3]);
  });
});

describe("activityLevel", () => {
  it.each([
    [0, 0, 0],
    [3, 0, 1],
    [0, 1, 2],
    [20, 1, 3],
    [60, 0, 4],
  ])("%i reviews and %i sessions is level %i", (reviews, sessions, level) => {
    expect(activityLevel(day("2026-11-02", reviews, sessions))).toBe(level);
  });
});

describe("describeDay", () => {
  it("says what was done", () => {
    expect(describeDay(day("2026-11-02", 1, 2))).toMatch(/: 1 review, 2 sessions$/);
    expect(describeDay(day("2026-11-02"))).toMatch(/: no study$/);
  });
});
