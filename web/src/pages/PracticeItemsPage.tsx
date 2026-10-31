import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router";
import type { CertificationDetail, PracticeItemView } from "../api/client";
import {
  useCertification,
  useCreatePracticeItem,
  useDeletePracticeItem,
  usePracticeItems,
  useSetPracticeItemStatus,
} from "../api/hooks";
import { Alert, EmptyState } from "../components/ui";

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

      <FlashcardForm certification={certification.data} />

      {all.length === 0 ? (
        <EmptyState title="No practice items yet">Write a flashcard above to start.</EmptyState>
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
