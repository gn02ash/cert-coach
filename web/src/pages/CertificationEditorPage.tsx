import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import type { CertificationDetail, CertificationStatus, DomainDraft, OutlineImport, SaveCertificationRequest } from "../api/client";
import { useAiStatus, useCertification, useImportOutline, useParseOutline, useSaveCertification } from "../api/hooks";
import { OutlineEditor } from "../components/OutlineEditor";
import { Alert, Warnings } from "../components/ui";
import sampleOutline from "../../../samples/DEMO-101-outline.md?raw";

const importMethods: Record<OutlineImport["method"], string> = {
  Parser: "Read by the parser",
  Ai: "Read by the AI parser",
  Agent: "Found by the AI agent",
};

/** Keeps what the learner typed; fills only empty fields, and the vendor while it is still the default. */
function withImportedFacts(form: SaveCertificationRequest, imported: OutlineImport): SaveCertificationRequest {
  return {
    ...form,
    outline: imported.draft.domains,
    examCode: form.examCode || imported.examCode || "",
    name: form.name || imported.name || "",
    vendor: imported.vendor && (!form.vendor || form.vendor === emptyForm.vendor) ? imported.vendor : form.vendor,
    outlineUrl: form.outlineUrl || imported.sourceUrl,
    outlineVersionDate: form.outlineVersionDate || imported.outlineVersionDate,
  };
}

const statuses: CertificationStatus[] = ["Active", "Beta", "Retired", "Replaced"];

const emptyForm: SaveCertificationRequest = {
  examCode: "",
  name: "",
  vendor: "Microsoft",
  outlineUrl: null,
  outlineVersionDate: null,
  status: "Active",
  replacedByExamCode: null,
  outline: [],
};

function toForm(c: CertificationDetail): SaveCertificationRequest {
  return {
    examCode: c.examCode,
    name: c.name,
    vendor: c.vendor,
    outlineUrl: c.outlineUrl,
    outlineVersionDate: c.outlineVersionDate,
    status: c.status,
    replacedByExamCode: c.replacedByExamCode,
    outline: c.domains.map<DomainDraft>((d) => ({
      name: d.name,
      weightMinPercent: d.weightMinPercent,
      weightMaxPercent: d.weightMaxPercent,
      skillAreas: d.skillAreas.map((s) => ({ name: s.name, objectives: s.objectives.map((o) => o.text) })),
    })),
  };
}

