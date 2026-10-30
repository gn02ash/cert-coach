import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router";
import type { SearchHit } from "../api/client";
import { useCertifications, useSearch } from "../api/hooks";
import { Alert, EmptyState } from "../components/ui";
import { Highlight } from "../components/Highlight";
import { pageLabel } from "../lib/citations";

export function SearchPage() {
  const [params, setParams] = useSearchParams();
  const query = params.get("q") ?? "";
  const certificationId = params.get("certificationId") ?? "";
  const [draft, setDraft] = useState(query);
  const certifications = useCertifications();
  const search = useSearch(query, certificationId || undefined);

  function submit(e: FormEvent) {
    e.preventDefault();
    const next = new URLSearchParams();
    if (draft.trim()) next.set("q", draft.trim());
    if (certificationId) next.set("certificationId", certificationId);
    setParams(next);
  }

  function setCertification(id: string) {
    const next = new URLSearchParams(params);
    if (id) next.set("certificationId", id);
    else next.delete("certificationId");
    setParams(next);
  }

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1>Search your materials</h1>
          <p className="muted">Find the passages in your PDFs about a topic or question.</p>
        </div>
      </div>

      <form className="card search-bar" onSubmit={submit} role="search">
        <input
          type="search"
          aria-label="Search"
          placeholder="e.g. How does hybrid search merge rankings?"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          autoFocus
        />
        <select aria-label="Certification" value={certificationId} onChange={(e) => setCertification(e.target.value)}>
          <option value="">All documents</option>
          {certifications.data?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.examCode}
            </option>
          ))}
        </select>
        <button className="button primary" type="submit">
          Search
        </button>
      </form>

      {search.data && !search.data.semantic && (
        <Alert>
          <strong>Offline mode.</strong> Results match your words, not their meaning. Configure an embeddings provider for
          better results.
        </Alert>
      )}

      {search.isFetching && <div className="page-loading">Searching…</div>}
      {search.error && <Alert tone="error">{search.error.message}</Alert>}

      {search.data && search.data.hits.length === 0 && (
        <EmptyState title="Nothing found">
          <p className="muted">
            No passage matches. Try other words, or <Link to="/library">upload more documents</Link>.
          </p>
        </EmptyState>
      )}

      {search.data && search.data.hits.length > 0 && (
        <ol className="hit-list">
          {search.data.hits.map((hit) => (
            <HitCard key={hit.chunkId} hit={hit} query={search.data.query} />
          ))}
        </ol>
      )}
    </div>
  );
}

function HitCard({ hit, query }: { hit: SearchHit; query: string }) {
  const foundBy = [hit.vectorRank != null && "meaning", hit.textRank != null && "keywords"].filter(Boolean).join(" + ");

  return (
    <li className="card hit">
      <div className="row-between">
        <div>
          <div className="strong">{hit.documentTitle}</div>
          <div className="small muted">
            {pageLabel(hit.pageStart, hit.pageEnd)}
            {hit.headingPath && ` · ${hit.headingPath}`}
          </div>
        </div>
        <a
          className="button ghost small"
          href={`/api/documents/${hit.documentId}/file#page=${hit.pageStart}`}
          target="_blank"
          rel="noreferrer"
        >
          Open
        </a>
      </div>
      <p className="hit-text">
        <Highlight text={hit.text} query={query} />
      </p>
      <p className="tiny muted">Matched by {foundBy}</p>
    </li>
  );
}
