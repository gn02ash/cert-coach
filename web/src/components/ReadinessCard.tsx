import { Link } from "react-router";
import { useReadiness } from "../api/hooks";
import { ProgressBar } from "./ui";

const explanation =
  "Readiness = objectives you've recalled × your recent accuracy on practice questions. " +
  "Recent answers count more (their weight halves every 14 days), and accuracy is pulled toward 50% so a few lucky answers can't read as certain. " +
  "A domain needs 10 answered questions in the last 90 days for a score. It's an estimate from your own questions, not a pass prediction.";

/** Readiness per exam domain, with the formula one tap away. */
export function ReadinessCard({ certificationId, className = "card" }: { certificationId: string; className?: string }) {
  const readiness = useReadiness(certificationId);
  const report = readiness.data;

  return (
    <div className={className}>
      <div className="row-between">
        <h3 className="card-title">Readiness</h3>
        {report?.overall != null && <span className="big-number small-big">{report.overall}%</span>}
      </div>
      {readiness.error && <p className="error-text small">{readiness.error.message}</p>}
      {report && (
        <>
          <ul className="domain-list">
            {report.domains.map((d) => (
              <li key={d.domainId}>
                <div className="row-between">
                  <span>{d.name}</span>
                  <span className="muted small">{d.score != null ? `${d.score}%` : "Not enough data"}</span>
                </div>
                <ProgressBar value={d.score ?? 0} max={100} label={`${d.name} readiness`} />
                <span className="muted tiny">
                  {d.objectivesCovered}/{d.objectivesTotal} objectives recalled · {d.answers} {d.answers === 1 ? "answer" : "answers"}
                  {d.accuracy != null ? ` · ${d.accuracy}% right` : ""}
                </span>
              </li>
            ))}
          </ul>
          {report.overall == null && (
            <p className="muted small">
              The overall score appears once every domain has 10 answers.{" "}
              <Link to={`/certifications/${certificationId}/practice`}>Practice items</Link>
            </p>
          )}
          <details className="muted small">
            <summary>How this is worked out</summary>
            <p>{explanation}</p>
          </details>
        </>
      )}
    </div>
  );
}
