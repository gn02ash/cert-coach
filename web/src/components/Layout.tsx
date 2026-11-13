import { NavLink, Outlet, useNavigate } from "react-router";
import { useAiAllowance, useDashboard, useLogout, useMe } from "../api/hooks";
import { TourProvider, useStartTour } from "./WelcomeTour";

export function Layout() {
  return (
    <TourProvider>
      <Shell />
    </TourProvider>
  );
}

function TourButton() {
  const start = useStartTour();
  return (
    <button className="button ghost small" onClick={start} title="A one-minute tour of where everything is">
      Tour
    </button>
  );
}

function Shell() {
  const me = useMe();
  const due = useDashboard().data?.reviewDue ?? 0;
  const logout = useLogout();
  const navigate = useNavigate();

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-inner">
          <NavLink to="/" className="brand">
            <span className="brand-mark" aria-hidden>
              ✓
            </span>
            Cert Coach
          </NavLink>
          <nav className="nav">
            <NavLink to="/" end data-tour="today">
              Today
            </NavLink>
            <NavLink to="/review" data-tour="review">
              Review{due > 0 && <span className="nav-count" aria-label={`${due} due`}>{due}</span>}
            </NavLink>
            <NavLink to="/certifications" data-tour="certifications">
              Certifications
            </NavLink>
            <NavLink to="/library" data-tour="library">
              Library
            </NavLink>
            <NavLink to="/search">Search</NavLink>
            <NavLink to="/ask">Ask</NavLink>
            <NavLink to="/career" data-tour="career">
              Career
            </NavLink>
            <NavLink to="/connect" data-tour="connect">
              Connect
            </NavLink>
          </nav>
          <div className="user">
            <AllowanceBadge />
            <span className="user-name">{me.data?.displayName}</span>
            <TourButton />
            <button
              className="button ghost small"
              onClick={() => logout.mutate(undefined, { onSuccess: () => navigate("/login") })}
            >
              Sign out
            </button>
          </div>
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}

/** How much of this month's AI allowance is used. Hidden when billing is off, as nothing is limited then. */
function AllowanceBadge() {
  const allowance = useAiAllowance().data;
  if (!allowance?.enforced) {
    return null;
  }

  const resets = new Date(allowance.resetsAt).toLocaleDateString(undefined, { day: "numeric", month: "long" });
  return (
    <span
      className={`badge${allowance.percentUsed >= 80 ? " warning" : ""}`}
      title={`${allowance.plan} plan: ${allowance.percentUsed}% of this month's AI allowance used. It resets on ${resets}.`}
    >
      {allowance.plan} · AI {allowance.percentUsed}%
    </span>
  );
}
