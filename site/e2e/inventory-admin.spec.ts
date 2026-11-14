import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');

const employees = [
  { id: 'employee-a', firstName: 'Ana', lastName: 'Keeper', email: 'ana@example.test', role: 'employee', isActive: true, identitySyncPending: false, skills: [], serviceAreas: [], defaultHours: {} },
  { id: 'employee-b', firstName: 'Ben', lastName: 'Cleaner', email: 'ben@example.test', role: 'employee', isActive: true, identitySyncPending: false, skills: [], serviceAreas: [], defaultHours: {} },
];

const usageRows = [
  { id: 'usage-1', employeeId: 'employee-a', employeeName: 'Ana Keeper', locationId: 'loc-1', locationName: 'Main store', itemId: 'item-1', itemName: 'Keys', unit: 'pcs', quantity: 2, note: 'Spare set', takenUtc: '2026-11-03T08:30:00Z', photoIds: ['photo-1', 'photo-2'], photosPurged: false, version: 'v1' },
  { id: 'usage-2', employeeId: 'employee-b', employeeName: 'Ben Cleaner', locationId: 'loc-1', locationName: 'Main store', itemId: 'item-2', itemName: 'Bedsheets', unit: 'pcs', quantity: 4, note: '', takenUtc: '2026-05-02T08:30:00Z', photoIds: [], photosPurged: true, version: 'v1' },
];

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees(\?.*)?$/, route => route.fulfill({ headers, json: employees }));
  const state = { usageQueries: [] as string[], posts: [] as { url: string; body: any }[], locations: [{ id: 'loc-1', name: 'Main store', isActive: true, version: 'v1' }], items: [{ id: 'item-3', name: 'Mop heads', unit: 'pcs', isVerified: false, isActive: true, version: 'v7' }] as any[] };

  await page.route(/\/api\/v1\/admin\/inventory\/usage\/export(\?.*)?$/, route => route.fulfill({
    headers: { ...headers, 'Content-Type': 'text/csv; charset=utf-8', 'Content-Disposition': 'attachment; filename="inventory-2026-11-06.csv"' },
    body: '﻿"Taken (Helsinki)","Employee"\r\n"2026-11-03 10:30","Ana Keeper"\r\n',
  }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage\/[^/]+\/photos\/[^/?]+(\?.*)?$/, route => route.fulfill({ headers, contentType: 'image/png', body: png }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    state.usageQueries.push(new URL(route.request().url()).search);
    return route.fulfill({ headers, json: usageRows });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/locations(\?.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON();
      state.posts.push({ url: route.request().url(), body });
      if (String(body.name).toLowerCase() === 'main store') {
        return route.fulfill({ status: 409, headers, contentType: 'application/problem+json', json: { title: 'Inventory conflict', status: 409, detail: 'A location named "Main store" already exists. Choose another name, or reactivate the existing location.' } });
      }
      return route.fulfill({ headers, json: { id: 'loc-2', name: body.name, isActive: true, version: 'v1' } });
    }
    return route.fulfill({ headers, json: state.locations });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/items\/[^/]+\/verify$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    state.posts.push({ url: route.request().url(), body: route.request().postDataJSON() });
    state.items = state.items.map(item => ({ ...item, isVerified: true }));
    return route.fulfill({ headers, json: state.items[0] });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/items(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ headers, json: state.items });
  });
  return state;
}

test('log shows every employee entry and sends the chosen date range to the server', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');

  await expect(page.getByRole('gridcell', { name: 'Keys', exact: true })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Ana Keeper', exact: true })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Ben Cleaner', exact: true })).toBeVisible();
  await expect(page.getByText('Deleted', { exact: true })).toBeVisible();

  await page.getByLabel('From', { exact: true }).fill('2026-11-01');
  await page.getByLabel('To', { exact: true }).fill('2026-11-06');
  await page.getByRole('button', { name: 'Apply' }).click();

  await expect.poll(() => state.usageQueries.at(-1)).toContain('from=2026-11-01');
  expect(state.usageQueries.at(-1)).toContain('to=2026-11-06');
});

