import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const photo = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');
const company = { id: 'company-one', name: 'Partner One', isActive: true, isPartnerEnabled: true };
const usage = ['Keys', 'Bedsheets'].map((name, index) => ({ id: `usage-${index}`, employeeId: 'cleaner', employeeName: 'Private Cleaner', locationId: 'store', locationName: 'Main store', itemId: `item-${index}`, itemName: name, quantity: index + 1, unit: 'pcs', note: 'Collected at reception', takenUtc: '2026-11-09T08:00:00Z', photoIds: [`photo-${index}`], photosPurged: false, version: 'v1' }));
function acknowledgment(row = usage[0]) { return { ...row, batchId: 'batch-one', companyId: company.id, companyName: company.name, message: 'Please confirm collection', status: 'Pending', responseNote: '', respondedByName: null, respondedUtc: null, createdUtc: '2026-11-09T09:00:00Z' }; }

async function setup(page: Page, options: { pending?: boolean; failSend?: boolean; failResponse?: boolean; failLookup?: boolean; expiredPhoto?: boolean } = {}) {
  await setupAdminMocks(page);
  const state = { requests: [] as any[], responses: [] as any[], rows: options.pending ? [acknowledgment()] as any[] : [], searches: [] as string[] };
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/(?:admin\/inventory|partner\/)/, route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.includes('/photos/')) return options.expiredPhoto ? route.fulfill({ headers, status: 404 }) : route.fulfill({ headers, contentType: 'image/png', body: photo });
    if (path.endsWith('/acknowledgments/partners')) { state.searches.push(url.searchParams.get('search') ?? ''); return route.fulfill({ headers, json: [company] }); }
    if (path.endsWith('/inventory/acknowledgments')) {
      if (request.method() === 'GET') return route.fulfill({ headers, status: options.failLookup ? 500 : 200, json: options.failLookup ? { detail: 'Unavailable' } : state.rows });
      const body = request.postDataJSON(); state.requests.push(body);
      if (options.failSend) return route.fulfill({ headers, status: 409, json: { detail: 'An acknowledgment has already been requested for a selected entry. Refresh the log.' } });
      state.rows = usage.filter(row => body.usageIds.includes(row.id)).map(row => ({ ...acknowledgment(row), ...body }));
      return route.fulfill({ headers, json: state.rows });
    }
    if (path.endsWith('/inventory/usage')) return route.fulfill({ headers, json: usage });
    if (path.endsWith('/inventory-acknowledgments')) {
      const history = url.searchParams.get('history') === 'true';
      return route.fulfill({ headers, json: { items: state.rows.filter(row => history ? row.status !== 'Pending' : row.status === 'Pending'), pendingCount: state.rows.filter(row => row.status === 'Pending').length, hasMore: false } });
    }
    if (path.endsWith('/response')) {
      const body = request.postDataJSON(); state.responses.push(body);
      if (options.failResponse) return route.fulfill({ headers, status: 409, json: { detail: 'Already changed.' } });
      const id = path.split('/').at(-2);
      state.rows = state.rows.map(row => row.id === id ? { ...row, status: body.status, responseNote: body.note, respondedByName: 'Test Owner', respondedUtc: '2026-11-09T10:00:00Z' } : row);
      return route.fulfill({ headers, json: state.rows.find(row => row.id === id) });
    }
    if (path.endsWith('/partner/me')) return route.fulfill({ headers, json: { realm: 'readysetsiivous', person: { id: 'owner', role: 'partner', isActive: true }, company, timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: path.endsWith('/settings') ? { photoLabels: [], account: {} } : [] });
  });
  return state;
}

async function enterPartner(page: Page) {
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['partner']; });
  await page.goto('/partner/');
}

