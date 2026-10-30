import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import type { DayOfWeek, PlanRequest } from "../api/client";
import { useCertification, useCreatePlan, usePreviewPlan } from "../api/hooks";
import { Alert, KindBadge, ShortfallNotice, Warnings } from "../components/ui";
import { addDays, formatDay, toIsoDate } from "../lib/dates";

const days: DayOfWeek[] = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

export function NewPlanPage() {
  const { id } = useParams();
  const certification = useCertification(id);
  const preview = usePreviewPlan();
  const create = useCreatePlan();
  const navigate = useNavigate();

  const today = toIsoDate(new Date());
  const [startDate, setStartDate] = useState(today);
  const [examDate, setExamDate] = useState(addDays(today, 56));
  const [hours, setHours] = useState(5);
  const [studyDays, setStudyDays] = useState<DayOfWeek[]>(days.slice(0, 5));
  const [buffer, setBuffer] = useState(10);

  const request = useMemo<PlanRequest>(
    () => ({ certificationId: id!, startDate, examDate, hoursPerWeek: hours, studyDays, reviewBufferPercent: buffer }),
    [id, startDate, examDate, hours, studyDays, buffer],
  );

  // Preview as the learner adjusts the settings, after a short pause.
  const { mutate: runPreview } = preview;
  useEffect(() => {
    if (!id || studyDays.length === 0 || !examDate || hours <= 0) return;
    const timer = setTimeout(() => runPreview(request), 300);
    return () => clearTimeout(timer);
  }, [id, request, studyDays.length, examDate, hours, runPreview]);

  const toggleDay = (day: DayOfWeek) =>
    setStudyDays((current) => (current.includes(day) ? current.filter((d) => d !== day) : days.filter((d) => d === day || current.includes(d))));

  const counts = preview.data?.sessions.reduce<Record<string, number>>((acc, s) => ({ ...acc, [s.kind]: (acc[s.kind] ?? 0) + 1 }), {});

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Plan for {certification.data?.examCode ?? "…"}</h1>
        <Link to="/certifications" className="button ghost">
          Cancel
        </Link>
      </div>

      <div className="grid">
        <div className="card stack">
          <h2 className="card-title">Your schedule</h2>
          <label>
            Start date
            <input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
          </label>
          <label>
            Exam date
            <input type="date" value={examDate} min={addDays(startDate, 1)} onChange={(e) => setExamDate(e.target.value)} />
          </label>
          <label>
            Hours per week: <strong>{hours}</strong>
            <input type="range" min={0.5} max={15} step={0.5} value={hours} onChange={(e) => setHours(Number(e.target.value))} />
          </label>
          <fieldset className="days">
            <legend>Study days</legend>
            {days.map((day) => (
              <button
                type="button"
                key={day}
                className={`day ${studyDays.includes(day) ? "on" : ""}`}
                aria-pressed={studyDays.includes(day)}
                onClick={() => toggleDay(day)}
              >
                {day.slice(0, 3)}
              </button>
            ))}
          </fieldset>
          <label>
            Review buffer before the exam: <strong>{buffer}%</strong>
            <input type="range" min={0} max={30} step={5} value={buffer} onChange={(e) => setBuffer(Number(e.target.value))} />
          </label>
        </div>

        <div className="card span-2 stack">
          <h2 className="card-title">Preview</h2>
          {studyDays.length === 0 && <Alert tone="warning">Pick at least one study day.</Alert>}
          {preview.error && <Alert tone="error">{preview.error.message}</Alert>}
          {preview.data && (
            <>
              <p>
                <strong>{preview.data.sessionsPerWeek}</strong> sessions a week of <strong>{preview.data.sessionMinutes} min</strong>,{" "}
                {preview.data.sessions.length} in total: {counts?.Learn ?? 0} learn, {counts?.Review ?? 0} review, {counts?.MockExam ?? 0}{" "}
                mock exam{counts?.MockExam === 1 ? "" : "s"}.
              </p>
              <ShortfallNotice shortfall={preview.data.shortfall} />
              <Warnings warnings={preview.data.warnings.filter((w) => w.code !== "ObjectivesPacked" || !preview.data?.shortfall)} />
              <ul className="agenda scroll">
                {preview.data.sessions.map((s, i) => (
                  <li key={i}>
                    <span className="agenda-date">{formatDay(s.date)}</span>
                    <KindBadge kind={s.kind} />
                    <span className="agenda-title">{s.title}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      </div>

      {create.error && <Alert tone="error">{create.error.message}</Alert>}
      <div className="actions">
        <button
          className="button primary"
          disabled={!preview.data || create.isPending || studyDays.length === 0}
          onClick={() => create.mutate(request, { onSuccess: () => navigate("/") })}
        >
          Start this plan
        </button>
      </div>
    </div>
  );
}
