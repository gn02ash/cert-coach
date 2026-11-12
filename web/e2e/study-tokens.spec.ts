import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png`, fullPage: true });

/** One MCP call through nginx, as an assistant would make it. */
const callTool = (request: APIRequestContext, token: string | null, name: string) =>
  request.post("/mcp", {
    headers: {
      Accept: "application/json, text/event-stream",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    data: { jsonrpc: "2.0", id: 1, method: "tools/call", params: { name, arguments: {} } },
  });

/**
 * Connecting an assistant: create a token in the app, use it against the MCP endpoint the way a client does,
 * then revoke it and see it stop working.
 */
test("create a token, call the MCP server with it, and revoke it", async ({ page, playwright }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();
  await expect(page.getByRole("heading", { name: "Today", exact: true })).toBeVisible();

  await page.getByRole("link", { name: "Connect" }).click();
  await expect(page.getByRole("heading", { name: "Connect an AI assistant" })).toBeVisible();

  const name = `E2E assistant ${Date.now()}`;
  await page.getByLabel("Name").fill(name);
  await page.getByRole("button", { name: "Create token" }).click();
  const token = (await page.getByTestId("new-token").textContent())!;
  expect(token).toMatch(/^cc_pat_[\w-]{40,}$/);
  await expect(page.getByText(`claude mcp add --transport http cert-coach`)).toBeVisible();

  // A client with no browser session: only the token signs it in.
  const client = await playwright.request.newContext({ baseURL: new URL(page.url()).origin });
  expect((await callTool(client, null, "get_today_session")).status()).toBe(401);
  const today = await callTool(client, token, "get_today_session");
  expect(today.status()).toBe(200);
  expect(await today.text()).toContain("practiceItemsDue");
  expect(await (await callTool(client, token, "list_certifications")).text()).toContain("DEMO-101");

  // The token is hidden before the screenshot, so no token ends up in the README.
  await page.getByRole("button", { name: "I've saved it" }).click();
  await expect(page.getByTestId("new-token")).toHaveCount(0);
  // The list was loaded before the token was used, so load it again.
  await page.reload();
  const row = page.locator(".resource-row", { hasText: name });
  await expect(row).toContainText("last used");
  await page.evaluate(() => window.scrollTo(0, 0));
  await shot(page, "16-connect");

  page.once("dialog", (dialog) => dialog.accept());
  await row.getByRole("button", { name: "Revoke" }).click();
  await expect(row).toHaveCount(0);
  expect((await callTool(client, token, "get_today_session")).status()).toBe(401);
  await client.dispose();
});
