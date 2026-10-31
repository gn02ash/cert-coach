import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router";
import type { CertificationDetail, GenerationJobView, PracticeItemView } from "../api/client";
import {
  useAiStatus,
  useCancelGeneration,
  useCertification,
  useGenerations,
  useStartGeneration,
  useCreatePracticeItem,
  useDeletePracticeItem,
  usePracticeItems,
  useSetPracticeItemStatus,
} from "../api/hooks";
import { Alert, EmptyState, ProgressBar } from "../components/ui";

const kindLabels: Record<PracticeItemView["kind"], string> = {
  SingleChoice: "Question",
  MultipleChoice: "Choose several",
  Flashcard: "Flashcard",
};

const letters = "ABCDEFGH";

const plural = (count: number, noun: string) => `${count} ${noun}${count === 1 ? "" : "s"}`;

/** Items under their objective in outline order, then the ones whose objective left the outline. */
function groupByObjective(certification: CertificationDetail, items: PracticeItemView[]) {
  const groups = certification.domains.flatMap((d) =>
    d.skillAreas.flatMap((s) =>
      s.objectives.map((o) => ({ key: o.id, domain: d.name, title: o.text, items: items.filter((i) => i.objectiveId === o.id) })),
    ),
  );
  const unassigned = items.filter((i) => !i.objectiveId);
  return [
    ...groups.filter((g) => g.items.length > 0),
    ...(unassigned.length > 0 ? [{ key: "none", domain: "", title: "Not linked to an objective", items: unassigned }] : []),
  ];
}

export function PracticeItemsPage() {
  const { id = "" } = useParams();
  const certification = useCertification(id);
  const items = usePracticeItems(id);

  if (certification.isPending || items.isPending) return <div className="page-loading">Loading…</div>;
  if (certification.error) return <p className="error-text">{certification.error.message}</p>;
  if (items.error) return <p className="error-text">{items.error.message}</p>;

  const all = items.data;
  const groups = groupByObjective(certification.data, all);
  const active = all.filter((i) => i.status === "Active").length;

  return (
    <div className="stack">
      <div className="page-head">
        <h1>{certification.data.examCode} practice items</h1>
        <Link to="/certifications" className="button ghost">
          Back
        </Link>
      </div>
      <p className="muted">
        {plural(all.length, "item")} · {active} active · {all.filter((i) => i.dueOn === null).length} not reviewed yet
      </p>

      <GeneratePanel certification={certification.data} />
      <FlashcardForm certification={certification.data} />

      {all.length === 0 ? (
        <EmptyState title="No practice items yet">Generate some from your documents, or write a flashcard.</EmptyState>
      ) : (
        groups.map((g) => (
          <section key={g.key} className="card stack">
            <div>
              {g.domain && <p className="muted small">{g.domain}</p>}
              <h2 className="objective-title">{g.title}</h2>
            </div>
            <ul className="plain-list stack">
              {g.items.map((item) => (
                <ItemRow key={item.id} item={item} />
              ))}
            </ul>
          </section>
        ))
      )}
    </div>
  );
}

function GeneratePanel({ certification }: { certification: CertificationDetail }) {
  const ai = useAiStatus();
  const runs = useGenerations(certification.id);
  const start = useStartGeneration();
  const cancel = useCancelGeneration();
  const [scope, setScope] = useState("");
  const [perObjective, setPerObjective] = useState(5);

  const usesAi = ai.data?.chatAvailable ?? false;
  const latest = runs.data?.[0];
  const active = latest && (latest.status === "Queued" || latest.status === "Running") ? latest : undefined;
  const objectiveCount = (domainId?: string) =>
    certification.domains.filter((d) => !domainId || d.id === domainId).reduce((n, d) => n + d.skillAreas.reduce((m, s) => m + s.objectives.length, 0), 0);
  const [kind, id] = scope.split(":");
  const objectives = kind === "objective" ? 1 : objectiveCount(kind === "domain" ? id : undefined);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    start.mutate({
      certificationId: certification.id,
      domainId: kind === "domain" ? id : null,
      objectiveId: kind === "objective" ? id : null,
      perObjective,
    });
  };

  return (
    <form className="card stack" onSubmit={submit}>
      <h2 className="card-title">Generate questions</h2>
      <p className="muted small">
        {usesAi
          ? "Exam-style questions and flashcards written from your documents, with a citation for each. Objectives your documents don't cover get questions from the model's own knowledge, marked \"Not from your materials\"."
          : "Offline mode: fill-in-the-blank questions and flashcards made from sentences in your documents. Set AI_CHAT_PROVIDER for exam-style questions."}
      </p>

      {active ? (
        <div className="stack" aria-live="polite">
          <ProgressBar value={active.objectivesDone} max={active.objectives} label="Objectives done" />
          <div className="row-between">
            <span className="muted small">
              {active.status === "Queued" ? "Waiting to start…" : `Objective ${Math.min(active.objectivesDone + 1, active.objectives)} of ${active.objectives}`} ·{" "}
              {active.itemsCreated} items so far
            </span>
            <button type="button" className="button ghost small" disabled={cancel.isPending} onClick={() => cancel.mutate(active.id)}>
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <>
          <div className="form-grid">
            <label className="span-2">
              For
              <select value={scope} onChange={(e) => setScope(e.target.value)}>
                <option value="">Every objective ({objectiveCount()})</option>
                {certification.domains.map((d) => (
                  <optgroup key={d.id} label={d.name}>
                    <option value={`domain:${d.id}`}>Every objective in this domain ({objectiveCount(d.id)})</option>
                    {d.skillAreas.flatMap((s) =>
                      s.objectives.map((o) => (
                        <option key={o.id} value={`objective:${o.id}`}>
                          {o.text}
                        </option>
                      )),
                    )}
                  </optgroup>
                ))}
              </select>
            </label>
            <label>
              Items per objective
              <input type="number" min={1} max={10} value={perObjective} onChange={(e) => setPerObjective(Number(e.target.value))} />
            </label>
          </div>
          {usesAi && <p className="muted small">One model call per objective: {objectives} {objectives === 1 ? "call" : "calls"}.</p>}
          {start.error && <Alert tone="error">{start.error.message}</Alert>}
          <div className="actions">
            <button className="button primary" disabled={start.isPending}>
              Generate
            </button>
          </div>
        </>
      )}

      {latest && !active && <RunSummary run={latest} />}
    </form>
  );
}

