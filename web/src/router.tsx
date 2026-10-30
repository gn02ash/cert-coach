import { createBrowserRouter } from "react-router";
import { AuthGate } from "./components/AuthGate";
import { Layout } from "./components/Layout";
import { CertificationEditorPage } from "./pages/CertificationEditorPage";
import { CertificationsPage } from "./pages/CertificationsPage";
import { DashboardPage } from "./pages/DashboardPage";
import { LibraryPage } from "./pages/LibraryPage";
import { LoginPage } from "./pages/LoginPage";
import { NewPlanPage } from "./pages/NewPlanPage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { PlanPage } from "./pages/PlanPage";
import { SearchPage } from "./pages/SearchPage";

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    element: (
      <AuthGate>
        <Layout />
      </AuthGate>
    ),
    children: [
      { index: true, element: <DashboardPage /> },
      { path: "certifications", element: <CertificationsPage /> },
      { path: "certifications/new", element: <CertificationEditorPage /> },
      { path: "certifications/:id", element: <CertificationEditorPage /> },
      { path: "certifications/:id/plan", element: <NewPlanPage /> },
      { path: "plans/:id", element: <PlanPage /> },
      { path: "library", element: <LibraryPage /> },
      { path: "search", element: <SearchPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);
