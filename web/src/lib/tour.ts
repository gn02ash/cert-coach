/** One stop on the welcome tour. */
export interface TourStep {
  /** The data-tour attribute of the element to point at; the card is centered when there is none. */
  target?: string;
  title: string;
  body: string;
}

export const tourSteps: TourStep[] = [
  {
    title: "Welcome to Cert Coach",
    body:
      "Cert Coach turns an exam's official skills outline into a daily study plan, answers questions from your own study PDFs, " +
      "and quizzes you until you're ready. This tour takes a minute: here's where everything is.",
  },
  {
    target: "certifications",
    title: "Add your exam",
    body:
      "Start here. Add a certification by pasting its exam page or study guide address into Import from URL, or paste the skills " +
      "outline. Then set your exam date and weekly hours to get a plan. Each certification also has Practice items and Resources.",
  },
  {
    target: "today",
    title: "Study a little every day",
    body:
      "Today shows the session to do now, the week ahead, your study streak and how ready you are per exam domain. Mark sessions " +
      "Done or Missed; a missed day moves its work to the days left.",
  },
  {
    target: "library",
    title: "Bring your own material",
    body:
      "Upload your study PDFs to the Library. Search finds passages in them, and Ask answers questions from them with a citation " +
      "to the page for every claim, or says when your documents don't cover it.",
  },
  {
    target: "review",
    title: "Practise until it sticks",
    body:
      "Generate questions and flashcards from your documents on a certification's Practice items page. Review brings each one back " +
      "just before you'd forget it, and you can take timed mock exams weighted like the real one.",
  },
  {
    target: "career",
    title: "Not sure which exam?",
    body: "Career reads your CV into a profile and ranks certifications that fit your skills and the role you want, with the reasons.",
  },
  {
    target: "connect",
    title: "Study from an AI assistant",
    body:
      "Connect gives Claude or another AI assistant access to your plan, so you can ask what to study today or be quizzed in chat. " +
      "It can't change or delete anything.",
  },
  {
    title: "You're ready",
    body: "Add your first certification to get a plan. You can take this tour again any time from Tour at the top of the page.",
  },
];

const storageKey = "certcoach.tour";

/** Whether this browser has finished or skipped the tour. Storage can be unavailable, in which case it never shows by itself. */
export function tourSeen(): boolean {
  try {
    return localStorage.getItem(storageKey) === "done";
  } catch {
    return true;
  }
}

export function markTourSeen(): void {
  try {
    localStorage.setItem(storageKey, "done");
  } catch {
    // Private browsing without storage: the tour may show again next time.
  }
}

/** Where to put a card of the given size beside a target, kept inside the viewport. */
export function placeCard(
  target: { top: number; left: number; width: number; height: number } | null,
  card: { width: number; height: number },
  viewport: { width: number; height: number },
  gap = 12,
): { top: number; left: number } {
  const margin = 16;
  const clamp = (value: number, max: number) => Math.max(margin, Math.min(value, max - margin));
  if (!target) {
    return { top: clamp((viewport.height - card.height) / 2, viewport.height - card.height), left: clamp((viewport.width - card.width) / 2, viewport.width - card.width) };
  }

  // Below the target when it fits, else above.
  const below = target.top + target.height + gap;
  const top = below + card.height <= viewport.height - margin ? below : target.top - gap - card.height;
  const left = target.left + target.width / 2 - card.width / 2;
  return { top: clamp(top, viewport.height - card.height), left: clamp(left, viewport.width - card.width) };
}