test('admin bulk request appears in partner inbox, responses return to admin with history', async ({ page }, testInfo) => {
  const state = await setup(page);
  await page.goto('/admin/inventory/');
  await page.getByRole('checkbox', { name: 'Select Keys by Private Cleaner' }).check();
  await page.getByRole('checkbox', { name: 'Select Bedsheets by Private Cleaner' }).check();
  await page.getByRole('button', { name: 'Request acknowledgment (2)' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Partner company', { exact: true }).click();
  await page.getByRole('option', { name: 'Partner One' }).click();
  await expect(dialog.getByRole('button', { name: 'Send acknowledgment request' })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Review photos for Keys' }).click();
  await expect(dialog.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await dialog.getByLabel('Message to partner (optional)').fill('Please confirm collection');
  await dialog.getByRole('checkbox', { name: /I confirm/ }).check();
  await page.screenshot({ path: testInfo.outputPath('admin-acknowledgment-review.png'), fullPage: true });
  await dialog.getByRole('button', { name: 'Send acknowledgment request' }).click();
  await expect(page.getByText('Acknowledgment request sent to the partner portal.')).toBeVisible();
  expect(state.requests).toHaveLength(1); expect(state.requests[0].usageIds).toEqual(['usage-0', 'usage-1']);
  await expect(page.getByRole('checkbox', { name: 'Select Keys by Private Cleaner' })).toBeDisabled();
  await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (2)' }).click();
  const inbox = page.getByRole('region', { name: 'Inventory acknowledgments' });
  await expect(inbox.getByText('Private Cleaner')).toHaveCount(0);
  const entry = inbox.getByRole('article', { name: 'Keys Main store' });
  await entry.getByRole('button', { name: 'Review entry' }).click();
  await expect(entry.getByRole('img', { name: 'Keys photo 1' })).toBeVisible();
  await expect(entry.getByText('Please confirm collection')).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('partner-acknowledgment-review.png'), fullPage: true });
  await entry.getByRole('button', { name: 'Acknowledge entry' }).click();
  await expect(inbox.getByText('Your response was recorded.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Needs acknowledgment (1)' })).toBeVisible();
  await inbox.getByRole('button', { name: 'Response history' }).click();
  await expect(inbox.getByText('Acknowledged', { exact: true })).toBeVisible();
  expect(state.responses).toEqual([{ version: 'v1', status: 'Acknowledged', note: '' }]);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['admin']; });
  await page.goto('/admin/inventory/');
  await page.getByRole('button', { name: 'Acknowledged', exact: true }).click();
  await expect(page.getByRole('dialog').getByText(/Test Owner/)).toBeVisible();
});

test('partner must explain an issue and can review it in history', async ({ page }) => {
  const state = await setup(page, { pending: true }); await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (1)' }).click();
  await page.getByRole('button', { name: 'Review entry' }).click();
  await page.getByRole('button', { name: 'Report an issue' }).click();
  await page.getByRole('button', { name: 'Send issue' }).click();
  await expect(page.getByText('Describe the issue before sending.')).toBeVisible(); expect(state.responses).toHaveLength(0);
  await page.getByLabel('Response note (required for an issue)').fill('Wrong key set');
  await page.getByRole('button', { name: 'Send issue' }).click();
  await page.getByRole('button', { name: 'Response history' }).click();
  await expect(page.getByText('Issue reported', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Review entry' }).click();
  await expect(page.getByText('Wrong key set')).toBeVisible();
  expect(state.responses[0]).toMatchObject({ status: 'IssueReported', note: 'Wrong key set' });
});

test('stale response stays visible and requires refresh before another write', async ({ page }) => {
  const state = await setup(page, { pending: true, failResponse: true }); await enterPartner(page);
  await page.getByRole('button', { name: 'Needs acknowledgment (1)' }).click();
  await page.getByRole('button', { name: 'Review entry' }).click();
  await page.getByRole('button', { name: 'Acknowledge entry' }).click();
  await expect(page.getByText(/The response could not be confirmed/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Acknowledge entry' })).toBeDisabled();
  expect(state.responses).toHaveLength(1);
  await page.getByRole('button', { name: 'Refresh acknowledgments' }).click();
  await expect(page.getByRole('button', { name: 'Acknowledge entry' })).toBeEnabled();
});

test('expired photo is explained and Finnish inbox stays within phone width', async ({ page }) => {
  await setup(page, { pending: true, expiredPhoto: true }); await enterPartner(page);
  await page.getByRole('button', { name: 'Suomi' }).click();
  await page.getByRole('button', { name: 'Odottaa kuittausta (1)' }).click();
  await page.getByRole('button', { name: 'Tarkista kirjaus' }).click();
  await expect(page.getByText(/Osa kuvista ei ole enää saatavilla/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('failed status lookup disables sharing until the log is reloaded', async ({ page }) => {
  await setup(page, { failLookup: true }); await page.goto('/admin/inventory/');
  await expect(page.getByText(/Acknowledgment statuses could not be loaded/)).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Select Keys by Private Cleaner' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Request acknowledgment (0)' })).toBeDisabled();
});

test('send conflict keeps reviewed entries and confirmation visible', async ({ page }) => {
  const state = await setup(page, { failSend: true }); await page.goto('/admin/inventory/');
  await page.getByRole('checkbox', { name: 'Select Keys by Private Cleaner' }).check();
  await page.getByRole('button', { name: 'Request acknowledgment (1)' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Partner company', { exact: true }).click(); await page.getByRole('option', { name: 'Partner One' }).click();
  await dialog.getByRole('checkbox', { name: /I confirm/ }).check();
  await dialog.getByRole('button', { name: 'Send acknowledgment request' }).click();
  await expect(dialog.getByRole('alert')).toContainText('already been requested');
  await expect(dialog.getByRole('checkbox', { name: /I confirm/ })).toBeChecked(); expect(state.requests).toHaveLength(1);
});
