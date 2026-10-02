import { test, expect } from '@playwright/test';

test('should navigate to home page', async ({ page }) => {
  await page.goto('/fi/');
  await expect(page).toHaveTitle(/ReadySetSiivous/i);
  await expect(page.locator('header')).toBeVisible();
});
