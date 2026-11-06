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
    headers: { ...headers, 'Content-Type': 'text/csv; charset=utf-8', 'Content-Disposition': 'attachment; filename="inventory.csv"' },
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

  await expect(page.getByRole('cell', { name: 'Keys' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Ana Keeper' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Ben Cleaner' })).toBeVisible();
  await expect(page.getByText('Deleted', { exact: true })).toBeVisible();

  await page.getByLabel('From', { exact: true }).fill('2026-11-01');
  await page.getByLabel('To', { exact: true }).fill('2026-11-06');
  await page.getByRole('button', { name: 'Apply' }).click();

  await expect.poll(() => state.usageQueries.at(-1)).toContain('from=2026-11-01');
  expect(state.usageQueries.at(-1)).toContain('to=2026-11-06');
});

test('an end date before the start date is refused without a request', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('cell', { name: 'Keys' })).toBeVisible();
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
  await expect(page.getByRole('cell', { name: 'Keys' })).toBeVisible();
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
