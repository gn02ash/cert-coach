import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router";
import type { MockExamView, MockQuestionView } from "../api/client";
import { useMockExam, useSaveMockAnswer, useSubmitMockExam } from "../api/hooks";
import { Alert, ProgressBar } from "../components/ui";
import { formatClock, toggleOption } from "../lib/review";

const letters = "ABCDEFGH";

export function MockExamPage() {
  const { id } = useParams();
  const exam = useMockExam(id);

  if (exam.isPending) return <div className="page-loading">Loading…</div>;
  if (exam.error) return <p className="error-text">{exam.error.message}</p>;

  return exam.data.status === "Submitted" ? <Results exam={exam.data} /> : <Taking exam={exam.data} />;
}

function Taking({ exam }: { exam: MockExamView }) {
  const save = useSaveMockAnswer(exam.id);
  const submit = useSubmitMockExam();
  const [index, setIndex] = useState(0);
  const [answers, setAnswers] = useState<Record<string, number[]>>(() =>
    Object.fromEntries(exam.questions.map((q) => [q.itemId, q.selected])),
  );

  // Counts down from the server's remaining time, measured against this device's clock from when it loaded.
  const deadline = useRef(Date.now() + exam.remainingSeconds * 1000);
  const [remaining, setRemaining] = useState(exam.remainingSeconds);
  useEffect(() => {
    const timer = setInterval(() => setRemaining(Math.max(0, Math.round((deadline.current - Date.now()) / 1000))), 1000);
    return () => clearInterval(timer);
  }, []);

  const finish = () => submit.mutate(exam.id);
  useEffect(() => {
    if (remaining === 0 && !submit.isPending && !submit.isSuccess) submit.mutate(exam.id);
  }, [remaining, submit, exam.id]);

  const question = exam.questions[index]!;
  const answered = exam.questions.filter((q) => (answers[q.itemId] ?? []).length > 0).length;
  const choose = (option: number) => {
    const selected = toggleOption(answers[question.itemId] ?? [], option, question.chooseCount);
    setAnswers((a) => ({ ...a, [question.itemId]: selected }));
    save.mutate({ itemId: question.itemId, selected });
  };
  const confirmSubmit = () => {
    const blank = exam.questions.length - answered;
    if (blank === 0 || confirm(`${blank} ${blank === 1 ? "question is" : "questions are"} unanswered and will count as wrong. Submit?`)) finish();
  };

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Mock exam: {exam.examCode}</h1>
        <span className={`exam-clock${remaining < 300 ? " low" : ""}`} role="timer" aria-label="Time left">
          {formatClock(remaining)}
        </span>
      </div>
      <ProgressBar value={answered} max={exam.questions.length} label="Questions answered" />

      <nav className="exam-nav" aria-label="Questions">
        {exam.questions.map((q, i) => (
          <button
            key={q.itemId}
            type="button"
            className={`exam-nav-item${i === index ? " current" : ""}${(answers[q.itemId] ?? []).length > 0 ? " answered" : ""}`}
            aria-current={i === index ? "step" : undefined}
            onClick={() => setIndex(i)}
          >
            {i + 1}
          </button>
        ))}
      </nav>

      <article className="card stack review-card">
        <div className="review-meta">
          <span className="muted small">
            Question {index + 1} of {exam.questions.length}
          </span>
          {question.origin === "ModelKnowledge" && <span className="badge warning">Not from your materials</span>}
        </div>
        <p className="review-prompt">{question.prompt}</p>
        <fieldset className="options">
          <legend className="visually-hidden">{question.chooseCount > 1 ? `Choose ${question.chooseCount}` : "Choose one"}</legend>
          {question.options.map((option, i) => {
            const selected = (answers[question.itemId] ?? []).includes(i);
            return (
              <label key={i} className={`option${selected ? " selected" : ""}`}>
                <input
                  type={question.chooseCount > 1 ? "checkbox" : "radio"}
                  name={`option-${question.itemId}`}
                  checked={selected}
                  onChange={() => choose(i)}
                />
                <span className="option-letter">{letters[i]}</span>
                <span>{option}</span>
              </label>
            );
          })}
        </fieldset>
        {save.error && <Alert tone="error">Your last answer wasn't saved: {save.error.message}</Alert>}
        <div className="row-between">
          <div className="actions">
            <button type="button" className="button ghost" disabled={index === 0} onClick={() => setIndex(index - 1)}>
              Previous
            </button>
            <button type="button" className="button ghost" disabled={index === exam.questions.length - 1} onClick={() => setIndex(index + 1)}>
              Next
            </button>
          </div>
          <button type="button" className="button primary" disabled={submit.isPending} onClick={confirmSubmit}>
            Submit exam
          </button>
        </div>
        {submit.error && <Alert tone="error">{submit.error.message}</Alert>}
      </article>
    </div>
  );
}

function Results({ exam }: { exam: MockExamView }) {
  const result = exam.result!;
  return (
    <div className="stack">
      <div className="page-head">
        <h1>Mock exam: {exam.examCode}</h1>
        <Link className="button ghost" to={`/mock-exams/new?certificationId=${exam.certificationId}`}>
          Take another
        </Link>
      </div>

      <section className="card stack">
        <p className="exam-score">
          {result.percent}% <span className="muted">({result.correct} of {result.total} correct)</span>
        </p>
        <p className="muted small">A practice score from your own questions, not a prediction of the real exam's result.</p>
        <ul className="plain-list stack">
          {result.domains.map((d) => (
            <li key={d.domainId} className="stack-tight">
              <div className="row-between">
                <span>{d.name}</span>
                <span className="muted small">
                  {d.correct} of {d.total} · {d.weight}% of the exam
                </span>
              </div>
              <ProgressBar value={d.correct} max={d.total} label={`${d.name} score`} />
            </li>
          ))}
        </ul>
      </section>

      <section className="stack">
        <h2>Questions</h2>
        {exam.questions.map((q, i) => (
          <ResultQuestion key={q.itemId} question={q} number={i + 1} />
        ))}
      </section>
    </div>
  );
}

function ResultQuestion({ question: q, number }: { question: MockQuestionView; number: number }) {
  return (
    <details className="card stack review-card" open={q.correct === false}>
      <summary className="exam-result-summary">
        <span className={q.correct ? "status-line completed" : "status-line missed"}>{q.correct ? "✓" : "✗"}</span>
        <span>
          {number}. {q.prompt}
        </span>
      </summary>
      <div className="options">
        {q.options.map((option, i) => {
          const state = q.correctOptions?.includes(i) ? "correct" : q.selected.includes(i) ? "wrong" : "";
          return (
            <div key={i} className={`option ${state}`}>
              <span className="option-letter">{letters[i]}</span>
              <span>
                {option}
                {q.selected.includes(i) && <span className="muted small"> (your answer)</span>}
              </span>
            </div>
          );
        })}
      </div>
      {q.selected.length === 0 && <p className="muted small">Not answered.</p>}
      {q.answer && <p>{q.answer}</p>}
      {q.sources && q.sources.length > 0 && (
        <p className="muted small">
          From{" "}
          {q.sources.map((s, i) => (
            <span key={i}>
              {i > 0 && ", "}
              <a href={`/api/documents/${s.documentId}/file#page=${s.page}`} target="_blank" rel="noreferrer">
                {s.documentTitle ?? "a deleted document"}, p. {s.page}
              </a>
            </span>
          ))}
        </p>
      )}
    </details>
  );
}
