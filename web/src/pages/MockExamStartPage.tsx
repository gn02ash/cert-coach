import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useCertification, useMockExams, useStartMockExam } from "../api/hooks";
import { Alert } from "../components/ui";
import { formatLongDate } from "../lib/dates";

/** Settings for a new mock exam, with past results. ?sessionId=… ties it to a plan's mock exam session. */
export function MockExamStartPage() {
  const [params] = useSearchParams();
  const certificationId = params.get("certificationId") ?? undefined;
  const sessionId = params.get("sessionId") ?? undefined;
  const certification = useCertification(certificationId);
  const history = useMockExams(certificationId);
  const start = useStartMockExam();
  const navigate = useNavigate();
  const [questions, setQuestions] = useState(40);
  const [minutes, setMinutes] = useState(60);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    start.mutate(
      { certificationId: certificationId ?? null, sessionId: sessionId ?? null, questionCount: questions, durationMinutes: minutes },
      { onSuccess: (exam) => navigate(`/mock-exams/${exam.id}`) },
    );
  };

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Mock exam{certification.data ? `: ${certification.data.examCode}` : ""}</h1>
      </div>

      <form className="card stack" onSubmit={submit}>
        <p className="muted small">
          Questions are drawn from your practice items in proportion to each domain's weight on the exam. There is no feedback until you
          submit, and the exam submits itself when the time runs out.
        </p>
        <div className="form-grid">
          <label>
            Questions
            <input type="number" min={5} max={100} value={questions} onChange={(e) => setQuestions(Number(e.target.value))} />
          </label>
          <label>
            Minutes
            <input type="number" min={5} max={240} value={minutes} onChange={(e) => setMinutes(Number(e.target.value))} />
          </label>
        </div>
        {start.error && <Alert tone="error">{start.error.message}</Alert>}
        <div className="actions">
          <button className="button primary" disabled={start.isPending}>
            Start the exam
          </button>
        </div>
      </form>

      {history.data && history.data.length > 0 && (
        <section className="card stack">
          <h2 className="card-title">Past exams</h2>
          <ul className="plain-list stack">
            {history.data.map((e) => (
              <li key={e.id} className="row-between">
                <Link to={`/mock-exams/${e.id}`}>{formatLongDate(e.startedAt.slice(0, 10))}</Link>
                <span className="muted">
                  {e.status === "Submitted" ? `${e.percent}% of ${e.questions}` : "In progress"}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
