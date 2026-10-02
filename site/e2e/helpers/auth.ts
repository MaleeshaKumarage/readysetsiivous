import { expect, type Page } from '@playwright/test';

// Drives the real Keycloak login form so the app's in-memory token is populated.
// The app keeps tokens in memory only (see src/lib/auth.ts), so we cannot seed
// localStorage/cookies — we must go through the actual login redirect.
export async function loginAsAdmin(page: Page): Promise<void> {
  const user = process.env.E2E_ADMIN_USER;
  const password = process.env.E2E_ADMIN_PASSWORD;
  if (!user || !password) {
    throw new Error('E2E_ADMIN_USER and E2E_ADMIN_PASSWORD must be set.');
  }

  await page.goto('/fi/admin');

  // The admin layout renders a "Sign in" button that triggers keycloak.login().
  await page.getByRole('button', { name: /sign in/i }).click();

  // Keycloak's default login form. Theme variants may not use the default
  // `#kc-login` id, so fall back to a submit button by role/type.
  await page.locator('#username').fill(user);
  await page.locator('#password').fill(password);

  const submit = page.locator('#kc-login');
  if ((await submit.count()) > 0) {
    await submit.click();
  } else {
    await page
      .getByRole('button', { name: /sign in|log in|login/i })
      .first()
      .click();
  }

  // Back on the admin route with a populated token.
  await expect(page).toHaveURL(/\/fi\/admin(\/|$|\?)/);
}
