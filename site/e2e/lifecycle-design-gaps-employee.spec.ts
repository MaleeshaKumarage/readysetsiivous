import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { chooseEmployeeSection } from './employee-section';

// Employee design gaps: the Keys date filter, the Offers banner for automatic acceptance, and the two-column
// Schedules list for today. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function keyRequest(id: string, branchName: string, date: string) {
  return {
    id, revision: 1, companyId: '11111111-1111-4111-8111-111111111111', branchId: '22222222-2222-4222-8222-222222222222', branchName,
    kind: 'Pickup', date, notes: null, feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null,
    completedUtc: null, assignedEmployeeId: 'employee-a', assignedEmployeeName: 'Test Staff', photos: [],
  };
}

function occurrence(id: string, site: string, startUtc: string) {
  return { shiftId: id, shiftName: site, startUtc, endUtc: startUtc.replace('08:00', '12:00'), timeZoneId: 'Europe/Helsinki', form: null };
}

async function setup(page: Page, options: { autoAccept?: boolean; keys?: unknown[]; occurrences?: unknown[] } = {}) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({
    headers,
    json: { occurrences: options.occurrences ?? [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-12-01', toLocalDate: '2026-12-01' },
  }));
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: options.autoAccept ?? false } }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/key-requests$/, route => route.fulfill({ headers, json: options.keys ?? [] }));
}

test('the Keys date filter keeps only requests inside the chosen dates', async ({ page }) => {
  await setup(page, { keys: [keyRequest('key-a', 'Lake apartment', '2026-12-03'), keyRequest('key-b', 'Harbour flat', '2026-12-10')] });
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Keys');
  await expect(page.getByRole('listitem').filter({ hasText: 'Lake apartment' })).toHaveCount(1);
  await expect(page.getByRole('listitem').filter({ hasText: 'Harbour flat' })).toHaveCount(1);
  await page.getByLabel('From date', { exact: true }).fill('2026-12-05');
  await expect(page.getByRole('listitem').filter({ hasText: 'Lake apartment' })).toHaveCount(0);
  await expect(page.getByRole('listitem').filter({ hasText: 'Harbour flat' })).toHaveCount(1);
  await page.getByLabel('To date', { exact: true }).fill('2026-12-07');
  await expect(page.getByRole('listitem').filter({ hasText: 'Harbour flat' })).toHaveCount(0);
});

test('the Offers section says automatic acceptance is off when it is off', async ({ page }) => {
  await setup(page, { autoAccept: false });
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Offers');
  await expect(page.locator('#employee-panel-offers').getByText('Automatic acceptance is off. You review each offer yourself.', { exact: true })).toBeVisible();
});

test('the Offers section does not show the off message when automatic acceptance is on', async ({ page }) => {
  await setup(page, { autoAccept: true });
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Offers');
  await expect(page.locator('#employee-panel-offers').getByText('Automatic acceptance is on, so offers are assigned to you directly.')).toBeVisible();
  await expect(page.locator('#employee-panel-offers').getByText('Automatic acceptance is off.')).toHaveCount(0);
});

test('today\'s shifts sit side by side on desktop and stack on phones', async ({ page, isMobile }) => {
  await setup(page, { occurrences: [occurrence('riverside', 'Riverside clean', '2026-12-01T08:00:00Z'), occurrence('oakfield', 'Oakfield clean', '2026-12-01T13:00:00Z')] });
  await page.goto('/me/shifts/');
  const first = await page.getByRole('group', { name: /Riverside clean/ }).boundingBox();
  const second = await page.getByRole('group', { name: /Oakfield clean/ }).boundingBox();
  expect(first && second).toBeTruthy();
  if (isMobile) {
    expect(second!.y).toBeGreaterThan(first!.y);
  } else {
    expect(Math.abs(second!.y - first!.y)).toBeLessThan(4);
    expect(second!.x).toBeGreaterThan(first!.x);
  }
});
