import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Layout checks for the restyled Schedules controls. Runs on chromium-desktop and mobile-touch.

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
    json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-01', toLocalDate: '2026-11-22' },
  }));
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
}

test('the Past, Today and Future tabs are at least 44px tall', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  for (const name of ['Past', 'Today', 'Future']) {
    const box = await page.getByRole('tab', { name, exact: true }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }
});

test('the date filter is a labelled group with From, To and Clear dates on the Past view', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  const group = page.getByRole('group', { name: 'Completed between dates' });
  await expect(group).toBeVisible();
  await expect(group.getByLabel('From date', { exact: true })).toBeVisible();
  await expect(group.getByLabel('To date', { exact: true })).toBeVisible();
  // Clear dates only appears once a date is chosen.
  await expect(group.getByRole('button', { name: 'Clear dates' })).toHaveCount(0);
  await group.getByLabel('From date', { exact: true }).fill('2026-11-01');
  await expect(group.getByRole('button', { name: 'Clear dates' })).toBeVisible();
});

test('the date inputs are at least 44px tall', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  for (const label of ['From date', 'To date']) {
    const box = await page.getByLabel(label, { exact: true }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }
});

test('the Schedules controls do not widen the page on a phone', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  await expect(page.getByRole('group', { name: 'Completed between dates' })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});
