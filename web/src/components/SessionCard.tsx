import { Link } from "react-router";
import type { SessionView } from "../api/client";
import { useSessionAction } from "../api/hooks";
import { KindBadge } from "./ui";

/** A session with its objectives and, while it is still planned, Done and Missed actions. */
export function SessionCard({ planId, session }: { planId: string; session: SessionView }) {
  const action = useSessionAction();
  const act = (kind: "complete" | "miss") => action.mutate({ planId, sessionId: session.id!, action: kind });

  return (
    <article className={`session status-${session.status.toLowerCase()}`}>
      <div className="session-head">
        <KindBadge kind={session.kind} />
        <span className="muted">
          {session.durationMinutes} min{session.domainName ? ` · ${session.domainName}` : ""}
        </span>
      </div>
      <h3 className="session-title">{session.title}</h3>
      {session.objectives.length > 1 && (
        <ul className="objectives">
          {session.objectives.map((o) => (
            <li key={o.id}>{o.text}</li>
          ))}
        </ul>
      )}
      {session.objectives.length === 1 && <p className="muted small">{session.objectives[0]!.skillArea}</p>}
      {session.kind === "MockExam" && <p className="muted small">Timed practice across every domain.</p>}
      {session.objectives.length > 0 && (
        <div className="session-links">
          <Link className="small" to={`/search?q=${encodeURIComponent(session.objectives.map((o) => o.text).join(" "))}`}>
            Find in my materials
          </Link>
          <Link className="small" to={`/review?objectives=${session.objectives.map((o) => o.id).join(",")}`}>
            Quiz me
          </Link>
        </div>
      )}

      {session.status === "Planned" ? (
        <div className="actions">
          <button className="button primary" disabled={action.isPending} onClick={() => act("complete")}>
            Done
          </button>
          <button className="button ghost" disabled={action.isPending} onClick={() => act("miss")}>
            Missed it
          </button>
        </div>
      ) : (
        <p className={`status-line ${session.status.toLowerCase()}`}>
          {session.status === "Completed" ? "✓ Done" : "Missed: moved to a later day"}
        </p>
      )}
      {action.error && <p className="error-text">{action.error.message}</p>}
    </article>
  );
}
