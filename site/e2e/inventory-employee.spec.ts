import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const locationId = '33333333-3333-4333-8333-333333333331';
const keysId = '33333333-3333-4333-8333-333333333332';
const tissuesId = '33333333-3333-4333-8333-333333333333';
const usageId = '44444444-4444-4444-8444-444444444441';
const photoId = '55555555-5555-4555-8555-555555555551';
const photo = { name: 'keys.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64') };

const locations = [{ id: locationId, name: 'Main store', isActive: true, version: 'v1' }];
const items = [
  { id: keysId, name: 'Keys', unit: 'pcs', isVerified: true, isActive: true, version: 'v1' },
  { id: tissuesId, name: 'Tissues', unit: 'box', isVerified: true, isActive: true, version: 'v1' },
];

async function setup(page: Page, options: { history?: unknown[] } = {}) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));

  const state = { history: [...(options.history ?? [])] as any[], posts: [] as { url: string; body: string }[], newItemId: null as string | null };

  await page.route(/\/api\/v1\/me\/inventory\/locations$/, route => route.fulfill({ headers, json: locations }));
  await page.route(/\/api\/v1\/me\/inventory\/items$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON() as { name: string; unit: string | null };
      state.posts.push({ url: route.request().url(), body: JSON.stringify(body) });
      const created = { id: '66666666-6666-4666-8666-666666666666', name: body.name, unit: body.unit || 'pcs', isVerified: false, isActive: true, version: 'v1' };
      state.newItemId = created.id;
      return route.fulfill({ headers, json: created });
    }
    return route.fulfill({ headers, json: [...items, ...(state.newItemId ? [{ id: state.newItemId, name: 'Mop heads', unit: 'pcs', isVerified: false, isActive: true, version: 'v1' }] : [])] });
  });
  await page.route(/\/api\/v1\/me\/inventory\/usage\/[^/]+\/photos\/[^/?]+(\?.*)?$/, route => route.fulfill({ headers, contentType: 'image/png', body: photo.buffer }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const body = route.request().postData() ?? '';
      state.posts.push({ url: route.request().url(), body });
      const itemIdMatch = /name="ItemId"\r\n\r\n([^\r]+)/.exec(body);
      const quantityMatch = /name="Quantity"\r\n\r\n([^\r]+)/.exec(body);
      const itemName = items.find(i => i.id === itemIdMatch?.[1])?.name ?? 'Mop heads';
      const entry = {
        id: usageId, employeeId: 'employee-a', employeeName: 'Test Staff', locationId, locationName: 'Main store',
        itemId: itemIdMatch?.[1], itemName, unit: 'pcs', quantity: Number(quantityMatch?.[1] ?? 1), note: '',
        takenUtc: new Date().toISOString(), photoIds: [photoId], photosPurged: false, version: 'v1',
      };
      state.history = [entry, ...state.history];
      return route.fulfill({ headers, json: entry });
    }
    return route.fulfill({ headers, json: state.history });
  });
  return state;
}

test('save stays disabled until a photo is attached', async ({ page }) => {
  await setup(page);
  await page.goto('/me/inventory/');
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();

  await page.getByLabel('Where did you take it from?').selectOption({ label: 'Main store' });
  await page.getByLabel('What did you take?').selectOption({ label: 'Keys' });
  const save = page.getByRole('button', { name: 'Save entry' });
  await expect(save).toBeDisabled();
  await expect(page.getByText('Attach at least one photo to save the entry.')).toBeVisible();

  await page.getByLabel('Photos (at least 1, up to 5)').setInputFiles(photo);
  await expect(page.getByRole('img', { name: 'Selected photo 1' })).toBeVisible();
  await expect(save).toBeEnabled();
});

test('quantity must be a whole number of at least one', async ({ page }) => {
  await setup(page);
  await page.goto('/me/inventory/');
  await page.getByLabel('Where did you take it from?').selectOption({ label: 'Main store' });
  await page.getByLabel('What did you take?').selectOption({ label: 'Keys' });
  await page.getByLabel('Photos (at least 1, up to 5)').setInputFiles(photo);

  await page.getByLabel(/Quantity/).fill('0');
  await expect(page.getByRole('button', { name: 'Save entry' })).toBeDisabled();
  await page.getByLabel(/Quantity/).fill('2');
  await expect(page.getByRole('button', { name: 'Save entry' })).toBeEnabled();
});

