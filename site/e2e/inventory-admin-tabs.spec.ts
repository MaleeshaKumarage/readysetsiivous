import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');

const employees = [
  { id: 'employee-a', firstName: 'Ana', lastName: 'Keeper', email: 'ana@example.test', role: 'employee', isActive: true, identitySyncPending: false, skills: [], serviceAreas: [], defaultHours: {} },
  { id: 'employee-b', firstName: 'Ben', lastName: 'Cleaner', email: 'ben@example.test', role: 'employee', isActive: false, identitySyncPending: false, skills: [], serviceAreas: [], defaultHours: {} },
];

const keysRow = { id: 'usage-1', employeeId: 'employee-a', employeeName: 'Ana Keeper', locationId: 'loc-1', locationName: 'Main store', itemId: 'item-1', itemName: 'Keys', unit: 'pcs', quantity: 2, note: 'Spare set', takenUtc: '2026-11-03T08:30:00Z', photoIds: ['photo-1', 'photo-2'], photosPurged: false, version: 'v1' };
const bedsheetsRow = { id: 'usage-2', employeeId: 'employee-b', employeeName: 'Ben Cleaner', locationId: 'loc-1', locationName: 'Main store', itemId: 'item-2', itemName: 'Bedsheets', unit: 'pcs', quantity: 4, note: '', takenUtc: '2026-05-02T08:30:00Z', photoIds: [], photosPurged: true, version: 'v1' };

type State = { usageQueries: URL[]; posts: { url: string; body: any }[]; puts: { url: string; body: any }[]; locations: any[]; items: any[] };

async function setup(page: Page, options: { pageOneRows?: unknown[] } = {}): Promise<State> {
  await setupAdminMocks(page, { authenticated: true });
  const state: State = {
    usageQueries: [], posts: [], puts: [],
    locations: [{ id: 'loc-1', name: 'Main store', isActive: true, version: 'v1' }],
    items: [{ id: 'item-1', name: 'Keys', unit: 'pcs', isVerified: true, isActive: true, version: 'v1' }],
  };
  await page.route(/\/api\/v1\/admin\/employees(\?.*)?$/, route => route.fulfill({ headers, json: employees }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage\/[^/]+\/photos\/[^/?]+(\?.*)?$/, route => route.fulfill({ headers, contentType: 'image/png', body: png }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage\/export(\?.*)?$/, route => route.fulfill({ headers: { ...headers, 'Content-Type': 'text/csv; charset=utf-8' }, body: '"Taken (Helsinki)"\r\n' }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    state.usageQueries.push(url);
    const employeeId = url.searchParams.get('employeeId');
    const skip = Number(url.searchParams.get('skip') ?? 0);
    if (options.pageOneRows && skip > 0) return route.fulfill({ headers, json: [{ ...keysRow, id: 'late', itemName: 'Late entry' }] });
    const rows = options.pageOneRows ?? [keysRow, bedsheetsRow];
    return route.fulfill({ headers, json: employeeId ? rows.filter((row: any) => row.employeeId === employeeId) : rows });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/locations(\?.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON();
      state.posts.push({ url: route.request().url(), body });
      const created = { id: 'loc-2', name: body.name, isActive: true, version: 'v1' };
      state.locations.push(created);
      return route.fulfill({ headers, json: created });
    }
    return route.fulfill({ headers, json: state.locations });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/locations\/[^/]+$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = route.request().postDataJSON();
    state.puts.push({ url: route.request().url(), body });
    const id = route.request().url().split('/').pop()!;
    state.locations = state.locations.map(loc => loc.id === id ? { ...loc, name: body.name, isActive: body.isActive, version: 'v2' } : loc);
    return route.fulfill({ headers, json: state.locations.find(loc => loc.id === id) });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/items(\?.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON();
      state.posts.push({ url: route.request().url(), body });
      const created = { id: 'item-9', name: body.name, unit: body.unit || 'pcs', isVerified: true, isActive: true, version: 'v1' };
      state.items.push(created);
      return route.fulfill({ headers, json: created });
    }
    return route.fulfill({ headers, json: state.items });
  });
  await page.route(/\/api\/v1\/admin\/inventory\/items\/[^/]+$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = route.request().postDataJSON();
    state.puts.push({ url: route.request().url(), body });
    const id = route.request().url().split('/').pop()!;
    state.items = state.items.map(item => item.id === id ? { ...item, name: body.name, unit: body.unit ?? 'pcs', version: 'v2' } : item);
    return route.fulfill({ headers, json: state.items.find(item => item.id === id) });
  });
  return state;
}

test('each admin tab renders its own panel', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('cell', { name: 'Keys', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Export CSV' })).toBeVisible();

  await page.getByRole('tab', { name: 'Locations', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Add location' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Main store', exact: true })).toBeVisible();

  await page.getByRole('tab', { name: 'Items', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Add item' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Keys', exact: true })).toBeVisible();
});

