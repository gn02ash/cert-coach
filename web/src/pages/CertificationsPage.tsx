import { Link } from "react-router";
import { useCertifications, useDeleteCertification } from "../api/hooks";
import { Alert, EmptyState, StatusBadge } from "../components/ui";

export function CertificationsPage() {
  const certifications = useCertifications();
  const remove = useDeleteCertification();

  if (certifications.isPending) return <div className="page-loading">Loading…</div>;
  if (certifications.error) return <p className="error-text">{certifications.error.message}</p>;

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Certifications</h1>
        <Link className="button primary" to="/certifications/new">
          Add certification
        </Link>
      </div>

      {remove.error && <Alert tone="error">{remove.error.message}</Alert>}

      {certifications.data.length === 0 && (
        <EmptyState title="No certifications yet">
          <p className="muted">Paste an exam's official skills outline to get started. A sample outline is one click away.</p>
          <Link className="button primary" to="/certifications/new">
            Add your first certification
          </Link>
        </EmptyState>
      )}

      <div className="grid">
        {certifications.data.map((c) => (
          <article key={c.id} className="card">
            <div className="row-between">
              <span className="exam-code">{c.examCode}</span>
              <StatusBadge status={c.status} replacedBy={c.replacedByExamCode} />
            </div>
            <h3>{c.name}</h3>
            <p className="muted small">
              {c.vendor} · {c.domainCount} domains · {c.objectiveCount} objectives
            </p>
            {(c.status === "Retired" || c.status === "Replaced") && (
              <p className="warning-text small">This exam is no longer offered. Check before you study for it.</p>
            )}
            <div className="actions">
              {c.activePlanId ? (
                <Link className="button primary small" to={`/plans/${c.activePlanId}`}>
                  View plan
                </Link>
              ) : (
                <Link className="button primary small" to={`/certifications/${c.id}/plan`}>
                  Create plan
                </Link>
              )}
              <Link className="button ghost small" to={`/certifications/${c.id}/practice`}>
                Practice items
              </Link>
              <Link className="button ghost small" to={`/certifications/${c.id}`}>
                Edit
              </Link>
              {!c.activePlanId && (
                <button
                  className="button ghost small danger"
                  onClick={() => confirm(`Delete ${c.examCode}?`) && remove.mutate(c.id)}
                >
                  Delete
                </button>
              )}
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}