test('employee logs an existing item with a photo and sees it in their history', async ({ page }) => {
  const work = await setup(page);
  await page.goto('/me/inventory/');
  await expect(page.getByText('No entries yet.')).toBeVisible();

  await page.getByLabel('Where did you take it from?').selectOption({ label: 'Main store' });
  await page.getByLabel('What did you take?').selectOption({ label: 'Keys' });
  await page.getByLabel(/Quantity/).fill('2');
  await page.getByLabel('Note (optional)').fill('Spare set for room 4');
  await page.getByLabel('Photos (at least 1, up to 5)').setInputFiles(photo);
  await page.getByRole('button', { name: 'Save entry' }).click();

  await expect(page.getByText('Entry saved.')).toBeVisible();
  const post = work.posts.find(p => p.url.includes('/usage'))!;
  expect(post.body).toContain('name="LocationId"');
  expect(post.body).toContain(locationId);
  expect(post.body).toContain(keysId);
  expect(post.body).toContain('name="Photos"; filename="keys.png"');
  expect(post.body).toContain('Spare set for room 4');

  const history = page.locator('section[aria-labelledby="history-heading"]');
  await expect(history.getByText('Keys: 2 pcs')).toBeVisible();
  await expect(history.getByText('From Main store')).toBeVisible();
  await history.getByRole('button', { name: 'Show photos (1)' }).click();
  await expect(history.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save entry' })).toBeDisabled();
});

test('a failed history reload after a successful save is not reported as an unsaved entry', async ({ page }) => {
  const work = await setup(page);
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => {
    // Fail only the reload that follows the save; the first load and the save itself succeed.
    if (route.request().method() === 'GET' && work.posts.some(p => p.url.includes('/usage'))) return route.fulfill({ status: 503, headers });
    return route.fallback();
  });
  await page.goto('/me/inventory/');

  await page.getByLabel('Where did you take it from?').selectOption({ label: 'Main store' });
  await page.getByLabel('What did you take?').selectOption({ label: 'Keys' });
  await page.getByLabel('Photos (at least 1, up to 5)').setInputFiles(photo);
  await page.getByRole('button', { name: 'Save entry' }).click();

  await expect(page.getByText('Entry saved.')).toBeVisible();
  await expect(page.getByText('The entry was not saved')).toHaveCount(0);
  await expect(page.getByText('Could not load your history.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible();
});

test('employee adds a new item and it is used for the entry', async ({ page }) => {
  const work = await setup(page);
  await page.goto('/me/inventory/');

  await page.getByLabel('Where did you take it from?').selectOption({ label: 'Main store' });
  await page.getByLabel('What did you take?').selectOption({ label: '+ Add new item' });
  await page.getByLabel('New item name').fill('Mop heads');
  await page.getByLabel('Unit').fill('pcs');
  await page.getByLabel('Photos (at least 1, up to 5)').setInputFiles(photo);
  await page.getByRole('button', { name: 'Save entry' }).click();

  await expect(page.getByText('Entry saved.')).toBeVisible();
  const addItem = work.posts.find(p => p.url.endsWith('/items'))!;
  expect(JSON.parse(addItem.body)).toEqual({ name: 'Mop heads', unit: 'pcs' });
  const entry = work.posts.find(p => p.url.includes('/usage'))!;
  expect(entry.body).toContain('66666666-6666-4666-8666-666666666666');
});

test('history shows the photo count for entries whose photos were deleted by retention', async ({ page }) => {
  await setup(page, {
    history: [{
      id: usageId, employeeId: 'employee-a', employeeName: 'Test Staff', locationId, locationName: 'Main store',
      itemId: keysId, itemName: 'Keys', unit: 'pcs', quantity: 1, note: '', takenUtc: '2026-05-01T09:00:00Z',
      photoIds: [], photosPurged: true, version: 'v1',
    }],
  });
  await page.goto('/me/inventory/');
  await expect(page.getByText('Photos were deleted after 3 months.')).toBeVisible();
});

test('inventory page has no horizontal scroll', async ({ page }) => {
  await setup(page);
  await page.goto('/me/inventory/');
  await expect(page.getByRole('heading', { name: 'Inventory' })).toBeVisible();
  const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflows).toBe(false);
});
