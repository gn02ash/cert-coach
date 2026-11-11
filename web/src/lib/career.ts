import type { AdviceView, RecommendationView } from "../api/client";

/** The roles the catalogue is tagged with; the server ignores any other. */
export const careerRoles = [
  "developer",
  "cloud engineer",
  "administrator",
  "architect",
  "data engineer",
  "data analyst",
  "data scientist",
  "ml engineer",
  "ai engineer",
  "devops engineer",
  "security engineer",
  "network engineer",
  "database administrator",
  "business user",
  "student",
];

/** "a, b;c\n d" → ["a", "b", "c", "d"]: entries separated by commas, semicolons or line breaks. */
export function splitList(text: string): string[] {
  return text
    .split(/[,;\n]/)
    .map((s) => s.trim())
    .filter(Boolean);
}

export const joinList = (items: string[]) => items.join(", ");

/** Puts the items the advice names first, in its order; the rest keep their score order. */
export function orderByAdvice(items: RecommendationView[], advice: AdviceView | undefined): RecommendationView[] {
  if (!advice) return items;
  const position = new Map(advice.items.map((a, i) => [a.code, i]));
  return [...items].sort((a, b) => (position.get(a.code) ?? Infinity) - (position.get(b.code) ?? Infinity));
}
