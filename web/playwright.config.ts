import { defineConfig, devices } from "@playwright/test";

// End-to-end smoke test against the running Docker Compose stack (docker compose up --wait).
export default defineConfig({
  testDir: "e2e",
  timeout: 60_000,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:8080",
    viewport: { width: 1280, height: 860 },
    colorScheme: "light",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 860 } } }],
});