test('the employee filter sends the chosen employee and narrows the log, including a deactivated employee', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('cell', { name: 'Keys', exact: true })).toBeVisible();

  await page.locator('input[aria-label="Employee"]').click();
  await page.getByRole('option', { name: 'Ben Cleaner (inactive)' }).click();
  await page.getByRole('button', { name: 'Apply' }).click();

  await expect.poll(() => state.usageQueries.at(-1)?.searchParams.get('employeeId')).toBe('employee-b');
  await expect(page.getByRole('cell', { name: 'Bedsheets', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Keys', exact: true })).toHaveCount(0);
});

test('the photo dialog opens for an entry and closes with Escape', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('button', { name: 'View 2' }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await expect(dialog.getByRole('img', { name: 'Keys photo 2' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('an entry with no photos says so instead of loading forever', async ({ page }) => {
  await setup(page, { pageOneRows: [{ ...keysRow, photoIds: [], photosPurged: false }] });
  await page.goto('/admin/inventory/');
  await page.getByRole('button', { name: 'View 0' }).click();
  await expect(page.getByText('No photos were attached to this entry.')).toBeVisible();
});

test('Load more fetches the next page when a full page was returned', async ({ page }) => {
  const hundred = Array.from({ length: 100 }, (_, index) => ({ ...keysRow, id: `row-${index}`, itemName: `Item ${index}`, photoIds: [], photosPurged: true }));
  const state = await setup(page, { pageOneRows: hundred });
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('cell', { name: 'Item 0', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Load more', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Late entry', exact: true })).toBeVisible();
  expect(state.usageQueries.at(-1)?.searchParams.get('skip')).toBe('100');
  await expect(page.getByRole('button', { name: 'Load more', exact: true })).toHaveCount(0);
});

test('a location can be edited, deactivated, and an edit can be cancelled', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Locations', exact: true }).click();

  await page.getByRole('button', { name: 'Edit Main store' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Name/).fill('Main store East');
  await dialog.getByText('Active', { exact: true }).click();
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();

  await expect(page.getByText('Location saved.')).toBeVisible();
  expect(state.puts.at(-1)?.body).toEqual({ name: 'Main store East', isActive: false, version: 'v1' });

  await page.getByRole('button', { name: 'Edit Main store East' }).click();
  await page.getByRole('dialog').getByLabel(/^Name/).fill('Something else');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.puts).toHaveLength(1);
});

test('a location can be added from the Locations tab', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Locations', exact: true }).click();

  await page.getByRole('button', { name: 'Add location', exact: true }).click();
  await page.getByRole('dialog').getByLabel(/^Name/).fill('Warehouse B');
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();

  await expect(page.getByText('Location added.')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Warehouse B', exact: true })).toBeVisible();
  expect(state.posts.at(-1)?.body).toEqual({ name: 'Warehouse B' });
});

test('an item can be added with a unit from the Items tab', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Items', exact: true }).click();

  await page.getByRole('button', { name: 'Add item', exact: true }).click();
  await page.getByRole('dialog').getByLabel(/^Name/).fill('Tissues');
  await page.getByRole('dialog').getByLabel('Unit', { exact: true }).fill('box');
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();

  await expect(page.getByText('Item added.')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Tissues', exact: true })).toBeVisible();
  expect(state.posts.at(-1)?.body).toEqual({ name: 'Tissues', unit: 'box' });
});

test('an existing item can be edited from the Items tab', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Items', exact: true }).click();

  await page.getByRole('button', { name: 'Edit Keys' }).click();
  await page.getByRole('dialog').getByLabel('Unit', { exact: true }).fill('set');
  await page.getByRole('dialog').getByText('Active', { exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();

  await expect(page.getByText('Item saved.')).toBeVisible();
  expect(state.puts.at(-1)?.body).toEqual({ name: 'Keys', unit: 'set', isActive: false, version: 'v1' });
});

test('Export CSV downloads the log for the filters currently applied', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('cell', { name: 'Keys', exact: true })).toBeVisible();

  await page.locator('input[aria-label="Employee"]').click();
  await page.getByRole('option', { name: 'Ana Keeper' }).click();
  await page.getByRole('button', { name: 'Apply' }).click();
  await expect.poll(() => state.usageQueries.at(-1)?.searchParams.get('employeeId')).toBe('employee-a');

  const exportRequest = page.waitForRequest(request => request.url().includes('/usage/export'));
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('button', { name: 'Export CSV', exact: true }).click(),
  ]);
  const url = new URL((await exportRequest).url());
  expect(url.searchParams.get('employeeId')).toBe('employee-a');
  expect(download.suggestedFilename()).toMatch(/^inventory-\d{4}-\d{2}-\d{2}\.csv$/);
});

test('a photo that fails to load shows an error instead of spinning', async ({ page }) => {
  await setup(page);
  await page.route(/\/api\/v1\/admin\/inventory\/usage\/[^/]+\/photos\/[^/?]+(\?.*)?$/, route => route.fulfill({ status: 503, headers }));
  await page.goto('/admin/inventory/');
  await page.getByRole('button', { name: 'View 2' }).click();
  await expect(page.getByText('Photos could not be loaded.')).toBeVisible();
  await expect(page.getByRole('dialog').getByRole('img', { name: 'Keys photo 1' })).toHaveCount(0);
});
