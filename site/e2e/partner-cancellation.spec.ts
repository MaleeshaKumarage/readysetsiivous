import { test, expect, type Locator, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleaningRequest, PartnerChange, PartnerIdentity } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', isActive: true, street: 'Lake road 1', city: 'Tampere', roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const pending: CleaningRequest = { id: 'pending-request', version: 3, companyId, branchId: branch.id, branchName: branch.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: 'Original instructions', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null };
const accepted: CleaningRequest = { ...pending, id: 'accepted-request', fromDate: '2026-12-05', toDate: '2026-12-05', status: 'Accepted', firstAcceptedAtUtc: '2026-11-01T10:00:00Z', linkedShiftId: 'linked-shift', acceptedCleanerName: 'Test Cleaner' };
const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' };
interface Write { path: string; method: string }
async function fixture(page: Page, role: 'partner' | 'admin' = 'partner') {
  await setupAdminMocks(page); await page.addInitScript(value => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = [value]; }, role);
  const writes: Write[] = [], reads: string[] = [];
  const change: PartnerChange = { id: 'change-one', companyId, branchId: branch.id, requestId: pending.id, kind: 'Property', message: 'Please include spare towels.', createdUtc: '2026-11-01T10:00:00Z', resolvedUtc: null, resolution: null };
  await page.route(/\/api\/v1\/(?:admin|partner)\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() !== 'GET') { writes.push({ path, method: req.method() }); return route.fulfill({ status: 409, headers, json: { detail: 'Unexpected mutation from a local cancellation.' } }); }
    reads.push(path);
    const suffix = path.split('/partner')[1];
    const json = path === '/api/v1/partner/me' ? identity : path.endsWith('/employees') ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }]
      : path.endsWith('/companies') ? { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 }
      : suffix === '/branches' || suffix === '/quotes' ? [branch]
      : suffix === '/requests' ? [pending, accepted]
      : suffix === '/change-requests' ? [change]
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ headers, json });
  });
  return { writes, reads };
}
async function cancel(editor: Locator, writes: Write[]) {
  await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(editor).toHaveCount(0); expect(writes).toEqual([]);
}

test('owner cancels valid property, cleaning and staff-change drafts without any writes', async ({ page }) => {
  const state = await fixture(page); await page.goto('/partner/');
  await page.getByRole('button', { name: 'Add property', exact: true }).click();
  const property = page.getByRole('region', { name: 'Add property', exact: true }); await property.getByLabel('Property name', { exact: true }).fill('Unsaved property'); await cancel(property, state.writes); await expect(page.getByRole('article', { name: 'Unsaved property', exact: true })).toHaveCount(0);
  const branchCard = page.getByRole('article', { name: branch.name, exact: true });
  await branchCard.getByRole('button', { name: 'Request cleaning', exact: true }).click();
  const cleaning = page.getByRole('region', { name: 'Cleaning request form', exact: true });
  await cleaning.getByLabel('From date', { exact: true }).fill('2026-12-10'); await cleaning.getByLabel('To date', { exact: true }).fill('2026-12-11'); await cleaning.getByLabel('Cleaning instructions').fill('Unsaved new cleaning'); await cancel(cleaning, state.writes);
  await branchCard.getByRole('button', { name: 'Request property changes', exact: true }).click();
  const staffChange = page.getByRole('region', { name: 'Request staff changes', exact: true }); await staffChange.getByLabel('Describe the requested change').fill('Unsaved property change'); await cancel(staffChange, state.writes);
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  const pendingCard = page.getByRole('article', { name: `${branch.name} ${pending.fromDate}`, exact: true }); await pendingCard.getByRole('button', { name: 'Edit request', exact: true }).click();
  await cleaning.getByLabel('From date', { exact: true }).fill('2026-12-10'); await cleaning.getByLabel('To date', { exact: true }).fill('2026-12-11'); await cleaning.getByLabel('Cleaning instructions').fill('Unsaved edited instructions'); await cancel(cleaning, state.writes);
  await expect(pendingCard.getByText('2026-12-01 – 2026-12-02', { exact: true })).toBeVisible(); await expect(pendingCard.getByText('Original instructions', { exact: true })).toBeVisible();
  await page.getByRole('article', { name: `${branch.name} ${accepted.fromDate}`, exact: true }).getByRole('button', { name: 'Request a change or cancellation', exact: true }).click(); await staffChange.getByLabel('Describe the requested change').fill('Unsaved accepted-request change'); await cancel(staffChange, state.writes);
  await page.getByRole('tab', { name: 'Change requests', exact: true }).click(); await expect(page.getByText('Unsaved accepted-request change', { exact: true })).toHaveCount(0); expect(state.writes).toEqual([]);
});

