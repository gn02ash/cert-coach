import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router";
import { ApiError } from "./api/client";
import { router } from "./router";
import "./styles.css";

/** A 401 anywhere means the session ended; start over at the sign-in page. */
function onError(error: Error) {
  if (error instanceof ApiError && error.status === 401 && window.location.pathname !== "/login") {
    window.location.assign(`/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
  }
}

const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
