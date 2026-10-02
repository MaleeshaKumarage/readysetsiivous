import { test, expect } from '@playwright/test';

test('should render the signing page', async ({ page }) => {
  await page.goto('/fi/sign');
  await expect(page.locator('h1')).toBeVisible();
});
