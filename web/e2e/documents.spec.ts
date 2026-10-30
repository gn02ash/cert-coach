import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png`, fullPage: true });

/**
 * The Phase 2 workflow end to end in offline mode: upload the sample PDF, wait for the ingestion worker,
 * search it, and ask a question that is answered with a citation.
 */
test("upload a PDF, search it and ask about it with citations", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();
  await expect(page.getByRole("heading", { name: "Today" })).toBeVisible();

  await page.getByRole("link", { name: "Library" }).click();
  await expect(page.getByRole("heading", { name: "Library" })).toBeVisible();
  // Re-runs against the same stack find the document already there.
  if ((await page.getByText("Retrieval-Augmented Generation").count()) === 0) {
    await page.locator("#pdf-upload").setInputFiles("../samples/DEMO-101-rag-notes.pdf");
  }
  await expect(page.getByText("Ready", { exact: true })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Retrieval-Augmented Generation")).toBeVisible();
  await shot(page, "7-library");

  await page.getByRole("link", { name: "Search", exact: true }).click();
  await page.getByRole("searchbox", { name: "Search" }).fill("How does reciprocal rank fusion merge two rankings?");
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect(page.locator(".hit").first()).toContainText("reciprocal rank fusion", { ignoreCase: true });
  await expect(page.locator(".hit").first().locator("mark").first()).toBeVisible();
  await shot(page, "8-search");

  await page.getByRole("link", { name: "Ask", exact: true }).click();
  await page.getByRole("textbox", { name: "Your question" }).fill("How does reciprocal rank fusion merge rankings?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect(page.getByRole("button", { name: /^Source \d+$/ }).first()).toBeVisible();
  await expect(page.getByText(/Offline mode: these are quotes/)).toBeVisible();
  await page.getByRole("button", { name: /^Source \d+$/ }).first().click();
  await expect(page.locator(".source.highlighted")).toBeVisible();
  await shot(page, "9-ask");

  await page.getByRole("textbox", { name: "Your question" }).fill("Which graphics card is best for playing games at 4K?");
  await page.getByRole("button", { name: "Ask", exact: true }).click();
  await expect(page.getByText(/couldn't find this in your materials/)).toBeVisible();
});
