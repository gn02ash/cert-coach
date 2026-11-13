import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png` });

// A first visit: nothing in storage, so the tour opens by itself.
test.use({ storageState: { cookies: [], origins: [] } });

test("the welcome tour opens on a first visit, walks through the app, and can be replayed", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();

  const tour = page.getByRole("dialog");
  await expect(tour).toBeVisible();
  await expect(tour.getByRole("heading", { name: "Welcome to Cert Coach" })).toBeVisible();
  await tour.getByRole("button", { name: "Show me around" }).click();

  // Each stop points at its part of the navigation.
  await expect(tour.getByRole("heading", { name: "1. Add your exam" })).toBeVisible();
  await expect(page.locator(".tour-spotlight")).toBeVisible();
  await shot(page, "17-tour");

  await page.keyboard.press("ArrowRight");
  await expect(tour.getByRole("heading", { name: "2. Study a little every day" })).toBeVisible();
  await tour.getByRole("button", { name: "Back" }).click();
  await expect(tour.getByRole("heading", { name: "1. Add your exam" })).toBeVisible();

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
