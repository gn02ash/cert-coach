import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png`, fullPage: true });

/**
 * Resources per objective, without a YouTube key: the search link is offered, a link of the learner's own is
 * saved against the objective and ticked off. Microsoft Learn suggestions depend on the network, so the test
 * accepts either some or none.
 */
test("find resources for an objective, save a link and tick it off", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();
  await expect(page.getByRole("heading", { name: "Today", exact: true })).toBeVisible();

  await page.goto("/certifications");
  await page.getByRole("link", { name: "Resources", exact: true }).first().click();
  await expect(page.getByRole("heading", { name: "DEMO-101 resources" })).toBeVisible();

  await page.getByLabel("Objective").selectOption({ label: "Combine vector and keyword search" });
  await expect(page.getByRole("heading", { name: "Microsoft Learn" })).toBeVisible({ timeout: 30_000 });
  const search = page.getByRole("link", { name: "Search YouTube for this objective" });
  await expect(search).toHaveAttribute("href", /youtube\.com\/results\?search_query=DEMO-101/);
  await expect(page.getByText(/Set YOUTUBE_API_KEY/)).toBeVisible();

  // Re-runs against the same stack find the link already saved.
  const saved = page.locator(".resource-row", { hasText: "Hybrid search explained" });
  if ((await saved.count()) === 0) {
    await page.getByLabel("Title").fill("Hybrid search explained");
    await page.getByLabel("Address").fill("https://example.com/hybrid-search");
    await page.getByRole("button", { name: "Add link" }).click();
  }

  await expect(saved).toBeVisible();
  // The tick shows once the server has saved it, so click and wait rather than check().
  const done = saved.getByRole("checkbox");
  if (!(await done.isChecked())) await done.click();
  await expect(done).toBeChecked();
  await expect(page.getByText(/^\d+ saved · [1-9]\d* done$/)).toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  await shot(page, "15-resources");
});
