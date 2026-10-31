/** "tomorrow", "in 6 days", "in 3 weeks", "in 2 months": when an item comes back. */
export function formatInterval(days: number): string {
  if (days <= 0) return "today";
  if (days === 1) return "tomorrow";
  if (days < 14) return `in ${days} days`;
  if (days < 60) return `in ${Math.round(days / 7)} weeks`;
  if (days < 365) return `in ${Math.round(days / 30)} months`;
  const years = Math.round(days / 365);
  return `in ${years} ${years === 1 ? "year" : "years"}`;
}

/** Selecting an option: one at a time for single-answer questions, toggled for "choose several". */
export function toggleOption(selected: number[], index: number, chooseCount: number): number[] {
  if (chooseCount <= 1) return [index];
  return selected.includes(index) ? selected.filter((i) => i !== index) : [...selected, index].sort((a, b) => a - b);
}
