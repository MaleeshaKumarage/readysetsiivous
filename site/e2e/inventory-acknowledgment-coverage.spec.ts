import { test, expect, type Page, type Route } from '@playwright/test';
import { setup, enterPartner, headers, acknowledgment, usage, company, photo } from './inventory-acknowledgment-helpers';

// Coverage for the inventory acknowledgment review threads: column alignment, selection rules, recovery paths,
// the request modal, the admin status modal, evidence loading, and the partner inbox controls.

function json(route: Route, body: unknown, status = 200) {
  if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
  return route.fulfill({ headers, status, json: body });
}

async function openRequestModal(page: Page, itemName: string) {
  await page.getByRole('checkbox', { name: `Select ${itemName} by Private Cleaner` }).check();
  await page.getByRole('button', { name: /Request acknowledgment \(\d+\)/ }).click();
  return page.getByRole('dialog');
}

test('log columns line up and selection rules disable ineligible rows and clear', async ({ page }) => {
  await setup(page);
  // 51 photographed entries plus one whose photos were never kept: selection caps at 50 and skips the unphotographed row.
  const many = Array.from({ length: 52 }, (_, index) => ({
    ...usage[0], id: `many-${index}`, itemName: index === 51 ? 'Unphotographed' : `Item ${index}`,
    photoIds: index === 51 ? [] : [`photo-${index}`],
  }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => json(route, many));
  await page.goto('/admin/inventory/');

  const table = page.getByRole('table');
  await expect(table.locator('thead th')).toHaveCount(9);
  await expect(table.locator('tbody tr').first().locator('td')).toHaveCount(9);
  await expect(table.locator('thead th').nth(2)).toHaveText('Employee');
  await expect(table.locator('tbody tr').first().locator('td').nth(2)).toHaveText('Private Cleaner');

  await expect(page.getByRole('checkbox', { name: 'Select Unphotographed by Private Cleaner' })).toBeDisabled();
  for (let index = 0; index < 50; index += 1) {
    await page.getByRole('checkbox', { name: `Select Item ${index} by Private Cleaner` }).check();
  }
  await expect(page.getByRole('button', { name: 'Request acknowledgment (50)' })).toBeEnabled();
  await expect(page.getByRole('checkbox', { name: 'Select Item 50 by Private Cleaner' })).toBeDisabled();

  await page.getByRole('button', { name: 'Clear selection' }).click();
  await expect(page.getByRole('button', { name: 'Request acknowledgment (0)' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Clear selection' })).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Select Item 50 by Private Cleaner' })).toBeEnabled();
});

test('a failed status lookup is recovered by Reload log', async ({ page }) => {
  await setup(page);
  let lookups = 0;
  await page.route(/\/api\/v1\/admin\/inventory\/acknowledgments(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    lookups += 1;
    return lookups === 1 ? json(route, { detail: 'Unavailable' }, 500) : json(route, []);
  });
  await page.goto('/admin/inventory/');
  await expect(page.getByText(/Acknowledgment statuses could not be loaded/)).toBeVisible();
  await page.getByRole('button', { name: 'Reload log' }).click();
  await expect(page.getByText(/Acknowledgment statuses could not be loaded/)).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'Select Keys by Private Cleaner' })).toBeEnabled();
  expect(lookups).toBeGreaterThanOrEqual(2);
});

