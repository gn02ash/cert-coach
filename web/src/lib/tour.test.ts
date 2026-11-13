import { afterEach, describe, expect, it } from "vitest";
import { markTourSeen, placeCard, tourSeen, tourSteps } from "./tour";

const viewport = { width: 1280, height: 800 };
const card = { width: 360, height: 220 };

describe("placeCard", () => {
  it("centers the card when there is nothing to point at", () => {
    expect(placeCard(null, card, viewport)).toEqual({ top: 290, left: 460 });
  });

  it("puts the card under its target, centered on it", () => {
    expect(placeCard({ top: 20, left: 600, width: 80, height: 30 }, card, viewport)).toEqual({ top: 62, left: 460 });
  });

  it("goes above a target near the bottom and stays inside the window", () => {
    const placed = placeCard({ top: 700, left: 1250, width: 30, height: 30 }, card, viewport);
    expect(placed.top).toBe(700 - 12 - 220);
    expect(placed.left).toBe(1280 - 16 - 360);
  });

  it("keeps a margin on a phone", () => {
    expect(placeCard(null, { width: 360, height: 220 }, { width: 375, height: 700 }).left).toBe(16);
  });
});

describe("tour storage", () => {
  afterEach(() => localStorage.clear());

  it("remembers the tour was seen", () => {
    expect(tourSeen()).toBe(false);
    markTourSeen();
    expect(tourSeen()).toBe(true);
  });
});

describe("tourSteps", () => {
  it("starts and ends without a target, and points at each main area once", () => {
    expect(tourSteps[0]!.target).toBeUndefined();
    expect(tourSteps.at(-1)!.target).toBeUndefined();
    const targets = tourSteps.flatMap((s) => (s.target ? [s.target] : []));
    expect(new Set(targets).size).toBe(targets.length);
  });
});
