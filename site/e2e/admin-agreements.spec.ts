import { test, expect } from '@playwright/test';

test('should render the admin dashboard', async ({ page }) => {
  await page.goto('/fi/admin');

  // The admin layout gates on auth; unauthenticated visitors should see the
  // sign-in prompt rather than an error page. Assert on the sign-in affordance
  // so this can't pass on a generic error page that happens to have an <h1>.
  await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
});
