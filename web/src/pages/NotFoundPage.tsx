import { Link } from "react-router";
import { EmptyState } from "../components/ui";

export const NotFoundPage = () => (
  <EmptyState title="Page not found">
    <Link className="button primary" to="/">
      Back to today
    </Link>
  </EmptyState>
);
