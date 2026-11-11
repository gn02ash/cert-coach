import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png`, fullPage: true });

/**
 * The Phase 4 workflow end to end in offline mode: read the synthetic sample CV into a profile, save it, get
 * ranked certifications with their reasons, and start adding one from its official page.
 */
test("read the sample CV, save the profile and get recommendations", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();
  await expect(page.getByRole("heading", { name: "Today", exact: true })).toBeVisible();

  await page.getByRole("link", { name: "Career" }).click();
  await expect(page.getByRole("heading", { name: "Career" })).toBeVisible();

  // On a re-run the profile exists and the CV reader starts closed.
  const useSample = page.getByRole("button", { name: "Use the sample CV" });
  if (!(await useSample.isVisible())) await page.getByText("Read it from my CV").click();
  await expect(page.getByText(/read on this server without AI/)).toBeVisible();
  await useSample.click();

  await expect(page.getByLabel("Skills")).toHaveValue(/asp\.net core/);
  await expect(page.getByLabel("Years of experience")).toHaveValue("6");
  await expect(page.getByLabel("Certifications you hold")).toHaveValue("AZ-900");
  await expect(page.getByRole("checkbox", { name: "developer", exact: true })).toBeChecked();

  await page.getByLabel("Target role").fill("AI engineer");
  await page.getByRole("button", { name: /^Save and/ }).click();

  await expect(page.getByRole("heading", { name: "Recommended for you" })).toBeVisible();
  const cards = page.locator(".recommendation");
  await expect(cards).toHaveCount(8);
  await expect(cards.filter({ hasText: "AI-200" })).toContainText("follows AZ-900, which you hold");
  await expect(cards.first()).toContainText("You already know:");
  await expect(cards.first()).toContainText("the role you're aiming for");
  await expect(page.locator(".recommendation", { hasText: "AZ-900:" })).toHaveCount(0);
  await shot(page, "14-career");

  // Adding one opens the certification editor with its details and official page filled in.
  const first = cards.first();
  const title = (await first.getByRole("heading").textContent())!;
  await first.getByRole("link", { name: "Add to my certifications" }).click();
  await expect(page.getByRole("heading", { name: "Add certification" })).toBeVisible();
  await expect(page.getByLabel("Exam code")).toHaveValue(title.split(":")[0]!);
  await expect(page.getByLabel("Import from a URL")).toHaveValue(/^https:\/\//);
});