test('the employee filter includes deactivated employees', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employeeRequests: string[] = [];
  await page.route(/\/api\/v1\/admin\/employees(\?.*)?$/, route => {
    employeeRequests.push(route.request().url());
    return route.fulfill({ headers, json: employees });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: usageRows }));
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('gridcell', { name: 'Keys', exact: true })).toBeVisible();
  expect(employeeRequests.some(url => url.includes('includeInactive=true'))).toBe(true);
});

test('a slow response for an older filter does not overwrite the newer results', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees(\?.*)?$/, route => route.fulfill({ headers, json: employees }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (new URL(route.request().url()).searchParams.get('from') === '2026-01-01') {
      // The older filter answers last, with a different row.
      await new Promise(resolve => setTimeout(resolve, 1500));
      return route.fulfill({ headers, json: [usageRows[1]] });
    }
    return route.fulfill({ headers, json: [usageRows[0]] });
  });
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('gridcell', { name: 'Keys', exact: true })).toBeVisible();

  // Both ends must be valid, or the page refuses the filter before any request, and the stale path is never exercised.
  await page.getByLabel('To', { exact: true }).fill('2026-11-06');
  await page.getByLabel('From', { exact: true }).fill('2026-01-01');
  await page.getByRole('button', { name: 'Apply' }).click();
  await page.getByLabel('From', { exact: true }).fill('2026-11-01');
  await page.getByRole('button', { name: 'Apply' }).click();

  await expect(page.getByRole('gridcell', { name: 'Keys', exact: true })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Bedsheets', exact: true })).toHaveCount(0, { timeout: 5000 });
  await expect.poll(async () => page.getByRole('gridcell', { name: 'Bedsheets', exact: true }).count(), { timeout: 3000 }).toBe(0);
});

test('an end date before the start date is refused without a request', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('gridcell', { name: 'Keys', exact: true })).toBeVisible();
  const before = state.usageQueries.length;

  await page.getByLabel('From', { exact: true }).fill('2026-11-06');
  await page.getByLabel('To', { exact: true }).fill('2026-11-01');
  await page.getByRole('button', { name: 'Apply' }).click();

  await expect(page.getByText('The end date must be on or after the start date.')).toBeVisible();
  expect(state.usageQueries.length).toBe(before);
});

test('admin opens the photos for an entry', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('button', { name: 'View 2' }).click();
  await expect(page.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Keys photo 2' })).toBeVisible();
});

test('CSV export downloads the filtered log', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('gridcell', { name: 'Keys', exact: true })).toBeVisible();
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('button', { name: 'Export CSV' }).click(),
  ]);
  expect(download.suggestedFilename()).toMatch(/^inventory-\d{4}-\d{2}-\d{2}\.csv$/);
});

test('adding a location that already exists shows the conflict message', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Locations' }).click();
  await page.getByRole('button', { name: 'Add location' }).click();
  await page.getByRole('dialog').getByLabel('Name').fill('Main store');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();

  await expect(page.getByText('A location named "Main store" already exists. Choose another name, or reactivate the existing location.')).toBeVisible();
  expect(state.posts.at(-1)?.body).toEqual({ name: 'Main store' });
});

test('admin verifies an employee-added item', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Items' }).click();
  await expect(page.getByText('Needs review')).toBeVisible();

  await page.getByRole('button', { name: 'Verify Mop heads' }).click();

  await expect(page.getByText('Mop heads verified.')).toBeVisible();
  await expect(page.getByText('Needs review')).toHaveCount(0);
  expect(state.posts.at(-1)?.body).toEqual({ version: 'v7' });
});

test('selecting an employee filters the log by that employee', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('textbox', { name: 'Employee' }).click();
  await page.getByRole('option', { name: 'Ben Cleaner' }).click();
  await page.getByRole('button', { name: 'Apply' }).click();
  await expect.poll(() => state.usageQueries.at(-1)).toContain('employeeId=employee-b');
});
