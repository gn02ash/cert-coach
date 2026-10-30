import type { ReactNode } from "react";
import type { CapacityShortfall, CertificationStatus, PlanWarning, SessionKind } from "../api/client";
import { formatLongDate } from "../lib/dates";

export function ProgressBar({ value, max, label }: { value: number; max: number; label?: string }) {
  const percent = max === 0 ? 0 : Math.round((value / max) * 100);
  return (
    <div className="progress" role="progressbar" aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100} aria-label={label}>
      <div className="progress-fill" style={{ width: `${percent}%` }} />
    </div>
  );
}

const kindLabels: Record<SessionKind, string> = { Learn: "Learn", Review: "Review", MockExam: "Mock exam" };

export const KindBadge = ({ kind }: { kind: SessionKind }) => (
  <span className={`badge kind-${kind.toLowerCase()}`}>{kindLabels[kind]}</span>
);

export function StatusBadge({ status, replacedBy }: { status: CertificationStatus; replacedBy?: string | null }) {
  if (status === "Active") return null;
  const text = status === "Replaced" ? `Replaced by ${replacedBy}` : status;
  return <span className={`badge status-${status.toLowerCase()}`}>{text}</span>;
}

export function Alert({ tone = "info", children }: { tone?: "info" | "warning" | "error"; children: ReactNode }) {
  return (
    <div className={`alert ${tone}`} role={tone === "error" ? "alert" : "status"}>
      {children}
    </div>
  );
}

export function Warnings({ warnings }: { warnings: PlanWarning[] | string[] }) {
  if (warnings.length === 0) return null;
  return (
    <Alert tone="warning">
      <ul className="plain-list">
        {warnings.map((w, i) => (
          <li key={i}>{typeof w === "string" ? w : w.message}</li>
        ))}
      </ul>
    </Alert>
  );
}

export function ShortfallNotice({ shortfall }: { shortfall: CapacityShortfall | null | undefined }) {
  if (!shortfall) return null;
  return (
    <Alert tone="warning">
      <strong>Not enough time before the exam.</strong> The plan is {shortfall.missingSessions} session
      {shortfall.missingSessions === 1 ? "" : "s"} short, so some sessions cover several objectives. To fix it, study about{" "}
      <strong>{shortfall.suggestedHoursPerWeek} h/week</strong> or move the exam to{" "}
      <strong>{formatLongDate(shortfall.suggestedExamDate)}</strong>.
    </Alert>
  );
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <h2>{title}</h2>
      {children}
    </div>
  );
}
