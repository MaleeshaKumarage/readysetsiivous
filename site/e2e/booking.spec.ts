import { test, expect } from '@playwright/test';

test('should book a service', async ({ page }) => {
  await page.goto('/fi/book');
  await page.fill('input[name="service"]', 'Service A');
  await page.fill('input[name="size"]', 'Large');
  await page.fill('input[name="city"]', 'Helsinki');
  await page.fill('input[name="date"]', '2023-10-01');
  await page.click('button[type="submit"]');
  await expect(page.locator('h1')).toHaveText('Booking confirmed');
});