function RunSummary({ run }: { run: GenerationJobView }) {
  if (run.status === "Failed") return <Alert tone="error">The last run stopped: {run.error}</Alert>;
  const parts = [
    `${run.status === "Cancelled" ? "Cancelled after" : "Last run:"} ${plural(run.itemsCreated, "new item")} for ${plural(run.objectivesDone, "objective")}`,
    run.itemsRejected > 0 ? `${run.itemsRejected} dropped as duplicates or invalid` : "",
    run.objectivesWithoutMaterial > 0
      ? `${plural(run.objectivesWithoutMaterial, "objective")} not covered by your documents${run.usesAi ? " (written from the model's knowledge)" : ""}`
      : "",
  ].filter(Boolean);
  return <p className="muted small">{parts.join(" · ")}.</p>;
}

function FlashcardForm({ certification }: { certification: CertificationDetail }) {
  const create = useCreatePracticeItem();
  const [objectiveId, setObjectiveId] = useState("");
  const [front, setFront] = useState("");
  const [back, setBack] = useState("");

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate(
      { certificationId: certification.id, objectiveId: objectiveId || null, kind: "Flashcard", prompt: front, options: null, correctOptions: null, answer: back },
      {
        onSuccess: () => {
          setFront("");
          setBack("");
        },
      },
    );
  };

  return (
    <form className="card stack" onSubmit={submit}>
      <h2 className="card-title">Write a flashcard</h2>
      <label>
        Objective
        <select value={objectiveId} onChange={(e) => setObjectiveId(e.target.value)}>
          <option value="">None</option>
          {certification.domains.map((d) => (
            <optgroup key={d.id} label={d.name}>
              {d.skillAreas.flatMap((s) =>
                s.objectives.map((o) => (
                  <option key={o.id} value={o.id}>
                    {o.text}
                  </option>
                )),
              )}
            </optgroup>
          ))}
        </select>
      </label>
      <div className="form-grid">
        <label className="span-2">
          Front
          <textarea rows={2} required value={front} onChange={(e) => setFront(e.target.value)} />
        </label>
        <label className="span-2">
          Back
          <textarea rows={2} required value={back} onChange={(e) => setBack(e.target.value)} />
        </label>
      </div>
      {create.error && <Alert tone="error">{create.error.message}</Alert>}
      <div className="actions">
        <button className="button primary" disabled={create.isPending || !front.trim() || !back.trim()}>
          Add flashcard
        </button>
      </div>
    </form>
  );
}

function ItemRow({ item }: { item: PracticeItemView }) {
  const setStatus = useSetPracticeItemStatus();
  const remove = useDeletePracticeItem(item.certificationId);
  const suspended = item.status === "Suspended";

  return (
    <li className={`practice-item${suspended ? " suspended" : ""}`}>
      <details>
        <summary>
          <span className="badge">{kindLabels[item.kind]}</span>
          {item.origin === "ModelKnowledge" && <span className="badge warning">Not from your materials</span>}
          {suspended && <span className="badge">Suspended</span>}
          <span className="practice-prompt">{item.prompt}</span>
        </summary>
        <div className="stack practice-body">
          {item.options.length > 0 && (
            <ol className="plain-list">
              {item.options.map((option, i) => (
                <li key={i} className={item.correctOptions.includes(i) ? "correct-option" : undefined}>
                  {letters[i]}. {option}
                  {item.correctOptions.includes(i) && <span className="visually-hidden"> (correct)</span>}
                </li>
              ))}
            </ol>
          )}
          {item.answer && <p>{item.answer}</p>}
          {item.sources.length > 0 && (
            <p className="muted small">
              From{" "}
              {item.sources.map((s, i) => (
                <span key={i}>
                  {i > 0 && ", "}
                  <a href={`/api/documents/${s.documentId}/file#page=${s.page}`} target="_blank" rel="noreferrer">
                    {s.documentTitle ?? "a deleted document"}, p. {s.page}
                  </a>
                </span>
              ))}
            </p>
          )}
          <div className="actions">
            <button
              type="button"
              className="button ghost small"
              disabled={setStatus.isPending}
              onClick={() => setStatus.mutate({ id: item.id, status: suspended ? "Active" : "Suspended" })}
            >
              {suspended ? "Resume" : "Suspend"}
            </button>
            <button
              type="button"
              className="button ghost small danger"
              disabled={remove.isPending}
              onClick={() => confirm("Delete this item and its review history?") && remove.mutate(item.id)}
            >
              Delete
            </button>
          </div>
        </div>
      </details>
    </li>
  );
}
