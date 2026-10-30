/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The API owns sign-in (backend-for-frontend), so every server route is proxied same-origin.
const api = process.env.API_URL ?? "http://localhost:5265";
const proxied = ["/api", "/auth", "/signin-oidc", "/signin-google", "/health"];

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: Object.fromEntries(proxied.map((path) => [path, { target: api, xfwd: true }])),
    // The sample outline lives in the repository's samples/ folder.
    fs: { allow: [".."] },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
  },
});
