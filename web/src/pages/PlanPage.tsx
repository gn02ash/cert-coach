import { Link, useParams } from "react-router";
import { useArchivePlan, usePlan, useSessionAction } from "../api/hooks";
import { Alert, KindBadge, ShortfallNotice } from "../components/ui";
import { formatDay, formatLongDate, groupByWeek, parseDate, toIsoDate } from "../lib/dates";

export function PlanPage() {
  const { id } = useParams();
  const plan = usePlan(id);
  const action = useSessionAction();
  const archive = useArchivePlan();
  const today = toIsoDate(new Date());

  if (plan.isPending) return <div className="page-loading">Loading…</div>;
  if (plan.error) return <p className="error-text">{plan.error.message}</p>;

  const p = plan.data;
  const active = p.status === "Active";
  const weeks = groupByWeek(p.sessions, (s) => s.date);

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1>
            {p.examCode} <span className="muted thin">· {p.certificationName}</span>
          </h1>
          <p className="muted">
            {formatLongDate(p.startDate)} → exam {formatLongDate(p.examDate)} · {p.hoursPerWeek} h/week on{" "}
            {p.studyDays.map((d) => d.slice(0, 3)).join(", ")}
          </p>
        </div>
        {active ? (
          <button
            className="button ghost"
            disabled={archive.isPending}
            onClick={() => confirm("Archive this plan? You can then edit the outline or start a new plan.") && archive.mutate(p.id)}
          >
            Archive plan
          </button>
        ) : (
          <span className="badge">Archived</span>
        )}
      </div>

      <ShortfallNotice shortfall={p.shortfall} />
      {(action.error || archive.error) && <Alert tone="error">{(action.error ?? archive.error)!.message}</Alert>}

      {weeks.map(({ week, items }) => (
        <section key={week} className="card">
          <h3 className="card-title">Week of {parseDate(week).toLocaleDateString(undefined, { month: "long", day: "numeric" })}</h3>
          <ul className="schedule">
            {items.map((s) => (
              <li key={s.id} className={`status-${s.status.toLowerCase()} ${s.date === today ? "is-today" : ""}`}>
                <span className="agenda-date">{formatDay(s.date)}</span>
                <KindBadge kind={s.kind} />
                <span className="agenda-title">
                  {s.title}
                  {s.domainName && s.kind === "Learn" && <span className="muted small"> · {s.domainName}</span>}
                </span>
                <span className="schedule-actions">
                  {s.status === "Planned" && active ? (
                    <>
                      <button
                        className="button ghost small"
                        disabled={action.isPending}
                        onClick={() => action.mutate({ planId: p.id, sessionId: s.id!, action: "complete" })}
                      >
                        Done
                      </button>
                      {s.date <= today && (
                        <button
                          className="button ghost small"
                          disabled={action.isPending}
                          onClick={() => action.mutate({ planId: p.id, sessionId: s.id!, action: "miss" })}
                        >
                          Missed
                        </button>
                      )}
                    </>
                  ) : (
                    <span className={`pill ${s.status.toLowerCase()}`}>{s.status === "Completed" ? "Done" : s.status}</span>
                  )}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ))}

      <Link to="/" className="button ghost">
        Back to today
      </Link>
    </div>
  );
}
