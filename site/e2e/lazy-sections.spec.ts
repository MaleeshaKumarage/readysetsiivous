import { test, expect } from '@playwright/test';

// Below-the-fold home sections load on demand through Suspense. Each one must still render in every language
// once it arrives, and the placeholder must be replaced rather than left in place.
for (const lang of ['fi', 'en', 'sv'] as const) {
  test(`${lang}: lazy home sections load and render`, async ({ page }) => {
    await page.goto(`/${lang}/`);
    // Wait for the real section content, not the placeholder that holds its place while loading.
    await expect(page.locator('#about').getByRole('heading').first()).toBeVisible();
    await expect(page.locator('#faq').getByRole('button').first()).toBeVisible();
    await expect(page.locator('#quote').locator('#service')).toBeVisible();
    await expect(page.locator('div[aria-hidden="true"]#about')).toHaveCount(0);
  });
}
