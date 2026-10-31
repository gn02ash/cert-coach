import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import type { AnswerResult, ReviewCard, ReviewGrade, ReviewSource } from "../api/client";
import { useAnswer, useReviewSet } from "../api/hooks";
import { Alert, EmptyState, ProgressBar } from "../components/ui";
import { formatInterval, toggleOption } from "../lib/review";

const letters = "ABCDEFGH";
const grades: { grade: ReviewGrade; label: string; key: keyof NonNullable<ReviewCard["intervals"]> }[] = [
  { grade: "Again", label: "Again", key: "again" },
  { grade: "Hard", label: "Hard", key: "hard" },
  { grade: "Good", label: "Good", key: "good" },
  { grade: "Easy", label: "Easy", key: "easy" },
];

/** The daily review, or with ?objectives=… a quiz on a study session's objectives. */
export function ReviewPage() {
  const [params] = useSearchParams();
  const objectiveIds = params.get("objectives")?.split(",").filter(Boolean);
  const certificationId = params.get("certificationId") ?? undefined;
  const quiz = !!objectiveIds?.length;
  const set = useReviewSet({ certificationId, objectiveIds });
  const [index, setIndex] = useState(0);
  const [tally, setTally] = useState({ correct: 0, wrong: 0, cards: 0 });

  if (set.isPending) return <div className="page-loading">Loading…</div>;
  if (set.error) return <p className="error-text">{set.error.message}</p>;

  const { cards } = set.data;
  const card = cards[index];
  const title = quiz ? "Quiz" : "Review";

  if (cards.length === 0) {
    return (
      <div className="stack">
        <h1>{title}</h1>
        <EmptyState title={quiz ? "No practice items for these objectives yet" : "Nothing to review today"}>
          <p className="muted">
            {set.data.newAvailable > 0 && !quiz
              ? "You've reached today's limit of new items. More tomorrow."
              : "Generate questions from your documents on a certification's Practice items page."}
          </p>
          <Link className="button primary" to="/certifications">
            Go to certifications
          </Link>
        </EmptyState>
      </div>
    );
  }

  if (!card) {
    return (
      <div className="stack">
        <h1>{title} done</h1>
        <div className="card stack">
          <p>
            {tally.correct + tally.wrong > 0 && `${tally.correct} of ${tally.correct + tally.wrong} questions right. `}
            {tally.cards > 0 && `${tally.cards} ${tally.cards === 1 ? "flashcard" : "flashcards"} reviewed.`}
          </p>
          <p className="muted small">Each item comes back when it is due: sooner when you missed it, later when you knew it.</p>
          <div className="actions">
            <Link className="button primary" to="/">
              Back to today
            </Link>
          </div>
        </div>
      </div>
    );
  }

  const next = (result: AnswerResult) => {
    setTally((t) =>
      result.correct === null
        ? { ...t, cards: t.cards + 1 }
        : result.correct
          ? { ...t, correct: t.correct + 1 }
          : { ...t, wrong: t.wrong + 1 },
    );
    setIndex((i) => i + 1);
  };

  return (
    <div className="stack">
      <div className="page-head">
        <h1>{title}</h1>
        <span className="muted">
          {index + 1} of {cards.length}
        </span>
      </div>
      <ProgressBar value={index} max={cards.length} label="Items done" />
      {/* Keyed by card, so each one starts with nothing selected or revealed. */}
      <CardView key={card.id} card={card} source={quiz ? "Quiz" : "Review"} onNext={next} />
    </div>
  );
}

function CardView({ card, source, onNext }: { card: ReviewCard; source: ReviewSource; onNext: (result: AnswerResult) => void }) {
  const answer = useAnswer();
  const [selected, setSelected] = useState<number[]>([]);
  const [revealed, setRevealed] = useState(false);
  const result = answer.data;
  const flashcard = card.kind === "Flashcard";

  const submit = () => answer.mutate({ id: card.id, request: { selectedOptions: selected, grade: null, source } });
  const grade = (g: ReviewGrade) =>
    answer.mutate({ id: card.id, request: { selectedOptions: null, grade: g, source } }, { onSuccess: onNext });

  return (
    <article className="card stack review-card">
      <div className="review-meta">
        <span className="badge">{card.examCode}</span>
        {card.domain && <span className="muted small">{card.domain}</span>}
        {card.isNew && <span className="badge">New</span>}
        {card.origin === "ModelKnowledge" && <span className="badge warning">Not from your materials</span>}
      </div>
      {card.objectiveText && <p className="muted small">{card.objectiveText}</p>}
      <p className="review-prompt">{card.prompt}</p>

      {!flashcard && (
        <fieldset className="options" disabled={!!result}>
          <legend className="visually-hidden">{card.chooseCount > 1 ? `Choose ${card.chooseCount}` : "Choose one"}</legend>
          {card.options.map((option, i) => {
            const state = result
              ? result.correctOptions.includes(i)
                ? "correct"
                : selected.includes(i)
                  ? "wrong"
                  : ""
              : selected.includes(i)
                ? "selected"
                : "";
            return (
              <label key={i} className={`option ${state}`}>
                <input
                  type={card.chooseCount > 1 ? "checkbox" : "radio"}
                  name={`option-${card.id}`}
                  checked={selected.includes(i)}
                  onChange={() => setSelected((s) => toggleOption(s, i, card.chooseCount))}
                />
                <span className="option-letter">{letters[i]}</span>
                <span>{option}</span>
              </label>
            );
          })}
        </fieldset>
      )}

      {flashcard && revealed && <p className="review-back">{card.back}</p>}

      {result && !flashcard && (
        <div className="stack" aria-live="polite">
          <p className={result.correct ? "status-line completed" : "status-line missed"}>
            {result.correct ? "✓ Correct" : "✗ Not quite"} · back {formatInterval(result.intervalDays)}
          </p>
          {result.answer && <p>{result.answer}</p>}
          {result.sources.length > 0 && (
            <p className="muted small">
              From{" "}
              {result.sources.map((s, i) => (
                <span key={i}>
                  {i > 0 && ", "}
                  <a href={`/api/documents/${s.documentId}/file#page=${s.page}`} target="_blank" rel="noreferrer">
                    {s.documentTitle ?? "a deleted document"}, p. {s.page}
                  </a>
                </span>
              ))}
              {result.sources[0]?.quote && !result.answer.includes(result.sources[0].quote) && (
                <q className="review-quote">{result.sources[0].quote}</q>
              )}
            </p>
          )}
        </div>
      )}

      {answer.error && <Alert tone="error">{answer.error.message}</Alert>}

      <div className="actions">
        {!flashcard && !result && (
          <button className="button primary" disabled={selected.length === 0 || answer.isPending} onClick={submit}>
            Check
          </button>
        )}
        {!flashcard && result && (
          <button className="button primary" autoFocus onClick={() => onNext(result)}>
            Next
          </button>
        )}
        {flashcard && !revealed && (
          <button className="button primary" autoFocus onClick={() => setRevealed(true)}>
            Show answer
          </button>
        )}
        {flashcard &&
          revealed &&
          grades.map((g) => (
            <button key={g.grade} className={`button ${g.grade === "Good" ? "primary" : "ghost"}`} disabled={answer.isPending} onClick={() => grade(g.grade)}>
              {g.label}
              {card.intervals && <span className="grade-interval">{formatInterval(card.intervals[g.key])}</span>}
            </button>
          ))}
      </div>
    </article>
  );
}
