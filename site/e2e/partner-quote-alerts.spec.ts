import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, setNavOpen } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const company = { id: 'quote-company', name: 'Apartment owner', businessId: '1234567-8', isActive: true, isPartnerEnabled: true };
const requested: Branch = { id: 'quote-branch', companyId: company.id, name: 'Lake apartment', city: 'Tampere', isActive: true, pricingReady: false, quoteRequestedUtc: '2026-11-01T10:00:00Z' };
for (const access of ['anonymous', 'denied'] as const) {
  test(`${access} admin navigation never reads or polls quotes`, async ({ page }) => {
    await setupAdminMocks(page, { authenticated: access !== 'anonymous' });
    if (access === 'denied') await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['employee']; });
    let quoteReads = 0;
    await page.route(/\/api\/v1\/admin\/partner\/quotes$/, route => { quoteReads++; return route.fulfill({ headers, json: [requested] }); });
    await page.clock.install();
    await page.goto('/admin/work-queue/');
    if (access === 'denied') await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
    else await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
    await page.clock.fastForward(60_000);
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.getByRole('status', { name: 'Pending quote requests' })).toHaveCount(0);
    expect(quoteReads).toBe(0);
  });
}
async function fixture(page: Page, initial: Branch[] = [requested]) {
  await setupAdminMocks(page);
  const state = { branches: initial, fail: false, quoteReads: 0, writes: [] as unknown[], work: [] as Record<string, unknown>[], changes: [] as Record<string, unknown>[] };
  await page.route(/\/api\/v1\/admin\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/partner/quotes')) {
      state.quoteReads++;
      return route.fulfill({ headers, status: state.fail ? 503 : 200, json: state.fail ? { detail: 'Try again' } : state.branches });
    }
    if (req.method() === 'PUT') {
      const body = req.postDataJSON(); state.writes.push({ path, body });
      state.branches = state.branches.map(branch => ({ ...branch, ...body, pricingReady: true }));
      return route.fulfill({ headers, json: state.branches[0] });
    }
    const json = path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' }
      : path.endsWith('/partner/requests') ? state.work
      : path.endsWith('/partner/change-requests') ? state.changes
      : path.endsWith('/partner/branches') ? state.branches
      : path.endsWith('/companies/' + company.id) ? { company, branches: state.branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 } : [];
    return route.fulfill({ headers, json });
  });
  return state;
}

