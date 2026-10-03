import { test, expect } from '@playwright/test';
import { blockKeycloak, json } from './support';

const NEW_AGREEMENT = /new agreement/i;

test.describe('admin agreements', () => {
  test('unauthenticated visitors never see admin actions', async ({ page }) => {
    // No live IdP in e2e: abort Keycloak so initAuth() settles as unauthenticated.
    await blockKeycloak(page);
    await page.route('**/api/**', (route) => json(route, 401, { title: 'Unauthorized' }));

    await page.goto('/fi/admin/agreements/');
    await expect(page.locator('body')).toBeVisible();

    // The admin-only action is gated behind isAdmin() and must never render here.
    await expect(page.getByRole('button', { name: NEW_AGREEMENT })).toHaveCount(0);
  });

  test('route renders without crashing when the backend returns data', async ({ page }) => {
    await blockKeycloak(page);
    await page.route('**/api/**', (route) => json(route, 200, []));

    await page.goto('/fi/admin/agreements/');

    await expect(page.locator('body')).toBeVisible();
    await expect(page.locator('body')).not.toContainText('Application error');
    // Still no admin action while unauthenticated.
    await expect(page.getByRole('button', { name: NEW_AGREEMENT })).toHaveCount(0);
  });
});
