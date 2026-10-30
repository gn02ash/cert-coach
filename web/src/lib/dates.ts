/** Dates from the API are calendar dates ("2026-11-02"), never instants, so parse them without a time zone shift. */
export function parseDate(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);
  return new Date(year ?? 1970, (month ?? 1) - 1, day ?? 1);
}

export function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function addDays(value: string, days: number): string {
  const date = parseDate(value);
  date.setDate(date.getDate() + days);
  return toIsoDate(date);
}

export const formatDay = (value: string) =>
  parseDate(value).toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" });

export const formatLongDate = (value: string) =>
  parseDate(value).toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric", year: "numeric" });

/** The Monday on or before the given date. */
export function weekStart(value: string): string {
  const date = parseDate(value);
  const offset = (date.getDay() + 6) % 7;
  date.setDate(date.getDate() - offset);
  return toIsoDate(date);
}

/** Groups items by the Monday-based week of their date, preserving order. */
export function groupByWeek<T>(items: T[], dateOf: (item: T) => string): { week: string; items: T[] }[] {
  const groups: { week: string; items: T[] }[] = [];
  for (const item of items) {
    const week = weekStart(dateOf(item));
    const last = groups.at(-1);
    if (last?.week === week) last.items.push(item);
    else groups.push({ week, items: [item] });
  }
  return groups;
}
