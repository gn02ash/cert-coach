import type { ActivityDay } from "../api/client";
import { formatLongDate } from "./dates";

/** Splits days that start on a Monday into weeks; the last week may be short. */
export function toWeeks(days: ActivityDay[]): ActivityDay[][] {
  const weeks: ActivityDay[][] = [];
  for (let i = 0; i < days.length; i += 7) weeks.push(days.slice(i, i + 7));
  return weeks;
}

/** 0 for a day off up to 4 for a heavy day. A completed session counts like ten reviews. */
export function activityLevel(day: ActivityDay): 0 | 1 | 2 | 3 | 4 {
  const effort = day.reviews + 10 * day.sessions;
  if (effort === 0) return 0;
  if (effort < 10) return 1;
  if (effort < 25) return 2;
  if (effort < 50) return 3;
  return 4;
}

export function describeDay(day: ActivityDay): string {
  const parts = [
    day.reviews > 0 ? `${day.reviews} ${day.reviews === 1 ? "review" : "reviews"}` : "",
    day.sessions > 0 ? `${day.sessions} ${day.sessions === 1 ? "session" : "sessions"}` : "",
  ].filter(Boolean);
  return `${formatLongDate(day.date)}: ${parts.length > 0 ? parts.join(", ") : "no study"}`;
}
