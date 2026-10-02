import { test, expect } from '@playwright/test';

test('should render the booking flow', async ({ page }) => {
  await page.goto('/fi/varaus');

  // Wait for the form to finish loading (services resolved) before asserting,
  // otherwise the heading check can race the loading state.
  const nameInput = page.getByLabel(/nimi|name/i).first();
  await expect(nameInput).toBeVisible();

  await expect(
    page.getByRole('heading', { level: 1, name: /varaus|booking/i })
  ).toBeVisible();
});
