import { test, expect } from '@playwright/test';

test('should render the booking flow', async ({ page }) => {
  await page.goto('/fi/varaus');
  await expect(
    page.getByRole('heading', { level: 1, name: /varaus|booking/i })
  ).toBeVisible();
  await expect(page.getByLabel(/nimi|name/i).first()).toBeVisible();
});