test('admin sees banner and red branch tag, reviews quotes in the Work queue and pricing clears them', async ({ page }, testInfo) => {
  const state = await fixture(page);
  await page.goto('/admin/companies/');
  const banner = page.getByRole('status', { name: 'Pending quote requests' });
  await expect(banner).toContainText('1 branch awaiting a quote');
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await expect(page.getByText('Quote requested', { exact: true })).toHaveClass(/text-red-800/);
  await page.screenshot({ path: testInfo.outputPath('quote-request-banner.png'), fullPage: true });
  await banner.getByRole('link', { name: 'Review quotes in the Work queue' }).click();
  await expect(page).toHaveURL(/\/admin\/work-queue\/\?type=quote/);
  await expect(page.getByLabel('Type')).toHaveValue('quote');
  // The queue lists the quotes itself, so the banner is not repeated there.
  await expect(banner).toHaveCount(0);
  const row = page.getByRole('listitem').filter({ hasText: 'Lake apartment' });
  await expect(row).toContainText('Quote requested');
  await row.getByRole('button', { name: /^Set price/ }).click();
  const form = page.getByRole('dialog', { name: 'Price Lake apartment' });
  await form.getByLabel('Price excl. VAT (€)').fill('90');
  await form.getByLabel('Estimated duration (minutes)').fill('120');
  await form.getByRole('button', { name: 'Save price', exact: true }).click();
  await expect(row).toHaveCount(0);
  await page.goto('/admin/companies/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(banner).toHaveCount(0);
  expect(state.branches[0].quoteRequestedUtc).toBe(requested.quoteRequestedUtc);
  expect(state.writes).toContainEqual({ path: '/api/v1/admin/partner/branches/quote-branch/pricing', body: { priceNet: 90, vatRatePercent: 25.5, estimatedDurationMinutes: 120 } });
});

test('the quote banner link opens only the quotes, and choosing all types again drops the filter from the address', async ({ page }) => {
  await fixture(page);
  await page.goto('/admin/companies/');
  await page.getByRole('status', { name: 'Pending quote requests' }).getByRole('link', { name: 'Review quotes in the Work queue' }).click();
  await expect(page.getByRole('listitem')).toHaveCount(1);
  await page.getByLabel('Type').selectOption('all');
  await expect(page).not.toHaveURL(/type=quote/);
});

test('visible polling and focus discover new quotes, errors retain count and retry clears resolved quotes', async ({ page }) => {
  const state = await fixture(page, []);
  await page.clock.install();
  await page.goto('/admin/companies/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  const banner = page.getByRole('status', { name: 'Pending quote requests' });
  await expect(banner).toHaveCount(0);
  state.branches = [requested, { ...requested, id: 'second' }, { ...requested, id: 'inactive', isActive: false }, { ...requested, id: 'priced', pricingReady: true }];
  await page.clock.fastForward(30_000);
  await expect(banner).toContainText('2 branches awaiting quotes');
  state.fail = true;
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(banner).toContainText('Quote requests could not be checked.');
  await expect(banner).toContainText('2 branches awaiting quotes');
  state.fail = false; state.branches = [];
  await banner.getByRole('button', { name: 'Retry quote requests' }).click();
  await expect(banner).toHaveCount(0);
});

test('hidden tabs stop quote polling and returning to visibility refreshes without resetting a draft price', async ({ page }) => {
  const state = await fixture(page);
  await page.clock.install();
  await page.goto('/admin/work-queue/?type=quote');
  await page.getByRole('listitem').filter({ hasText: 'Lake apartment' }).getByRole('button', { name: /^Set price/ }).click();
  await page.getByRole('dialog').getByLabel('Price excl. VAT (€)').fill('77');
  await page.evaluate(() => Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' }));
  const reads = state.quoteReads;
  await page.clock.fastForward(60_000);
  expect(state.quoteReads).toBe(reads);
  await page.evaluate(() => { Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' }); document.dispatchEvent(new Event('visibilitychange')); });
  await expect.poll(() => state.quoteReads).toBeGreaterThan(reads);
  await expect(page.getByRole('dialog').getByLabel('Price excl. VAT (€)')).toHaveValue('77');
  expect(state.writes).toEqual([]);
});

test('failed initial queue read is visible and can be retried without reporting no requests', async ({ page }) => {
  const state = await fixture(page); state.fail = true;
  await page.goto('/admin/companies/');
  const banner = page.getByRole('status', { name: 'Pending quote requests' });
  await expect(banner).toContainText('Quote requests could not be checked.');
  await expect(banner).not.toContainText('awaiting a quote');
  state.fail = false;
  await banner.getByRole('button', { name: 'Retry quote requests' }).click();
  await expect(banner).toContainText('1 branch awaiting a quote');
});

test('pricing through Companies clears the shared banner and branch badge immediately', async ({ page }) => {
  const state = await fixture(page);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await expect(page.getByText('Quote requested', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Edit', exact: true }).last().click();
  const dialog = page.getByRole('dialog', { name: 'Edit branch' });
  await dialog.getByLabel('Fixed cleaning price, excl. VAT (€)').fill('90');
  await dialog.getByLabel('Estimated cleaning duration (minutes)').fill('120');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('status', { name: 'Pending quote requests' })).toHaveCount(0);
  await expect(page.getByText('Quote requested', { exact: true })).toHaveCount(0);
  expect(state.writes).toHaveLength(1);
});


test('pending action badges exclude cancelled and already arranged work, then refresh after resolution', async ({ page }, testInfo) => {
  const state = await fixture(page);
  const request = { id: 'pending-clean', companyId: company.id, branchId: requested.id, branchName: 'Pending apartment', version: 1, status: 'Requested', fromDate: '2026-12-01', toDate: '2026-12-02', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120 };
  state.work = [request, { ...request, id: 'cancelled', branchName: 'Cancelled apartment', status: 'Cancelled' }, { ...request, id: 'arranged', branchName: 'Arranged apartment', status: 'Offered' }];
  state.changes = [{ id: 'open-change', companyId: company.id, kind: 'Property', message: 'Please update access instructions', resolvedUtc: null }, { id: 'resolved-change', resolvedUtc: '2026-11-01T10:00:00Z' }];
  await page.goto('/admin/work-queue/');
  // The badge sits on the Work queue entry: one request needing an employee, one quote and one open change.
  await setNavOpen(page, true); await expect(page.getByLabel('3 pending partner actions', { exact: true })).toHaveText('3', { timeout: 20000 }); await setNavOpen(page, false);
  const list = page.getByRole('list', { name: 'Work waiting for the office' });
  await expect(list.getByRole('listitem').filter({ hasText: 'Pending apartment' })).toContainText('Needs an employee');
  await expect(list.getByRole('listitem').filter({ hasText: 'Arranged apartment' })).toContainText('Awaiting acceptance');
  await expect(list.getByRole('listitem').filter({ hasText: 'Cancelled apartment' })).toHaveCount(0);
  await expect(list.getByRole('listitem').filter({ hasText: 'Change request' })).toHaveCount(1);
  await page.screenshot({ path: testInfo.outputPath('admin-work-queue-partner.png'), fullPage: true });
  state.work = [{ ...request, status: 'Offered' }]; state.changes = []; state.branches = [];
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await setNavOpen(page, true); await expect(page.getByLabel(/pending partner actions$/)).toHaveCount(0, { timeout: 20000 }); await setNavOpen(page, false);
});


test('failed cleaning-count reads warn instead of silently reporting an empty queue and retry recovers', async ({ page }) => {
  await fixture(page, []);
  let failed = true;
  await page.route(/\/api\/v1\/admin\/partner\/requests$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    return route.fulfill(failed ? { status: 503, headers, json: {} } : { headers, json: [] });
  });
  await page.goto('/admin/companies/');
  await expect(page.getByRole('status', { name: 'Pending quote requests' }).getByRole('alert')).toContainText('Partner action counts could not be checked.');
  failed = false;
  await page.getByRole('button', { name: 'Retry partner counts', exact: true }).click();
  await expect(page.getByText('Partner action counts could not be checked.', { exact: false })).toHaveCount(0);
});
