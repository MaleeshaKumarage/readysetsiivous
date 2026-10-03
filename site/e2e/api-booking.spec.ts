import { test, expect } from '@playwright/test';

// Verifies the booking flow's API integration against the deployed dev build.
// Requires an active dev tenant with seeded services.
test('booking flow loads services from the dev API', async ({ page }) => {
  await page.goto('/fi/varaus');
  // services fetch from the API -> service grid buttons render (not the "unavailable" fallback)
  await expect(page.locator('.grid button').first()).toBeVisible({ timeout: 15000 });
});
