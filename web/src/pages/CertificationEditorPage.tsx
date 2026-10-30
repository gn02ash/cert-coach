import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import type { CertificationDetail, CertificationStatus, DomainDraft, SaveCertificationRequest } from "../api/client";
import { useAiStatus, useCertification, useParseOutline, useSaveCertification } from "../api/hooks";
import { OutlineEditor } from "../components/OutlineEditor";
import { Alert, Warnings } from "../components/ui";
import sampleOutline from "../../../samples/DEMO-101-outline.md?raw";

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
  const ai = useAiStatus();
  const navigate = useNavigate();

  const [form, setForm] = useState<SaveCertificationRequest>(emptyForm);
  const [pasted, setPasted] = useState("");
  const [showPaste, setShowPaste] = useState(!id);

  useEffect(() => {
    if (existing.data) setForm(toForm(existing.data));
  }, [existing.data]);

  const set = <K extends keyof SaveCertificationRequest>(key: K, value: SaveCertificationRequest[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const runParse = (text: string, withAi = false) =>
    parse.mutate({ text, ai: withAi }, {
      onSuccess: (draft) => {
        set("outline", draft.domains);
        if (draft.domains.length > 0) setShowPaste(false);
      },
    });

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
