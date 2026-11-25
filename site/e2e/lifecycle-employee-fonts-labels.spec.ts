import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Employee portal typography and shift card labels. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

async function setup(page: Page) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({
    headers,
    json: { occurrences: [{ shiftId: 'riverside', shiftName: 'Riverside clean', startUtc: '2026-11-24T08:00:00Z', endUtc: '2026-11-24T12:00:00Z', timeZoneId: 'Europe/Helsinki', form: { startedAtUtc: '2026-11-24T08:00:00Z', endedAtUtc: null, isSubmitted: false } }], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-24', toLocalDate: '2026-11-24' },
  }));
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
}

test('employee headings use Outfit and body text uses Work Sans', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  const heading = await page.getByRole('heading', { name: 'My Shifts', exact: true }).evaluate(el => getComputedStyle(el).fontFamily);
  const body = await page.locator('.employee-portal').first().evaluate(el => getComputedStyle(el).fontFamily);
  expect(heading).toContain('Outfit');
  expect(body).toContain('Work Sans');
});

test('the open-form button on a shift card is labelled Add shift photos', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'Add shift photos', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Record activities & end', exact: true })).toHaveCount(0);
});
