import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png` });

// A first visit: nothing in storage, so the tour opens by itself.
// Reduced motion also checks that the tour's animation turns off, and keeps the screenshot steady.
test.use({ storageState: { cookies: [], origins: [] }, reducedMotion: "reduce" });

test("the welcome tour opens on a first visit, walks through the app, and can be replayed", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();

  const tour = page.getByRole("dialog");
  await expect(tour).toBeVisible();
  await expect(tour.getByRole("heading", { name: "Welcome to Cert Coach" })).toBeVisible();
  await tour.getByRole("button", { name: "Show me around" }).click();

  // Each stop points at its part of the navigation.
  await expect(tour.getByRole("heading", { name: "Add your exam" })).toBeVisible();
  await expect(page.locator(".tour-spotlight")).toBeVisible();

  // The card sits just below the link it points at.
  const link = (await page.locator('[data-tour="certifications"]').boundingBox())!;
  const card = (await tour.boundingBox())!;
  expect(card.y).toBeGreaterThan(link.y + link.height);
  expect(card.y).toBeLessThan(link.y + link.height + 40);
  await shot(page, "17-tour");

  await page.keyboard.press("ArrowRight");
  await expect(tour.getByRole("heading", { name: "Study a little every day" })).toBeVisible();
  await tour.getByRole("button", { name: "Back" }).click();
  await expect(tour.getByRole("heading", { name: "Add your exam" })).toBeVisible();

  for (let i = 0; i < 6; i++) await tour.getByRole("button", { name: "Next" }).click();
  await expect(tour.getByRole("heading", { name: "You're ready" })).toBeVisible();
  await tour.getByRole("button", { name: "Add a certification" }).click();
  await expect(page.getByRole("heading", { name: "Add certification" })).toBeVisible();
  await expect(tour).toHaveCount(0);

  // Once finished it stays closed, and Tour in the header brings it back.
  await page.reload();
  await expect(page.getByRole("heading", { name: "Add certification" })).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByRole("button", { name: "Tour" }).click();
  await expect(page.getByRole("dialog").getByRole("heading", { name: "Welcome to Cert Coach" })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
