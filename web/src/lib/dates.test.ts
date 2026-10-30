import { addDays, groupByWeek, parseDate, weekStart } from "./dates";

describe("dates", () => {
  it("parses calendar dates without shifting the day", () => {
    const date = parseDate("2026-11-02");
    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([2026, 10, 2]);
  });

  it("adds days across month boundaries", () => {
    expect(addDays("2026-11-29", 3)).toBe("2026-12-02");
  });

  it("finds the Monday that starts the week", () => {
    expect(weekStart("2026-11-02")).toBe("2026-11-02"); // Monday
    expect(weekStart("2026-11-08")).toBe("2026-11-02"); // Sunday
    expect(weekStart("2026-11-09")).toBe("2026-11-09");
  });

  it("groups sessions by week in order", () => {
    const groups = groupByWeek(["2026-11-02", "2026-11-05", "2026-11-09"], (d) => d);
    expect(groups).toEqual([
      { week: "2026-11-02", items: ["2026-11-02", "2026-11-05"] },
      { week: "2026-11-09", items: ["2026-11-09"] },
    ]);
  });
});
