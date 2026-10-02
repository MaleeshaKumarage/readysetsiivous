import { test, expect } from '@playwright/test';

test('should render the booking flow', async ({ page }) => {
  await page.goto('/fi/varaus');
  await expect(page.locator('h1')).toBeVisible();
  await expect(page.getByText(/nimi|name/i).first()).toBeVisible();
});