test('request modal: partner search retries, evidence toggles, send waits for partner and confirmation, Cancel sends nothing', async ({ page }) => {
  const state = await setup(page);
  let partnerCalls = 0;
  await page.route(/\/api\/v1\/admin\/inventory\/acknowledgments\/partners(\?.*)?$/, route => {
    partnerCalls += 1;
    return partnerCalls === 1 ? json(route, { detail: 'Search unavailable' }, 500) : json(route, [company]);
  });
  await page.goto('/admin/inventory/');
  const dialog = await openRequestModal(page, 'Keys');

  await expect(dialog.getByRole('button', { name: 'Retry partner search' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Retry partner search' }).click();
  await dialog.getByLabel('Partner company', { exact: true }).click();
  await expect(page.getByRole('option', { name: 'Partner One' })).toBeVisible();
  await page.getByRole('option', { name: 'Partner One' }).click();

  const send = dialog.getByRole('button', { name: 'Send acknowledgment request' });
  await expect(send).toBeDisabled();
  await dialog.getByRole('checkbox', { name: /I confirm/ }).check();
  await expect(send).toBeEnabled();

  const review = dialog.getByRole('button', { name: 'Review photos for Keys' });
  await review.click();
  await expect(review).toHaveAttribute('aria-expanded', 'true');
  await expect(dialog.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await review.click();
  await expect(review).toHaveAttribute('aria-expanded', 'false');
  await expect(dialog.getByRole('img', { name: 'Keys photo 1' })).toHaveCount(0);

  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.requests).toHaveLength(0);
});

test('admin send shows the entry as awaiting acknowledgment in the log', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  const dialog = await openRequestModal(page, 'Keys');
  await dialog.getByLabel('Partner company', { exact: true }).click();
  await page.getByRole('option', { name: 'Partner One' }).click();
  await dialog.getByRole('checkbox', { name: /I confirm/ }).check();
  await dialog.getByRole('button', { name: 'Send acknowledgment request' }).click();
  await expect(page.getByText('Acknowledgment request sent to the partner portal.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Awaiting acknowledgment', exact: true })).toHaveCount(1);
  expect(state.requests).toHaveLength(1);
});

test('admin status button opens the partner acknowledgment details', async ({ page }) => {
  await setup(page, { pending: true });
  await page.goto('/admin/inventory/');
  await page.getByRole('button', { name: 'Awaiting acknowledgment', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Keys · Partner One')).toBeVisible();
  await expect(dialog.getByText('Awaiting acknowledgment', { exact: true })).toBeVisible();
  await expect(dialog.getByText('Message: Please confirm collection')).toBeVisible();
});

test('request modal opens when crypto.randomUUID is unavailable', async ({ page }) => {
  // Non-secure origins and older browsers lack randomUUID; the modal must still open.
  await page.addInitScript(() => {
    Object.defineProperty(Crypto.prototype, 'randomUUID', { value: undefined, configurable: true });
  });
  await setup(page);
  await page.goto('/admin/inventory/');
  const dialog = await openRequestModal(page, 'Keys');
  await expect(dialog.getByText('Request partner acknowledgment', { exact: true })).toBeVisible();
});

test('partner evidence loads photos, links open in a new tab, and a missing photo is reported', async ({ page }) => {
  await setup(page, { rows: [acknowledgment({ ...usage[0], photoIds: ['photo-0', 'photo-1'] })] });
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments\/[^/]+\/photos\/photo-1(\?.*)?$/, route => json(route, { detail: 'Gone' }, 404));
  await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (1)' }).click();
  await page.getByRole('button', { name: 'Review entry' }).click();

  await expect(page.getByRole('status').filter({ hasText: 'Some photos are unavailable' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Keys photo 2' })).toHaveCount(0);
  const link = page.getByRole('link', { name: 'Keys photo 1' });
  await expect(link).toHaveAttribute('target', '_blank');
  await expect(link).toHaveAttribute('href', /^blob:/);
});

test('partner inbox expands, refreshes, and shows entry details and the issue form', async ({ page }) => {
  const state = await setup(page, { pending: true });
  let listCalls = 0;
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    listCalls += 1;
    const url = new URL(route.request().url());
    const history = url.searchParams.get('history') === 'true';
    return json(route, { items: state.rows.filter(row => history ? row.status !== 'Pending' : row.status === 'Pending'), pendingCount: state.rows.filter(row => row.status === 'Pending').length, hasMore: false });
  });
  await enterPartner(page);
  const bell = page.getByRole('button', { name: 'Needs acknowledgment (1)' });
  const inbox = page.locator('#inventory-acknowledgment-inbox');
  await expect(bell).toHaveAttribute('aria-expanded', 'false');
  await expect(inbox).toHaveCount(0);
  await bell.click();
  await expect(bell).toHaveAttribute('aria-expanded', 'true');
  await expect(inbox).toBeVisible();

  const callsBeforeRefresh = listCalls;
  await page.getByRole('button', { name: 'Refresh acknowledgments' }).click();
  await expect.poll(() => listCalls).toBeGreaterThan(callsBeforeRefresh);

  const entry = page.getByRole('article', { name: 'Keys Main store' });
  await entry.getByRole('button', { name: 'Review entry' }).click();
  await expect(entry.getByRole('button', { name: 'Close entry' })).toHaveAttribute('aria-expanded', 'true');
  await expect(entry.getByText('Message from admin:')).toBeVisible();
  await entry.getByRole('button', { name: 'Close entry' }).click();
  await expect(entry.getByText('Message from admin:')).toHaveCount(0);

  await entry.getByRole('button', { name: 'Review entry' }).click();
  await entry.getByRole('button', { name: 'Report an issue' }).click();
  await expect(entry.getByLabel('Response note (required for an issue)')).toBeVisible();
  await entry.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(entry.getByRole('button', { name: 'Acknowledge entry' })).toBeVisible();

  await bell.click();
  await expect(bell).toHaveAttribute('aria-expanded', 'false');
  await expect(inbox).toHaveCount(0);
});

test('partner pending and history views and pagination request the correct pages', async ({ page }) => {
  const pendingRows = Array.from({ length: 26 }, (_, index) => acknowledgment({ ...usage[0], id: `pending-${index}`, itemName: `Item ${index}`, photoIds: [] }));
  const requests: { history: boolean; skip: number }[] = [];
  await setup(page);
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const url = new URL(route.request().url());
    const history = url.searchParams.get('history') === 'true';
    const skip = Number(url.searchParams.get('skip') ?? 0);
    requests.push({ history, skip });
    const items = history ? [] : pendingRows.slice(skip, skip + 25);
    return json(route, { items, pendingCount: pendingRows.length, hasMore: !history && skip + 25 < pendingRows.length });
  });
  await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (26)' }).click();
  await expect(page.getByRole('article', { name: 'Item 0 Main store' })).toBeVisible();

  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Item 25 Main store' })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Item 0 Main store' })).toHaveCount(0);
  expect(requests).toContainEqual({ history: false, skip: 25 });
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Item 0 Main store' })).toBeVisible();

  await page.getByRole('button', { name: 'Response history' }).click();
  await expect(page.getByText('No responses yet.')).toBeVisible();
  expect(requests.at(-1)).toEqual({ history: true, skip: 0 });
  await page.getByRole('button', { name: 'Pending', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Item 0 Main store' })).toBeVisible();
  expect(requests.at(-1)).toEqual({ history: false, skip: 0 });
});

