import { useSearchParams } from "react-router";
import { useProviders } from "../api/hooks";

const icons: Record<string, string> = { microsoft: "▦", google: "G", dev: "⌂" };

export function LoginPage() {
  const providers = useProviders();
  const [params] = useSearchParams();
  const returnUrl = params.get("returnUrl") ?? "/";

  return (
    <div className="login">
      <div className="login-card">
        <div className="brand large">
          <span className="brand-mark" aria-hidden>
            ✓
          </span>
          Cert Coach
        </div>
        <p className="muted">Turn an exam outline into short daily study sessions that adapt when life happens.</p>

        {providers.isPending && <p className="muted">Loading sign-in options…</p>}
        {providers.error && <p className="error-text">Could not reach the server: {providers.error.message}</p>}

        <div className="login-buttons">
          {providers.data?.map((p) => (
            <a
              key={p.id}
              className={`button ${p.id === "dev" ? "ghost" : "primary"} block`}
              href={`/auth/login/${p.id}?returnUrl=${encodeURIComponent(returnUrl)}`}
            >
              <span aria-hidden className="provider-icon">
                {icons[p.id] ?? "→"}
              </span>
              {p.id === "dev" ? p.name : `Sign in with ${p.name}`}
            </a>
          ))}
        </div>

        {providers.data?.length === 0 && (
          <p className="muted small">
            No sign-in method is configured. Set Microsoft or Google credentials, or enable the local dev login, in the
            API configuration.
          </p>
        )}
      </div>
    </div>
  );
}
