import { describe, expect, it } from "vitest";
import { formatClock, formatInterval, toggleOption } from "./review";

describe("formatInterval", () => {
  it.each([
    [1, "tomorrow"],
    [6, "in 6 days"],
    [15, "in 2 weeks"],
    [90, "in 3 months"],
    [400, "in 1 year"],
  ])("formats %i days as %s", (days, text) => {
    expect(formatInterval(days)).toBe(text);
  });
});

describe("toggleOption", () => {
  it("replaces the choice on single-answer questions", () => {
    expect(toggleOption([0], 2, 1)).toEqual([2]);
  });

  it("toggles choices on multiple-answer questions", () => {
    expect(toggleOption([3], 1, 2)).toEqual([1, 3]);
    expect(toggleOption([1, 3], 3, 2)).toEqual([1]);
  });
});

describe("formatClock", () => {
  it.each([
    [0, "0:00"],
    [245, "4:05"],
    [3729, "1:02:09"],
    [-5, "0:00"],
  ])("formats %i seconds as %s", (seconds, text) => {
    expect(formatClock(seconds)).toBe(text);
  });
});
