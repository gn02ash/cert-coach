import { useEffect, useRef } from "react";
import { Link } from "react-router";
import type { PlanDashboard } from "../api/client";
import { useDashboard, useRescheduleOverdue } from "../api/hooks";
import { SessionCard } from "../components/SessionCard";
import { EmptyState, KindBadge, ProgressBar, ShortfallNotice } from "../components/ui";
import { formatDay, formatLongDate } from "../lib/dates";

export function DashboardPage() {
  const dashboard = useDashboard();
  const reschedule = useRescheduleOverdue();
  const rolled = useRef(new Set<string>());

  // Sessions left planned on past days roll forward once per visit, so "today" is always accurate.
  useEffect(() => {
    for (const plan of dashboard.data?.plans ?? []) {
      if (plan.overdueSessions > 0 && !rolled.current.has(plan.planId)) {
        rolled.current.add(plan.planId);
        reschedule.mutate(plan.planId);
      }
    }
  }, [dashboard.data, reschedule]);

  if (dashboard.isPending) return <div className="page-loading">Loading…</div>;
  if (dashboard.error) return <p className="error-text">{dashboard.error.message}</p>;

  const { today, plans } = dashboard.data;

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Today</h1>
        <span className="muted">{formatLongDate(today)}</span>
      </div>

      {plans.length === 0 && (
        <EmptyState title="No active study plan yet">
          <p className="muted">Add a certification from its official skills outline, then build a plan around your exam date.</p>
          <Link className="button primary" to="/certifications">
            Go to certifications
          </Link>
        </EmptyState>
      )}

      {plans.map((plan) => (
        <PlanSection key={plan.planId} plan={plan} />
      ))}
    </div>
  );
}

function PlanSection({ plan }: { plan: PlanDashboard }) {
  const nextSession = plan.upcoming[0];
  const todays = plan.today;

  return (
    <section className="stack">
      <div className="plan-head">
        <div>
          <h2>
            {plan.examCode} <span className="muted thin">· {plan.certificationName}</span>
          </h2>
          <p className="muted">
            Exam {plan.daysUntilExam > 0 ? `in ${plan.daysUntilExam} days` : "today"}, {formatLongDate(plan.examDate)}
          </p>
        </div>
        <Link className="button ghost small" to={`/plans/${plan.planId}`}>
          Full plan
        </Link>
      </div>

      <ShortfallNotice shortfall={plan.shortfall} />

      <div className="grid">
        <div className="card span-2">
          <h3 className="card-title">Today's session{todays.length > 1 ? "s" : ""}</h3>
          {todays.length === 0 ? (
            <p className="muted">
              Rest day.{nextSession ? ` Next session: ${formatDay(nextSession.date)}, ${nextSession.title}.` : ""}
            </p>
          ) : (
            <div className="stack">
              {todays.map((s) => (
                <SessionCard key={s.id} planId={plan.planId} session={s} />
              ))}
            </div>
          )}
        </div>

        <div className="card">
          <h3 className="card-title">Progress</h3>
          <div className="big-number">{plan.progress.percentComplete}%</div>
          <ProgressBar value={plan.progress.completed} max={plan.progress.total} label="Sessions completed" />
          <p className="muted small">
            {plan.progress.completed} of {plan.progress.total} sessions done
            {plan.progress.missed > 0 ? ` · ${plan.progress.missed} missed and rescheduled` : ""}
          </p>
        </div>

        <div className="card">
          <h3 className="card-title">Coverage by domain</h3>
          <ul className="domain-list">
            {plan.domains.map((d) => (
              <li key={d.domainId}>
                <div className="row-between">
                  <span>{d.name}</span>
                  <span className="muted small">
                    {d.objectivesStudied}/{d.objectivesTotal}
                  </span>
                </div>
                <ProgressBar value={d.objectivesStudied} max={d.objectivesTotal} label={d.name} />
                <span className="muted tiny">{d.weight}% of the exam</span>
              </li>
            ))}
          </ul>
        </div>

        <div className="card span-2">
          <h3 className="card-title">Next 7 days</h3>
          {plan.upcoming.length === 0 ? (
            <p className="muted">Nothing scheduled in the next week.</p>
          ) : (
            <ul className="agenda">
              {plan.upcoming.map((s) => (
                <li key={s.id}>
                  <span className="agenda-date">{formatDay(s.date)}</span>
                  <KindBadge kind={s.kind} />
                  <span className="agenda-title">{s.title}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </section>
  );
}
