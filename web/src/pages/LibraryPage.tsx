import { useRef, useState } from "react";
import type { DocumentStatus, DocumentSummary } from "../api/client";
import {
  useAiStatus,
  useCertifications,
  useDeleteDocument,
  useDocuments,
  useRetryDocument,
  useUpdateDocument,
  useUploadDocument,
} from "../api/hooks";
import { Alert, EmptyState } from "../components/ui";

const statusLabels: Record<DocumentStatus, string> = {
  Pending: "Queued",
  Processing: "Processing",
  Ready: "Ready",
  Failed: "Failed",
};

function formatSize(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function LibraryPage() {
  const documents = useDocuments();
  const certifications = useCertifications();
  const ai = useAiStatus();
  const uploadDocument = useUploadDocument();
  const input = useRef<HTMLInputElement>(null);
  const [certificationId, setCertificationId] = useState("");
  const [uploadErrors, setUploadErrors] = useState<string[]>([]);
  const [uploading, setUploading] = useState(0);

  async function uploadFiles(files: FileList | null) {
    if (!files || files.length === 0) return;
    setUploadErrors([]);
    setUploading(files.length);

    // One at a time keeps the order predictable and the server's memory flat.
    for (const file of Array.from(files)) {
      try {
        await uploadDocument.mutateAsync({ file, certificationId: certificationId || undefined });
      } catch (error) {
        setUploadErrors((errors) => [...errors, `${file.name}: ${(error as Error).message}`]);
      }
      setUploading((n) => n - 1);
    }

    if (input.current) input.current.value = "";
  }

  if (documents.isPending) return <div className="page-loading">Loading…</div>;
  if (documents.error) return <p className="error-text">{documents.error.message}</p>;

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1>Library</h1>
          <p className="muted">Your study PDFs. They are split into passages you can search and ask questions about.</p>
        </div>
      </div>

      <div className="card upload-bar">
        <input
          ref={input}
          id="pdf-upload"
          className="visually-hidden"
          type="file"
          accept="application/pdf,.pdf"
          multiple
          disabled={uploading > 0}
          onChange={(e) => void uploadFiles(e.target.files)}
        />
        <label htmlFor="pdf-upload" className="button primary" aria-disabled={uploading > 0}>
          {uploading > 0 ? `Uploading ${uploading}…` : "Upload PDFs"}
        </label>
        <label className="small muted">
          For{" "}
          <select value={certificationId} onChange={(e) => setCertificationId(e.target.value)}>
            <option value="">any certification</option>
            {certifications.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.examCode}
              </option>
            ))}
          </select>
        </label>
        <span className="small muted">PDF only, up to 50 MB. Scanned PDFs without a text layer aren't supported yet.</span>
      </div>

      {ai.data && !ai.data.semanticSearch && (
        <Alert>
          <strong>Offline mode.</strong> Passages are matched by shared words. Configure an embeddings provider for search
          that understands meaning.
        </Alert>
      )}

      {uploadErrors.length > 0 && (
        <Alert tone="error">
          <ul className="plain-list">
            {uploadErrors.map((e) => (
              <li key={e}>{e}</li>
            ))}
          </ul>
        </Alert>
      )}

      {documents.data.length === 0 ? (
        <EmptyState title="No documents yet">
          <p className="muted">Upload study guides, books or your own notes as PDFs.</p>
        </EmptyState>
      ) : (
        <ul className="doc-list">
          {documents.data.map((d) => (
            <DocumentRow key={d.id} document={d} certifications={certifications.data ?? []} />
          ))}
        </ul>
      )}
    </div>
  );
}

function DocumentRow({
  document: d,
  certifications,
}: {
  document: DocumentSummary;
  certifications: { id: string; examCode: string }[];
}) {
  const update = useUpdateDocument();
  const retry = useRetryDocument();
  const remove = useDeleteDocument();
  const error = update.error ?? retry.error ?? remove.error;

  function rename() {
    const title = prompt("Document title", d.title)?.trim();
    if (title && title !== d.title) update.mutate({ id: d.id, request: { title, certificationId: d.certificationId } });
  }

  return (
    <li className="card doc">
      <div className="stack thin">
        <div className="doc-title">{d.title}</div>
        <div className="doc-meta small muted">
          <span className={`badge doc-status-${d.status.toLowerCase()}`}>{statusLabels[d.status]}</span>
          <span>{d.fileName}</span>
          <span>{formatSize(d.sizeBytes)}</span>
          {d.pageCount != null && <span>{d.pageCount} pages</span>}
          {d.status === "Ready" && <span>{d.chunkCount} passages</span>}
        </div>
        {d.error && <p className={`small ${d.status === "Failed" ? "error-text" : "warning-text"}`}>{d.error}</p>}
        {error && <p className="small error-text">{error.message}</p>}
      </div>

      <div className="actions">
        <select
          aria-label={`Certification for ${d.title}`}
          value={d.certificationId ?? ""}
          onChange={(e) =>
            update.mutate({ id: d.id, request: { title: d.title, certificationId: e.target.value || null } })
          }
        >
          <option value="">Any certification</option>
          {certifications.map((c) => (
            <option key={c.id} value={c.id}>
              {c.examCode}
            </option>
          ))}
        </select>
        <a className="button ghost small" href={`/api/documents/${d.id}/file`} target="_blank" rel="noreferrer">
          Open
        </a>
        <button className="button ghost small" onClick={rename}>
          Rename
        </button>
        {d.status === "Failed" && (
          <button className="button ghost small" onClick={() => retry.mutate(d.id)} disabled={retry.isPending}>
            Retry
          </button>
        )}
        <button
          className="button ghost small danger"
          onClick={() => confirm(`Delete "${d.title}"?`) && remove.mutate(d.id)}
          disabled={remove.isPending}
        >
          Delete
        </button>
      </div>
    </li>
  );
}