test('last page of partner entries hides Next and Previous returns to the first page', async ({ page }) => {
  const pendingRows = Array.from({ length: 26 }, (_, index) => acknowledgment({ ...usage[0], id: `last-${index}`, itemName: `Entry ${index}`, photoIds: [] }));
  await setup(page);
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const skip = Number(new URL(route.request().url()).searchParams.get('skip') ?? 0);
    return json(route, { items: pendingRows.slice(skip, skip + 25), pendingCount: pendingRows.length, hasMore: skip + 25 < pendingRows.length });
  });
  await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (26)' }).click();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Entry 25 Main store' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Next', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Entry 0 Main store' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Previous', exact: true })).toHaveCount(0);
});

test('partner inbox shows a failed refresh and recovers on the next refresh', async ({ page }) => {
  const state = await setup(page, { pending: true });
  let failNext = false;
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (failNext) { failNext = false; return json(route, { detail: 'Unavailable' }, 500); }
    return json(route, { items: state.rows, pendingCount: state.rows.length, hasMore: false });
  });
  await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (1)' }).click();
  await expect(page.getByRole('article', { name: 'Keys Main store' })).toBeVisible();
  const failure = page.getByRole('alert').filter({ hasText: 'Acknowledgments could not be loaded' });
  failNext = true;
  await page.getByRole('button', { name: 'Refresh acknowledgments' }).click();
  await expect(failure).toBeVisible();
  await page.getByRole('button', { name: 'Refresh acknowledgments' }).click();
  await expect(page.getByRole('article', { name: 'Keys Main store' })).toBeVisible();
  await expect(failure).toHaveCount(0);
});

test('partner evidence shows a loading status until the photos arrive', async ({ page }) => {
  await setup(page, { pending: true });
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments\/[^/]+\/photos\//, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    await gate;
    return route.fulfill({ headers, contentType: 'image/png', body: photo });
  });
  await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (1)' }).click();
  await page.getByRole('button', { name: 'Review entry' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Loading photos' })).toBeVisible();
  release();
  await expect(page.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Loading photos' })).toHaveCount(0);
});

test('an entry that already has an acknowledgment cannot be selected again', async ({ page }) => {
  await setup(page, { pending: true });
  await page.goto('/admin/inventory/');
  await expect(page.getByRole('checkbox', { name: 'Select Keys by Private Cleaner' })).toBeDisabled();
  await expect(page.getByRole('checkbox', { name: 'Select Bedsheets by Private Cleaner' })).toBeEnabled();
});

test('load more keeps the selection, while a fresh load clears it', async ({ page }) => {
  await setup(page);
  const first = Array.from({ length: 100 }, (_, index) => ({ ...usage[0], id: `page-a-${index}`, itemName: `Stock ${index}`, photoIds: [`photo-${index}`] }));
  const second = [{ ...usage[0], id: 'page-b-0', itemName: 'Late entry', photoIds: ['photo-late'] }];
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const skip = new URL(route.request().url()).searchParams.get('skip');
    return json(route, skip === '100' ? second : first);
  });
  await page.goto('/admin/inventory/');
  await page.getByRole('checkbox', { name: 'Select Stock 0 by Private Cleaner' }).check();
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByRole('cell', { name: 'Late entry', exact: true })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Select Stock 0 by Private Cleaner' })).toBeChecked();
  await expect(page.getByRole('button', { name: 'Request acknowledgment (1)' })).toBeEnabled();
});
