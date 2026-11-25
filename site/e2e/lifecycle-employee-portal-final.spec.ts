import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Final employee Schedules layout: a day's shifts in Past and Future sit side by side on large screens.
// Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function occurrence(id: string, site: string, startUtc: string) {
  return { shiftId: id, shiftName: site, startUtc, endUtc: startUtc.replace('08:00', '12:00'), timeZoneId: 'Europe/Helsinki', form: null };
}

async function setup(page: Page) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({
    headers,
    json: {
      occurrences: [occurrence('riverside', 'Riverside clean', '2026-12-01T08:00:00Z'), occurrence('oakfield', 'Oakfield clean', '2026-12-01T13:00:00Z')],
      timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-12-01', toLocalDate: '2026-12-07',
    },
  }));
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
}

test('future shifts for one day sit side by side on desktop and stack on phones', async ({ page, isMobile }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Future', exact: true }).click();
  await page.getByRole('button', { name: /^Week of / }).click();
  const day = page.getByRole('region', { name: 'Shifts on 2026-12-01' });
  await expect(day).toBeVisible();
  const first = await day.getByRole('group', { name: /Riverside clean/ }).boundingBox();
  const second = await day.getByRole('group', { name: /Oakfield clean/ }).boundingBox();
  expect(first && second).toBeTruthy();
  if (isMobile) {
    expect(second!.y).toBeGreaterThan(first!.y);
  } else {
    expect(Math.abs(second!.y - first!.y)).toBeLessThan(4);
    expect(second!.x).toBeGreaterThan(first!.x);
  }
});
