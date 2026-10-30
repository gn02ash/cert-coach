import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png`, fullPage: true });

/**
 * The Phase 1 workflow end to end, through nginx, the API and PostgreSQL:
 * sign in, add a certification from the sample outline, plan it, and study today's session.
 */
test("sign in, add the sample certification, plan it and complete today's session", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login/);
  await shot(page, "1-login");
  await page.getByRole("link", { name: "Local dev login" }).click();

  await expect(page.getByRole("heading", { name: "Today" })).toBeVisible();
  await page.getByRole("link", { name: "Certifications" }).first().click();
  await page.getByRole("link", { name: "Add certification" }).first().click();

  await page.getByRole("button", { name: "Use the sample outline" }).click();
  await expect(page.getByText(/4 domains · 21 objectives/)).toBeVisible();
  await shot(page, "2-certification-editor");
  await page.getByRole("button", { name: "Save and plan" }).click();

  // Study every day so there is a session today whatever day the test runs.
  await expect(page.getByRole("heading", { name: "Plan for DEMO-101" })).toBeVisible();
  for (const day of ["Sat", "Sun"]) {
    const toggle = page.getByRole("button", { name: day, exact: true });
    if ((await toggle.getAttribute("aria-pressed")) !== "true") await toggle.click();
  }
  await expect(page.getByText(/sessions a week of/)).toBeVisible();
  await shot(page, "3-plan-preview");
  await page.getByRole("button", { name: "Start this plan" }).click();

  await expect(page.getByRole("heading", { name: /DEMO-101/ })).toBeVisible();
  await expect(page.getByRole("button", { name: "Done" })).toBeVisible();
  await shot(page, "4-dashboard");

  await page.getByRole("button", { name: "Done" }).click();
  await expect(page.getByText("✓ Done")).toBeVisible();
  await expect(page.getByText(/^1 of \d+ sessions done/)).toBeVisible();

  await page.getByRole("link", { name: "Full plan" }).click();
  await expect(page.getByText(/^Week of/).first()).toBeVisible();
  await shot(page, "5-plan");

  await page.goto("/certifications");
  await expect(page.getByRole("link", { name: "View plan" })).toBeVisible();
  await shot(page, "6-certifications");
});

test("the API rejects unauthenticated calls", async ({ request }) => {
  const response = await request.get("/api/dashboard");
  expect(response.status()).toBe(401);
});
