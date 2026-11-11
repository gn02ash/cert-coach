import { useState, type FormEvent } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import type { CertificationDetail, ResourceView, SuggestedResource } from "../api/client";
import {
  useCertification,
  useDeleteResource,
  useResources,
  useResourceSuggestions,
  useSaveResource,
  useSetResourceDone,
} from "../api/hooks";
import { Alert, EmptyState, Warnings } from "../components/ui";

const kindLabels: Record<ResourceView["kind"], string> = { LearnModule: "Learn module", Video: "Video", Link: "Link" };

const minutes = (m: number | null | undefined) => (m ? ` · ${m} min` : "");

/** Study resources for a certification: suggestions per objective, and the ones saved, to tick off. */
export function ResourcesPage() {
  const { id = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const certification = useCertification(id);
  const resources = useResources(id);
  const objectiveId = params.get("objective") ?? "";

  if (certification.isPending || resources.isPending) return <div className="page-loading">Loading…</div>;
  if (certification.error) return <p className="error-text">{certification.error.message}</p>;
  if (resources.error) return <p className="error-text">{resources.error.message}</p>;

  const objectives = certification.data.domains.flatMap((d) => d.skillAreas.flatMap((s) => s.objectives.map((o) => ({ ...o, domain: d.name }))));
  const saved = resources.data;
  const groups = [
    ...objectives.map((o) => ({ key: o.id, title: o.text, items: saved.filter((r) => r.objectiveId === o.id) })),
    { key: "none", title: "Whole certification", items: saved.filter((r) => !r.objectiveId) },
  ].filter((g) => g.items.length > 0);

  return (
    <div className="stack">
      <div className="page-head">
        <h1>{certification.data.examCode} resources</h1>
        <Link to="/certifications" className="button ghost">
          Back
        </Link>
      </div>
      <p className="muted">
        {saved.length} saved · {saved.filter((r) => r.done).length} done
      </p>

      <section className="card stack">
        <h2 className="card-title">Find resources</h2>
        <label>
          Objective
          <select value={objectiveId} onChange={(e) => setParams(e.target.value ? { objective: e.target.value } : {}, { replace: true })}>
            <option value="">Choose an objective…</option>
            {certification.data.domains.map((d) => (
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
        {objectiveId && <Suggestions key={objectiveId} certification={certification.data} objectiveId={objectiveId} />}
      </section>

      {groups.length === 0 ? (
        <EmptyState title="No resources saved yet">Choose an objective above to find Microsoft Learn modules and videos, or add a link of your own.</EmptyState>
      ) : (
        groups.map((g) => (
          <section key={g.key} className="card stack">
            <h2 className="objective-title">{g.title}</h2>
            <ul className="plain-list stack">
              {g.items.map((r) => (
                <SavedRow key={r.id} resource={r} />
              ))}
            </ul>
          </section>
        ))
      )}
    </div>
  );
}

function Suggestions({ certification, objectiveId }: { certification: CertificationDetail; objectiveId: string }) {
  const suggestions = useResourceSuggestions(objectiveId);
  const save = useSaveResource();
  const [title, setTitle] = useState("");
  const [url, setUrl] = useState("");

  if (suggestions.isPending) return <p className="muted small">Looking for resources…</p>;
  if (suggestions.error) return <Alert tone="error">{suggestions.error.message}</Alert>;

  const { learn, videos, youTubeAvailable, youTubeSearchUrl, notes } = suggestions.data;
  const keep = (s: SuggestedResource) =>
    save.mutate({
      certificationId: certification.id,
      objectiveId,
      kind: s.kind,
      title: s.title,
      url: s.url,
      source: s.source,
      summary: s.summary,
      durationMinutes: s.durationMinutes,
    });
  const addLink = (e: FormEvent) => {
    e.preventDefault();
    save.mutate(
      { certificationId: certification.id, objectiveId, kind: "Link", title, url, source: null, summary: null, durationMinutes: null },
      {
        onSuccess: () => {
          setTitle("");
          setUrl("");
        },
      },
    );
  };
  const list = (items: SuggestedResource[]) => (
    <ul className="plain-list stack">
      {items.map((s) => (
        <li key={s.url} className="resource-row">
          <div className="resource-text">
            <a href={s.url} target="_blank" rel="noreferrer">
              {s.title}
            </a>
            <span className="muted small">
              {s.source}
              {minutes(s.durationMinutes)}
            </span>
            {s.summary && <span className="muted small resource-summary">{s.summary}</span>}
          </div>
          <button type="button" className="button ghost small" disabled={s.saved || save.isPending} onClick={() => keep(s)}>
            {s.saved ? "Saved" : "Save"}
          </button>
        </li>
      ))}
    </ul>
  );

  return (
    <div className="stack">
      <Warnings warnings={notes} />

      <div className="stack">
        <h3 className="objective-title">Microsoft Learn</h3>
        {learn.length > 0 ? list(learn) : <p className="muted small">No Learn module matches this objective closely enough.</p>}
      </div>

      <div className="stack">
        <h3 className="objective-title">YouTube</h3>
        {videos.length > 0 && list(videos)}
        <p className="muted small">
          {!youTubeAvailable && "Set YOUTUBE_API_KEY to see videos here. "}
          <a href={youTubeSearchUrl} target="_blank" rel="noreferrer">
            Search YouTube for this objective
          </a>
          {!youTubeAvailable && ", then paste a video you want to keep below."}
        </p>
      </div>

      <form className="stack" onSubmit={addLink}>
        <h3 className="objective-title">Add a link</h3>
        <div className="form-grid">
          <label>
            Title
            <input required value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="span-2">
            Address
            <input required type="url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://…" />
          </label>
        </div>
        {save.error && <Alert tone="error">{save.error.message}</Alert>}
        <div className="actions">
          <button className="button ghost" disabled={save.isPending || !title.trim() || !url.trim()}>
            Add link
          </button>
        </div>
      </form>
    </div>
  );
}

function SavedRow({ resource: r }: { resource: ResourceView }) {
  const setDone = useSetResourceDone();
  const remove = useDeleteResource(r.certificationId);

  return (
    <li className={`resource-row${r.done ? " done" : ""}`}>
      <label className="resource-check">
        <input
          type="checkbox"
          checked={r.done}
          disabled={setDone.isPending}
          onChange={(e) => setDone.mutate({ id: r.id, done: e.target.checked })}
          aria-label={`Done: ${r.title}`}
        />
      </label>
      <div className="resource-text">
        <a href={r.url} target="_blank" rel="noreferrer">
          {r.title}
        </a>
        <span className="muted small">
          {kindLabels[r.kind]} · {r.source}
          {minutes(r.durationMinutes)}
        </span>
      </div>
      <button
        type="button"
        className="button ghost small danger"
        disabled={remove.isPending}
        onClick={() => confirm("Remove this resource?") && remove.mutate(r.id)}
      >
        Remove
      </button>
    </li>
  );
}
