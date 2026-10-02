import { test, expect } from '@playwright/test';

test('should render the signing page', async ({ page }) => {
  await page.goto('/fi/sign');
  await expect(
    page.getByRole('heading', { level: 1, name: /sign|allekirjoit/i })
  ).toBeVisible();
});
