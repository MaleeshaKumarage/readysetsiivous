import { test, expect } from '@playwright/test';

test('should render the admin dashboard', async ({ page }) => {
  await page.goto('/fi/admin');
  await expect(page.locator('h1')).toBeVisible();
});
