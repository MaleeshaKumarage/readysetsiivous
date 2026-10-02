import { test, expect } from '@playwright/test';

test('should invite an employee', async ({ page }) => {
  await page.goto('/fi/admin/invite');
  await page.fill('input[name="id"]', 'test-id');
  await page.click('button[type="submit"]');
  await expect(page.locator('h1')).toHaveText('Employee invited');
});
