import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { chooseEmployeeSection } from './employee-section';

// Layout and behaviour checks for the restyled Keys cards. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function keyRequest(id: string, branchName: string, status: 'Requested' | 'Completed', revision: number) {
  return {
    id, revision, companyId: '11111111-1111-4111-8111-111111111111', branchId: '22222222-2222-4222-8222-222222222222', branchName,
    kind: 'Pickup', date: '2026-12-03', notes: null, feeNet: 15, vatRatePercent: 25.5, status, cancellationReason: null,
    completedUtc: status === 'Completed' ? '2026-12-03T09:00:00Z' : null, assignedEmployeeId: 'employee-a', assignedEmployeeName: 'Test Staff', photos: [],
  };
}

async function setup(page: Page, items = [keyRequest('key-7', 'Lake apartment', 'Requested', 4), keyRequest('key-8', 'Harbour flat', 'Completed', 2)]) {
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
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/key-requests$/, route => route.fulfill({ headers, json: items }));
}

async function openKeys(page: Page) {
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Keys');
  await expect(page.getByRole('heading', { name: 'Key requests', exact: true })).toBeVisible();
}

test('each key card has the gold left edge', async ({ page }) => {
  await setup(page);
  await openKeys(page);
  const card = page.getByRole('listitem').filter({ hasText: 'Lake apartment' });
  const width = await card.evaluate(el => parseFloat(getComputedStyle(el).borderLeftWidth));
  expect(width).toBeGreaterThanOrEqual(4);
});

test('the status shows as a pill with the original wording', async ({ page }) => {
  await setup(page);
  await openKeys(page);
  await expect(page.getByRole('listitem').filter({ hasText: 'Lake apartment' }).getByText('To do', { exact: true })).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Harbour flat' }).getByText('Completed', { exact: true })).toBeVisible();
});

test('Mark completed is at least 44px tall', async ({ page }) => {
  await setup(page);
  await openKeys(page);
  const box = await page.getByRole('button', { name: 'Mark completed', exact: true }).boundingBox();
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
});

test('key cards sit side by side on desktop and stack on phones', async ({ page, isMobile }) => {
  await setup(page);
  await openKeys(page);
  const first = await page.getByRole('listitem').filter({ hasText: 'Lake apartment' }).boundingBox();
  const second = await page.getByRole('listitem').filter({ hasText: 'Harbour flat' }).boundingBox();
  expect(first && second).toBeTruthy();
  if (isMobile) {
    expect(second!.y).toBeGreaterThan(first!.y);
  } else {
    expect(Math.abs(second!.y - first!.y)).toBeLessThan(4);
    expect(second!.x).toBeGreaterThan(first!.x);
  }
});

test('the phone layout does not widen the page', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await openKeys(page);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('Mark completed still sends the request revision', async ({ page }) => {
  await setup(page);
  const bodies: unknown[] = [];
  await page.route(/\/api\/v1\/me\/key-requests\/key-7\/complete$/, route => {
    bodies.push(route.request().postDataJSON());
    return route.fulfill({ headers, json: {} });
  });
  await openKeys(page);
  await page.getByRole('listitem').filter({ hasText: 'Lake apartment' }).getByRole('button', { name: 'Mark completed', exact: true }).click();
  await expect.poll(() => bodies.length).toBe(1);
  expect(bodies[0]).toEqual({ revision: 4 });
});
