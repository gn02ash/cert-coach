import { NavLink, Outlet, useNavigate } from "react-router";
import { useLogout, useMe } from "../api/hooks";

export function Layout() {
  const me = useMe();
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
            <NavLink to="/" end>
              Today
            </NavLink>
            <NavLink to="/certifications">Certifications</NavLink>
            <NavLink to="/library">Library</NavLink>
            <NavLink to="/search">Search</NavLink>
            <NavLink to="/ask">Ask</NavLink>
          </nav>
          <div className="user">
            <span className="user-name">{me.data?.displayName}</span>
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
