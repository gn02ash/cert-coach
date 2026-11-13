import { useState, type FormEvent } from "react";
import type { CreatedApiToken } from "../api/client";
import { useApiTokens, useCreateApiToken, useRevokeApiToken } from "../api/hooks";
import { Alert, EmptyState } from "../components/ui";
import { formatLongDate } from "../lib/dates";

const tools = [
  ["get_today_session", "today's sessions, what's next, and how many items are due"],
  ["quiz_me and answer_question", "quiz you in chat and record your answers, so items are rescheduled as usual"],
  ["get_readiness", "your readiness per exam domain"],
  ["search_materials", "passages from your own documents, with page numbers"],
  ["complete_session", "mark a study session done when you say you finished it"],
  ["list_certifications", "the certifications you're studying"],
];

/** Personal access tokens for connecting an AI assistant to Cert Coach over MCP. */
export function ConnectPage() {
  const tokens = useApiTokens();
  const create = useCreateApiToken();
  const revoke = useRevokeApiToken();
  const [name, setName] = useState("");
  const [days, setDays] = useState(90);
  const [created, setCreated] = useState<CreatedApiToken | null>(null);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate(
      { name, lifetimeDays: days },
      {
        onSuccess: (token) => {
          setCreated(token);
          setName("");
        },
      },
    );
  };

  return (
    <div className="stack">
      <div className="page-head">
        <h1>Connect an AI assistant</h1>
      </div>
      <p className="muted">
        Cert Coach has an MCP server, so an assistant such as Claude can work with your study plan. Create a token here and give it to the
        assistant. A token lets it read and study with you. It cannot edit or delete anything, and you can revoke it at any time.
      </p>

      <section className="card stack">
        <h2 className="card-title">What an assistant can do</h2>
        <ul className="plain-list small tool-list">
          {tools.map(([tool, what]) => (
            <li key={tool}>
              <code>{tool}</code>: {what}
            </li>
          ))}
        </ul>
      </section>

      <form className="card stack" onSubmit={submit}>
        <h2 className="card-title">Create a token</h2>
        <div className="form-grid">
          <label className="span-2">
            Name
            <input required maxLength={60} value={name} onChange={(e) => setName(e.target.value)} placeholder="Claude Code on my laptop" />
          </label>
          <label>
            Expires after
            <select value={days} onChange={(e) => setDays(Number(e.target.value))}>
              <option value={30}>30 days</option>
              <option value={90}>90 days</option>
              <option value={365}>1 year</option>
            </select>
          </label>
        </div>
        {create.error && <Alert tone="error">{create.error.message}</Alert>}
        <div className="actions">
          <button className="button primary" disabled={create.isPending || !name.trim()}>
            Create token
          </button>
        </div>
      </form>

      {created && <NewToken created={created} onDone={() => setCreated(null)} />}

      <section className="card stack">
        <h2 className="card-title">Your tokens</h2>
        {tokens.error && <Alert tone="error">{tokens.error.message}</Alert>}
        {tokens.data?.length === 0 && <EmptyState title="No tokens yet" />}
        <ul className="plain-list stack">
          {tokens.data?.map((t) => (
            <li key={t.id} className="resource-row">
              <div className="resource-text">
                <span>
                  {t.name} <code className="muted">{t.prefix}…</code>
                  {!t.active && <span className="badge warning"> Expired</span>}
                </span>
                <span className="muted small">
                  Created {formatLongDate(t.createdAt.slice(0, 10))} · expires {formatLongDate(t.expiresAt.slice(0, 10))} ·{" "}
                  {t.lastUsedAt ? `last used ${formatLongDate(t.lastUsedAt.slice(0, 10))}` : "never used"}
                </span>
              </div>
              <button
                type="button"
                className="button ghost small danger"
                disabled={revoke.isPending}
                onClick={() => confirm(`Revoke "${t.name}"? Any assistant using it loses access at once.`) && revoke.mutate(t.id)}
              >
                Revoke
              </button>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

function NewToken({ created, onDone }: { created: CreatedApiToken; onDone: () => void }) {
  const [copied, setCopied] = useState(false);
  const url = `${window.location.origin}/mcp`;
  const command = `claude mcp add --transport http cert-coach ${url} --header "Authorization: Bearer ${created.token}"`;
  const config = JSON.stringify(
    { mcpServers: { "cert-coach": { type: "http", url, headers: { Authorization: `Bearer ${created.token}` } } } },
    null,
    2,
  );
  const copy = () => navigator.clipboard.writeText(created.token).then(() => setCopied(true));

  return (
    <section className="card stack new-token" aria-live="polite">
      <h2 className="card-title">Your new token</h2>
      <Alert tone="warning">Copy it now. Cert Coach keeps only a fingerprint of it, so it can't be shown again.</Alert>
      <div className="token-row">
        <code data-testid="new-token">{created.token}</code>
        <button type="button" className="button ghost small" onClick={copy}>
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
      <div className="stack">
        <h3 className="objective-title">Claude Code</h3>
        <pre className="snippet">{command}</pre>
        <h3 className="objective-title">Other MCP clients</h3>
        <p className="muted small">
          Server address <code>{url}</code>, HTTP transport, with the header <code>Authorization: Bearer …</code>. As JSON:
        </p>
        <pre className="snippet">{config}</pre>
      </div>
      <p className="muted small">Treat the token like a password: anyone who has it can read your study data until it expires or you revoke it.</p>
      <div className="actions">
        <button type="button" className="button ghost" onClick={onDone}>
          I've saved it
        </button>
      </div>
    </section>
  );
}
