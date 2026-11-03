import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
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
    await page.goto('/admin/partner-requests/');
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

test('admin sees banner and red branch tag, reviews quotes directly and pricing clears them', async ({ page }, testInfo) => {
  const state = await fixture(page);
  await page.goto('/admin/companies/');
  const banner = page.getByRole('status', { name: 'Pending quote requests' });
  await expect(banner).toContainText('1 branch awaiting a quote');
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await expect(page.getByText('Quote requested', { exact: true })).toHaveClass(/text-red-800/);
  await page.screenshot({ path: testInfo.outputPath('quote-request-banner.png'), fullPage: true });
  await banner.getByRole('link', { name: 'Review quotes' }).click();
  await expect(page.getByRole('tab', { name: 'Quote requests', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Quote requested', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Set cleaning price', exact: true }).click();
  const form = page.getByRole('dialog', { name: /^Price / });
  await form.getByLabel('Fixed cleaning price excl. VAT (€)').fill('90');
  await form.getByLabel('Estimated duration (minutes)').fill('120');
  await form.getByRole('button', { name: 'Save pricing', exact: true }).click();
  await expect(banner).toHaveCount(0);
  await expect(page.getByText('Quote requested', { exact: true })).toHaveCount(0);
  expect(state.branches[0].quoteRequestedUtc).toBe(requested.quoteRequestedUtc);
  expect(state.writes).toContainEqual({ path: '/api/v1/admin/partner/branches/quote-branch/pricing', body: { priceNet: 90, vatRatePercent: 25.5, estimatedDurationMinutes: 120 } });
});

test('quote banner switches tabs on the current operations page; an open property dialog must be closed first', async ({ page }) => {
  await fixture(page);
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await page.getByRole('button', { name: 'Edit property', exact: true }).click();
  await page.getByLabel('Property name', { exact: true }).fill('Preserve my draft');
  // The dialog is modal and covers the banner, so close it (discarding the draft) before using the banner.
  await page.getByRole('dialog', { name: 'Edit property', exact: true }).getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('status', { name: 'Pending quote requests' }).getByRole('link', { name: 'Review quotes' }).click();
  await expect(page.getByRole('tab', { name: 'Quote requests', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('dialog', { name: 'Edit property', exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page).not.toHaveURL(/tab=quotes/);
  await page.getByRole('status', { name: 'Pending quote requests' }).getByRole('link', { name: 'Review quotes' }).click();
  await expect(page.getByRole('tab', { name: 'Quote requests', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('dialog', { name: 'Edit property', exact: true })).toHaveCount(0);
});

test('visible polling and focus discover new quotes, errors retain count and retry clears resolved quotes', async ({ page }) => {
  const state = await fixture(page, []);
  await page.clock.install();
  await page.goto('/admin/partner-requests/');
  await expect(page.getByRole('button', { name: 'Refresh partner operations' })).toBeEnabled();
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

test('hidden tabs stop quote polling and returning to visibility refreshes without resetting a draft', async ({ page }) => {
  const state = await fixture(page);
  await page.clock.install();
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page.getByText('Quote requested', { exact: true })).toHaveClass(/text-red-800/);
  await page.getByRole('button', { name: 'Edit property', exact: true }).click();
  await page.getByLabel('Property name', { exact: true }).fill('Unsaved name');
  await page.evaluate(() => Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' }));
  const reads = state.quoteReads;
  await page.clock.fastForward(60_000);
  expect(state.quoteReads).toBe(reads);
  state.branches = [];
  await page.evaluate(() => { Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' }); document.dispatchEvent(new Event('visibilitychange')); });
  await expect(page.getByRole('status', { name: 'Pending quote requests' })).toHaveCount(0);
  await expect(page.getByText('Quote requested', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Property name', { exact: true })).toHaveValue('Unsaved name');
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
  await page.goto('/admin/partner-requests/');
  await expect(page.getByLabel('3 pending partner actions', { exact: true })).toHaveText('3');
  for (const name of ['Requests', 'Quote requests', 'Changes']) await expect(page.getByRole('tab', { name, exact: true }).getByLabel('1 pending', { exact: true })).toHaveText('1');
  await expect(page.getByRole('article', { name: 'Cancelled apartment request', exact: true })).toHaveCount(0);
  await expect(page.getByRole('article', { name: 'Arranged apartment request', exact: true })).toBeVisible();
  const action = page.getByRole('button', { name: 'Arrange cleaner / time', exact: true }).first();
  await expect(action).toHaveClass(/mantine-Button-root/);
  await page.screenshot({ path: testInfo.outputPath('admin-partner-requests.png'), fullPage: true });
  state.work = [request, { ...request, id: 'second-pending' }];
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(page.getByRole('tab', { name: 'Requests', exact: true }).getByLabel('2 pending', { exact: true })).toHaveText('2');
  state.work = [{ ...request, status: 'Offered' }]; state.changes = []; state.branches = [];
  await page.getByRole('button', { name: 'Refresh partner operations', exact: true }).click();
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(page.getByLabel(/pending partner actions$/)).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Requests', exact: true }).getByLabel('1 pending', { exact: true })).toHaveCount(0);
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