test('staff cancels valid offer, pricing, resolution, reschedule and property drafts without writes', async ({ page }) => {
  const state = await fixture(page, 'admin'); await page.goto('/admin/partner-requests/');
  const card = page.getByRole('article', { name: `${branch.name} request`, exact: true }).first();
  await card.getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
  const offer = page.getByRole('region', { name: 'Arrange cleaning', exact: true }); await offer.getByLabel('Cleaner', { exact: true }).selectOption('cleaner'); await offer.getByLabel('Planned start (tenant local time, optional)').fill('2026-12-01T10:00'); await cancel(offer, state.writes);
  await card.getByRole('button', { name: 'Edit / reschedule request', exact: true }).click();
  const edit = page.getByRole('region', { name: 'Edit cleaning request', exact: true }); await edit.getByLabel('From date', { exact: true }).fill('2026-12-10'); await edit.getByLabel('To date', { exact: true }).fill('2026-12-11'); await edit.getByLabel('Cleaning instructions').fill('Unsaved reschedule'); await cancel(edit, state.writes);
  await expect(card.getByText('2026-12-01 – 2026-12-02', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Quote requests', exact: true }).click(); await page.getByRole('button', { name: 'Set cleaning price', exact: true }).click();
  const price = page.getByRole('region', { name: 'Property pricing', exact: true }); await price.getByLabel('Fixed cleaning price excl. VAT (€)').fill('99'); await price.getByLabel('VAT (%)').fill('14'); await price.getByLabel('Estimated duration (minutes)').fill('90'); await cancel(price, state.writes);
  await page.getByRole('tab', { name: 'Changes', exact: true }).click(); await page.getByRole('button', { name: 'Resolve change request', exact: true }).click();
  const resolution = page.locator('form').filter({ has: page.getByRole('button', { name: 'Save resolution', exact: true }) }); await resolution.getByLabel('Resolution', { exact: true }).fill('Unsaved resolution'); await cancel(resolution, state.writes); await expect(page.getByText('Awaiting staff response', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Properties', exact: true }).click(); await page.getByRole('button', { name: 'Add property', exact: true }).click();
  const add = page.getByRole('heading', { name: 'Add property', exact: true }).locator('..'); await add.getByRole('combobox').selectOption(companyId); await add.getByLabel('Property name', { exact: true }).fill('Unsaved staff property'); await cancel(add, state.writes);
  await page.getByRole('button', { name: 'Edit property', exact: true }).click(); const property = page.getByRole('heading', { name: 'Edit property', exact: true }).locator('..'); await property.getByLabel('Property name', { exact: true }).fill('Unsaved edited property'); await property.getByLabel('street', { exact: true }).fill('Unsaved address'); await cancel(property, state.writes);
  await expect(page.getByRole('heading', { name: 'Lake apartment · Owner company', exact: true })).toBeVisible(); await expect(page.getByText('€70.00 excl. VAT · 120 minutes', { exact: true })).toBeVisible(); expect(state.writes).toEqual([]);
});

test('anonymous owner signs in, uses Account navigation, signs out and stays logged out on reload', async ({ page }) => {
  const state = await fixture(page);
  await page.addInitScript(() => {
    const count = Number(sessionStorage.getItem('__AUTH_DOCUMENT_COUNT__') ?? '0'); sessionStorage.setItem('__AUTH_DOCUMENT_COUNT__', String(count + 1));
    if (!sessionStorage.getItem('__PARTNER_AUTH_INITIALIZED__')) { sessionStorage.setItem('__PARTNER_AUTH_INITIALIZED__', 'true'); sessionStorage.setItem('__MOCK_LOGGED_OUT__', 'true'); (window as unknown as { __MOCK_AUTHED__: boolean }).__MOCK_AUTHED__ = false; }
  });
  await page.goto('/partner/'); await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible(); expect(state.reads).toEqual([]);
  const beforeLogin = await page.evaluate(() => Number(sessionStorage.getItem('__AUTH_DOCUMENT_COUNT__')));
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await expect(page.getByRole('article', { name: branch.name, exact: true })).toBeVisible();
  expect(await page.evaluate(() => ({ authenticated: (window as unknown as { __MOCK_AUTHED__: boolean }).__MOCK_AUTHED__, loggedOut: sessionStorage.getItem('__MOCK_LOGGED_OUT__'), documents: Number(sessionStorage.getItem('__AUTH_DOCUMENT_COUNT__')) }))).toEqual({ authenticated: true, loggedOut: null, documents: beforeLogin + 1 });
  await page.getByRole('link', { name: 'Account', exact: true }).click(); await expect(page).toHaveURL(/\/account\/$/); await expect(page.getByRole('heading', { name: 'Account ready', exact: true })).toBeVisible(); await page.getByRole('link', { name: 'Open partner portal', exact: true }).click(); await expect(page.getByRole('article', { name: branch.name, exact: true })).toBeVisible();
  const beforeLogout = await page.evaluate(() => Number(sessionStorage.getItem('__AUTH_DOCUMENT_COUNT__')));
  await page.getByRole('button', { name: 'Sign out', exact: true }).click(); await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  expect(await page.evaluate(() => ({ authenticated: (window as unknown as { __MOCK_AUTHED__: boolean }).__MOCK_AUTHED__, loggedOut: sessionStorage.getItem('__MOCK_LOGGED_OUT__'), documents: Number(sessionStorage.getItem('__AUTH_DOCUMENT_COUNT__')) }))).toEqual({ authenticated: false, loggedOut: 'true', documents: beforeLogout + 1 });
  const readsAfterLogout = state.reads.length; await page.reload(); await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible(); await expect(page.getByRole('article', { name: branch.name, exact: true })).toHaveCount(0); expect(state.reads).toHaveLength(readsAfterLogout); expect(state.writes).toEqual([]);
});
