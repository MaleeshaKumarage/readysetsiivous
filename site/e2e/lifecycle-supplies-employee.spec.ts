import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { chooseEmployeeSection } from './employee-section';

// Employee supply requests: choose items and quantities, send a request, see its status. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

const catalogue = [
  { id: 'cloths', name: 'Microfibre cloths', unit: 'pcs', isVerified: true, isActive: true, version: 'v1' },
  { id: 'bags', name: 'Bin liners', unit: 'roll', isVerified: true, isActive: true, version: 'v1' },
  { id: 'old', name: 'Retired polish', unit: 'pcs', isVerified: true, isActive: false, version: 'v1' },
];

type State = { posts: any[]; requests: any[] };

async function setup(page: Page): Promise<State> {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({ headers, json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-01', toLocalDate: '2026-11-22' } }));
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/inventory\/locations$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/items$/, route => route.fulfill({ headers, json: catalogue }));
  const state: State = { posts: [], requests: [] };
  await page.route(/\/api\/v1\/me\/supply-requests$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON();
      state.posts.push(body);
      const lines = body.lines.map((line: any) => ({ itemId: line.itemId, itemName: catalogue.find(c => c.id === line.itemId)!.name, unit: catalogue.find(c => c.id === line.itemId)!.unit, quantity: line.quantity }));
      const created = { id: 'req-1', employeeId: 'employee-a', employeeName: 'Test Staff', lines, note: body.note, status: 'Requested', decisionNote: null, decidedByName: null, decidedUtc: null, createdUtc: '2026-11-23T08:00:00Z', version: 'v1' };
      state.requests.push(created);
      return route.fulfill({ headers, json: created });
    }
    return route.fulfill({ headers, json: state.requests });
  });
  return state;
}

async function openRequestSupplies(page: Page) {
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Inventory');
  await page.getByRole('tab', { name: 'Request supplies', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Request supplies', exact: true })).toBeVisible();
}

test('only active catalogue items are offered for request', async ({ page }) => {
  await setup(page);
  await openRequestSupplies(page);
  await expect(page.getByLabel('Quantity for Microfibre cloths')).toBeVisible();
  await expect(page.getByLabel('Quantity for Bin liners')).toBeVisible();
  await expect(page.getByText('Retired polish')).toHaveCount(0);
});

test('Send request stays disabled until a quantity above zero is entered', async ({ page }) => {
  await setup(page);
  await openRequestSupplies(page);
  const send = page.getByRole('button', { name: 'Send request', exact: true });
  await expect(send).toBeDisabled();
  await page.getByLabel('Quantity for Microfibre cloths').fill('0');
  await expect(send).toBeDisabled();
  await page.getByLabel('Quantity for Microfibre cloths').fill('3');
  await expect(send).toBeEnabled();
});

test('a request sends the chosen items and note, and shows as Requested', async ({ page }) => {
  const state = await setup(page);
  await openRequestSupplies(page);
  await page.getByLabel('Quantity for Microfibre cloths').fill('3');
  await page.getByLabel('Note (optional)').fill('For Monday');
  await page.getByRole('button', { name: 'Send request', exact: true }).click();

  await expect.poll(() => state.posts.length).toBe(1);
  expect(state.posts[0]).toEqual({ lines: [{ itemId: 'cloths', quantity: 3 }], note: 'For Monday' });
  await expect(page.getByRole('status').filter({ hasText: 'Request sent.' })).toBeVisible();
  await expect(page.getByText('Requested', { exact: true })).toBeVisible();
});

test('the item list and history stack on a phone and do not widen the page', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await openRequestSupplies(page);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});
