import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { Link } from "react-router";
import { askStream, type AnswerKind, type AskSource, type AskTurn } from "../api/client";
import { useAiStatus, useCertifications, useDocuments } from "../api/hooks";
import { Alert, EmptyState } from "../components/ui";
import { parseAnswer, type Inline } from "../lib/answer";
import { pageLabel } from "../lib/citations";

interface Exchange {
  id: number;
  question: string;
  answer: string;
  sources: AskSource[];
  done?: { kind: AnswerKind; cited: number[]; notice: string | null };
  error?: string;
}

const examples = ["What should I focus on for this exam?", "Explain the difference between", "Summarize what my notes say about"];

export function AskPage() {
  const ai = useAiStatus();
  const documents = useDocuments();
  const certifications = useCertifications();
  const [certificationId, setCertificationId] = useState("");
  const [question, setQuestion] = useState("");
  const [exchanges, setExchanges] = useState<Exchange[]>([]);
  const [busy, setBusy] = useState(false);
  const abort = useRef<AbortController | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => bottom.current?.scrollIntoView({ block: "end", behavior: "smooth" }), [exchanges.length]);

  const update = (id: number, change: (e: Exchange) => Exchange) =>
    setExchanges((list) => list.map((e) => (e.id === id ? change(e) : e)));

  async function ask(e?: FormEvent) {
    e?.preventDefault();
    const text = question.trim();
    if (!text || busy) return;

    // Earlier finished exchanges become the conversation history, so follow-up questions work.
    const history: AskTurn[] = exchanges
      .filter((x) => x.done && !x.error)
      .flatMap((x) => [
        { role: "User" as const, text: x.question },
        { role: "Assistant" as const, text: x.answer },
      ]);

    const id = Date.now();
    setExchanges((list) => [...list, { id, question: text, answer: "", sources: [] }]);
    setQuestion("");
    setBusy(true);
    abort.current = new AbortController();

    try {
      await askStream(
        { question: text, certificationId: certificationId || null, history },
        (event) => {
          if (event.type === "sources") update(id, (x) => ({ ...x, sources: event.sources }));
          else if (event.type === "delta") update(id, (x) => ({ ...x, answer: x.answer + event.text }));
          else update(id, (x) => ({ ...x, done: { kind: event.kind, cited: event.cited, notice: event.notice } }));
        },
        abort.current.signal,
      );
    } catch (error) {
      if ((error as Error).name !== "AbortError") update(id, (x) => ({ ...x, error: (error as Error).message }));
    } finally {
      setBusy(false);
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      void ask();
    }
  }

  const hasReadyDocuments = documents.data?.some((d) => d.status === "Ready");

  return (
    <div className="stack ask-page">
      <div className="page-head">
        <div>
          <h1>Ask your materials</h1>
          <p className="muted">Answers come only from your documents, with a citation for every claim.</p>
        </div>
        {exchanges.length > 0 && (
          <button className="button ghost small" onClick={() => setExchanges([])} disabled={busy}>
            New conversation
          </button>
        )}
      </div>

      {ai.data && !ai.data.chatAvailable && (
        <Alert>
          <strong>Offline mode.</strong> Without a chat provider, answers are the most relevant sentences quoted from your
          documents. Set <code>AI_CHAT_PROVIDER</code> to get written answers.
        </Alert>
      )}

      {documents.data && !hasReadyDocuments && (
        <EmptyState title="Nothing to ask about yet">
          <p className="muted">
            <Link to="/library">Upload a PDF</Link> first. Questions are answered from your own documents only.
          </p>
        </EmptyState>
      )}

      {exchanges.length === 0 && hasReadyDocuments && (
        <div className="examples">
          {examples.map((example) => (
            <button key={example} className="button ghost small" onClick={() => setQuestion(example + " ")}>
              {example}…
            </button>
          ))}
        </div>
      )}

      <div className="conversation">
        {exchanges.map((x) => (
          <ExchangeView key={x.id} exchange={x} />
        ))}
        <div ref={bottom} />
      </div>

      <form className="card ask-form" onSubmit={(e) => void ask(e)}>
        <textarea
          aria-label="Your question"
          placeholder="Ask about your study materials…"
          rows={2}
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={onKeyDown}
          maxLength={2000}
        />
        <div className="row-between">
          <select aria-label="Certification" value={certificationId} onChange={(e) => setCertificationId(e.target.value)}>
            <option value="">All documents</option>
            {certifications.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.examCode}
              </option>
            ))}
          </select>
          {busy ? (
            <button type="button" className="button ghost" onClick={() => abort.current?.abort()}>
              Stop
            </button>
          ) : (
            <button type="submit" className="button primary" disabled={!question.trim()}>
              Ask
            </button>
          )}
        </div>
      </form>
    </div>
  );
}

function ExchangeView({ exchange: x }: { exchange: Exchange }) {
  const [highlighted, setHighlighted] = useState<number | null>(null);
  const valid = new Set(x.sources.map((s) => s.number));
  const blocks = parseAnswer(x.answer, valid);
  const cited = new Set(x.done?.cited ?? []);
  const shownSources = x.done?.kind === "NotCovered" ? [] : x.sources;

  function cite(n: number) {
    setHighlighted(n);
    document.getElementById(`source-${x.id}-${n}`)?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }

  const renderInline = (inline: Inline, i: number) =>
    inline.kind === "cite" ? (
      <button key={i} className="cite" onClick={() => cite(inline.n)} aria-label={`Source ${inline.n}`}>
        {inline.n}
      </button>
    ) : inline.kind === "bold" ? (
      <strong key={i}>{inline.text}</strong>
    ) : (
      <span key={i}>{inline.text}</span>
    );

  return (
    <article className="exchange">
      <p className="question">{x.question}</p>

      <div className={`card answer ${x.done?.kind === "NotCovered" ? "not-covered" : ""}`} aria-live="polite">
        {blocks.length === 0 && !x.error && <p className="muted">Searching your documents…</p>}
        {blocks.map((block, i) =>
          block.kind === "paragraph" ? (
            <p key={i}>{block.inlines.map(renderInline)}</p>
          ) : (
            <ul key={i}>
              {block.items.map((item, j) => (
                <li key={j}>{item.map(renderInline)}</li>
              ))}
            </ul>
          ),
        )}
        {x.done?.notice && <p className="small muted">{x.done.notice}</p>}
        {x.error && <p className="error-text">{x.error}</p>}
      </div>

      {shownSources.length > 0 && (
        <details className="sources" open={x.done !== undefined}>
          <summary className="small muted">
            {shownSources.length} sources{x.done ? `, ${cited.size} cited` : ""}
          </summary>
          <ol className="source-list">
            {shownSources.map((s) => (
              <li
                key={s.number}
                id={`source-${x.id}-${s.number}`}
                className={`source ${highlighted === s.number ? "highlighted" : ""} ${x.done && !cited.has(s.number) ? "uncited" : ""}`}
              >
                <div className="row-between">
                  <span className="small">
                    <span className="cite static">{s.number}</span> <strong>{s.documentTitle}</strong>{" "}
                    <span className="muted">
                      {pageLabel(s.pageStart, s.pageEnd)}
                      {s.headingPath && ` · ${s.headingPath}`}
                    </span>
                  </span>
                  <a className="small" href={`/api/documents/${s.documentId}/file#page=${s.pageStart}`} target="_blank" rel="noreferrer">
                    Open
                  </a>
                </div>
                <p className="small source-text">{s.text.length > 400 ? s.text.slice(0, 400) + "…" : s.text}</p>
              </li>
            ))}
          </ol>
        </details>
      )}
    </article>
  );
}
