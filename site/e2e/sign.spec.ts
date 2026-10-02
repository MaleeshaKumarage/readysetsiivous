import { test, expect } from '@playwright/test';

test('should sign an agreement', async ({ page }) => {
  await page.goto('/fi/sign');
  await page.fill('input[name="token"]', 'test-token');
  await page.click('button[type="submit"]');
  await expect(page.locator('h1')).toHaveText('Agreement signed');
});