export function CertificationEditorPage() {
  const { id } = useParams();
  const existing = useCertification(id);
  const save = useSaveCertification(id);
  const parse = useParseOutline();
  const importUrl = useImportOutline();
  const ai = useAiStatus();
  const navigate = useNavigate();

  const [form, setForm] = useState<SaveCertificationRequest>(emptyForm);
  const [pasted, setPasted] = useState("");
  const [url, setUrl] = useState("");
  const [showPaste, setShowPaste] = useState(!id);

  useEffect(() => {
    if (existing.data) setForm(toForm(existing.data));
  }, [existing.data]);

  const set = <K extends keyof SaveCertificationRequest>(key: K, value: SaveCertificationRequest[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const runParse = (text: string, withAi = false) => {
    importUrl.reset();
    parse.mutate({ text, ai: withAi }, {
      onSuccess: (draft) => {
        set("outline", draft.domains);
        if (draft.domains.length > 0) setShowPaste(false);
      },
    });
  };

  const runImport = () => {
    if (!url.trim() || importUrl.isPending) return;
    parse.reset();
    importUrl.mutate(url.trim(), {
      onSuccess: (imported) => {
        setForm((f) => withImportedFacts(f, imported));
        if (imported.draft.domains.length > 0) setShowPaste(false);
      },
    });
  };

  const useSample = () => {
    setPasted(sampleOutline);
    setForm((f) => ({ ...f, examCode: f.examCode || "DEMO-101", name: f.name || "Contoso AI Solutions Engineer", vendor: "Contoso" }));
    runParse(sampleOutline);
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate(form, { onSuccess: (saved) => navigate(id ? "/certifications" : `/certifications/${saved.id}/plan`) });
  };

  if (id && existing.isPending) return <div className="page-loading">Loading…</div>;
  if (existing.error) return <p className="error-text">{existing.error.message}</p>;

  return (
    <form className="stack" onSubmit={submit}>
      <div className="page-head">
        <h1>{id ? `Edit ${form.examCode}` : "Add certification"}</h1>
        <Link to="/certifications" className="button ghost">
          Cancel
        </Link>
      </div>

      {existing.data?.activePlanId && (
        <Alert>This certification has an active plan, so its outline is locked. Archive the plan to change domains or objectives.</Alert>
      )}

      <div className="card form-grid">
        <label>
          Exam code
          <input required value={form.examCode} onChange={(e) => set("examCode", e.target.value)} placeholder="AI-103" />
        </label>
        <label className="span-2">
          Name
          <input required value={form.name} onChange={(e) => set("name", e.target.value)} />
        </label>
        <label>
          Vendor
          <input required value={form.vendor} onChange={(e) => set("vendor", e.target.value)} />
        </label>
        <label>
          Status
          <select value={form.status} onChange={(e) => set("status", e.target.value as CertificationStatus)}>
            {statuses.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </select>
        </label>
        {form.status === "Replaced" && (
          <label>
            Replaced by
            <input required value={form.replacedByExamCode ?? ""} onChange={(e) => set("replacedByExamCode", e.target.value)} />
          </label>
        )}
        <label className="span-2">
          Skills outline URL
          <input
            type="url"
            value={form.outlineUrl ?? ""}
            onChange={(e) => set("outlineUrl", e.target.value || null)}
            placeholder="https://learn.microsoft.com/credentials/…/study-guide"
          />
        </label>
        <label>
          Outline version date
          <input type="date" value={form.outlineVersionDate ?? ""} onChange={(e) => set("outlineVersionDate", e.target.value || null)} />
        </label>
      </div>

      <div className="card stack">
        <div className="row-between">
          <h2 className="card-title">Skills outline</h2>
          {!showPaste && (
            <button type="button" className="button ghost small" onClick={() => setShowPaste(true)}>
              Paste a new outline
            </button>
          )}
        </div>

        {showPaste && (
          <div className="stack">
            <div className="url-import">
              <label className="grow">
                Import from a URL
                <input
                  type="url"
                  value={url}
                  onChange={(e) => setUrl(e.target.value)}
                  onKeyDown={(e) => {
                    // Enter imports instead of submitting the certification form.
                    if (e.key === "Enter") {
                      e.preventDefault();
                      runImport();
                    }
                  }}
                  placeholder="https://learn.microsoft.com/credentials/certifications/…"
                />
              </label>
              <button type="button" className="button primary" disabled={!url.trim() || importUrl.isPending} onClick={runImport}>
                {importUrl.isPending ? "Reading the site…" : "Import from URL"}
              </button>
            </div>
            <p className="muted small">
              An exam page, its study guide or an exam guide PDF. Cert Coach reads at most 5 pages on that site
              {ai.data?.chatAvailable ? ", and an AI agent looks for the outline when the parser can't find it." : "."}
            </p>
            {importUrl.isPending && <p className="muted small">This can take a minute when the outline is a few links away.</p>}
            <div className="divider-text">or paste it</div>
            <p className="muted small">
              Paste the "Skills measured" section of the exam's study guide. Domain lines need their weight, like "(15–20%)".
            </p>
            <textarea rows={10} value={pasted} onChange={(e) => setPasted(e.target.value)} placeholder="Plan and manage an Azure AI solution (20–25%)…" />
            <div className="actions">
              <button type="button" className="button primary" disabled={!pasted.trim() || parse.isPending} onClick={() => runParse(pasted)}>
                Parse outline
              </button>
              {ai.data?.chatAvailable && (
                <button
                  type="button"
                  className="button ghost"
                  disabled={!pasted.trim() || parse.isPending}
                  onClick={() => runParse(pasted, true)}
                  title="For outlines in other layouts, such as a table copied from a web page or PDF"
                >
                  {parse.isPending && parse.variables?.ai ? "Parsing…" : "Parse with AI"}
                </button>
              )}
              <button type="button" className="button ghost" onClick={useSample}>
                Use the sample outline
              </button>
            </div>
          </div>
        )}

        {importUrl.data && (
          <p className="muted small" data-testid="import-summary">
            {importMethods[importUrl.data.method]} from{" "}
            <a href={importUrl.data.sourceUrl} target="_blank" rel="noreferrer">
              {importUrl.data.sourceUrl}
            </a>
            {importUrl.data.pagesRead.length > 1 && ` after reading ${importUrl.data.pagesRead.length} pages`}.
          </p>
        )}
        {importUrl.data && <Warnings warnings={importUrl.data.draft.warnings} />}
        {importUrl.error && <Alert tone="error">{importUrl.error.message}</Alert>}
        {parse.data && <Warnings warnings={parse.data.warnings} />}
        {parse.error && <Alert tone="error">{parse.error.message}</Alert>}

        {form.outline.length > 0 && <OutlineEditor value={form.outline} onChange={(outline) => set("outline", outline)} />}
      </div>

      {save.error && <Alert tone="error">{save.error.message}</Alert>}

      <div className="actions">
        <button className="button primary" disabled={save.isPending || form.outline.length === 0}>
          {id ? "Save changes" : "Save and plan"}
        </button>
      </div>
    </form>
  );
}
