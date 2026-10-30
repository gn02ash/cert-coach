import { useEffect, type ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { ApiError } from "../api/client";
import { useMe, useSetTimeZone } from "../api/hooks";

/** Renders its children only for a signed-in user, and keeps the user's time zone in sync. */
export function AuthGate({ children }: { children: ReactNode }) {
  const me = useMe();
  const location = useLocation();
  const setTimeZone = useSetTimeZone();
  const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  useEffect(() => {
    // "Today" is decided on the server from the stored time zone, so keep it in step with the browser.
    if (me.data && browserTimeZone && me.data.timeZone !== browserTimeZone && setTimeZone.isIdle) {
      setTimeZone.mutate(browserTimeZone);
    }
  }, [me.data, browserTimeZone, setTimeZone]);

  if (me.isPending) return <div className="page-loading">Loading…</div>;

  if (me.error) {
    if (me.error instanceof ApiError && me.error.status === 401) {
      return <Navigate to={`/login?returnUrl=${encodeURIComponent(location.pathname)}`} replace />;
    }
    return <div className="page-loading">Could not reach the server: {me.error.message}</div>;
  }

  return <>{children}</>;
}
