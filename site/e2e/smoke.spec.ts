import { test, expect } from '@playwright/test';

test('should navigate to home page', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('h1')).toHaveText('Welcome to Cleaning Suite');
});
