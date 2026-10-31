import { expect, test, type Page } from "@playwright/test";

const shot = (page: Page, name: string) => page.screenshot({ path: `e2e-screenshots/${name}.png`, fullPage: true });

/**
 * The Phase 3 workflow end to end in offline mode, after the other specs (files run in name order) have added
 * the sample certification and uploaded the sample PDF: generate practice items from the PDF, review them, take a
 * short mock exam, and see readiness on the dashboard.
 */
test("generate practice items, review them and take a mock exam", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("link", { name: "Local dev login" }).click();
  await expect(page.getByRole("heading", { name: "Today", exact: true })).toBeVisible();

  await page.goto("/certifications");
  await page.getByRole("link", { name: "Practice items" }).first().click();
  await expect(page.getByRole("heading", { name: "DEMO-101 practice items" })).toBeVisible();
  await expect(page.getByText(/Offline mode: fill-in-the-blank/)).toBeVisible();

  await page.getByRole("button", { name: "Generate", exact: true }).click();
  await expect(page.getByText(/^Last run: \d+ new items? for 21 objectives/)).toBeVisible({ timeout: 60_000 });
  await expect(page.locator(".practice-item").first()).toBeVisible();
  await page.locator(".practice-item summary").first().click();
  await expect(page.getByRole("link", { name: /DEMO-101.*p\. \d+/ }).first()).toBeVisible();
  await shot(page, "10-practice-items");

  // Review a few items: reveal and grade flashcards, answer questions.
  await page.getByRole("link", { name: /^Review/ }).click();
  await expect(page.getByRole("heading", { name: "Review" })).toBeVisible();
  let shotTaken = false;
  for (let i = 1; i <= 4; i++) {
    await expect(page.getByText(new RegExp(`^${i} of \\d+$`))).toBeVisible();
    const showAnswer = page.getByRole("button", { name: "Show answer" });
    if (await showAnswer.isVisible()) {
      await showAnswer.click();
      await page.getByRole("button", { name: /^Good/ }).click();
    } else {
      await page.locator(".option").first().click();
      await page.getByRole("button", { name: "Check" }).click();
      await expect(page.getByText(/^(✓ Correct|✗ Not quite) · back/)).toBeVisible();
      if (!shotTaken) {
        await shot(page, "11-review");
        shotTaken = true;
      }

      await page.getByRole("button", { name: "Next" }).click();
    }
  }

  // A short mock exam: answer the first question, leave the rest, submit.
  await page.goto("/certifications");
  await page.getByRole("link", { name: "Practice items" }).first().click();
  await page.getByRole("link", { name: "Take a mock exam" }).click();
  await page.getByLabel("Questions").fill("6");
  await page.getByLabel("Minutes").fill("10");
  await page.getByRole("button", { name: "Start the exam" }).click();
  await expect(page.getByRole("timer")).toBeVisible();
  await expect(page.getByText("Question 1 of 6")).toBeVisible();
  await page.locator(".option").first().click();
  await expect(page.locator(".exam-nav-item.answered")).toHaveCount(1);

  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Submit exam" }).click();
  await expect(page.getByText(/^\d+% \(\d of 6 correct\)$/)).toBeVisible();
  await expect(page.getByText("Not answered.").first()).toBeVisible();
  await shot(page, "12-mock-exam");

  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Readiness" })).toBeVisible();
  await expect(page.locator(".domain-list").last()).toContainText(/answers?/);
});
