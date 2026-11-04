import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Owner withdraw/edit/declined-quote paths and staff rejection, plus banner counts limited to partner-enabled companies.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const ownerCompanyId = 'lifecycle-company';
const otherCompanyId = 'lifecycle-disabled';
const priced = { id: 'lake-branch', companyId: ownerCompanyId, name: 'Lake apartment', street: 'Lake road 1', postalCode: '33100', city: 'Tampere', country: 'FI', contactPhone: '+358 40 000', contactEmail: 'owner@example.test', roomCount: 2, bedCount: 3, isActive: true, pricingReady: true, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingVersion: 1, quoteRequestedUtc: null, quoteRejectedUtc: null, quoteRejectionReason: null };
const pendingQuote = { ...priced, id: 'cabin-branch', name: 'Cabin', city: 'Oulu', pricingReady: false, priceNet: null, vatRatePercent: null, estimatedDurationMinutes: null, quoteRequestedUtc: '2026-11-01T10:00:00Z' };
const declinedQuote = { ...pendingQuote, id: 'remote-branch', name: 'Remote cabin', quoteRequestedUtc: null, quoteRejectedUtc: '2026-11-02T09:00:00Z', quoteRejectionReason: 'Outside the service area' };
const enabledStaffQuote = { ...pendingQuote, id: 'staff-quote', name: 'Lake apartment', companyId: ownerCompanyId };
const disabledStaffQuote = { ...pendingQuote, id: 'disabled-quote', name: 'Disabled flat', companyId: otherCompanyId };
const ownerCompany = { id: ownerCompanyId, name: 'Owner company', businessId: '1234567-8', isActive: true, isPartnerEnabled: true };
const otherCompany = { id: otherCompanyId, name: 'Other company', businessId: '7654321-0', isActive: true, isPartnerEnabled: false };

interface Write { path: string; method: string; body: Record<string, unknown> | null }

async function owner(page: Page, branches: Record<string, unknown>[]) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; });
  const identity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: ownerCompanyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' };
  const state = { branches, writes: [] as Write[] };
  await page.route(/\/api\/v1\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/me' ? identity : path === '/branches' ? state.branches : [] });
    const body = req.postData() ? req.postDataJSON() as Record<string, unknown> : null;
    state.writes.push({ path, method: req.method(), body });
    const id = path.split('/')[2];
    if (req.method() === 'PUT' && path === `/branches/${id}`) {
      state.branches = state.branches.map(branch => branch.id === id ? { ...branch, ...body } : branch);
      return route.fulfill({ headers, json: state.branches.find(branch => branch.id === id) });
    }
    if (path === `/branches/${id}/quote-request/withdraw`) {
      state.branches = state.branches.map(branch => branch.id === id ? { ...branch, quoteRequestedUtc: null } : branch);
      return route.fulfill({ headers, json: state.branches.find(branch => branch.id === id) });
    }
    if (path === `/branches/${id}/quote-request`) {
      state.branches = state.branches.map(branch => branch.id === id ? { ...branch, quoteRequestedUtc: '2026-11-03T08:00:00Z', quoteRejectedUtc: null, quoteRejectionReason: null } : branch);
      return route.fulfill({ headers, json: state.branches.find(branch => branch.id === id) });
    }
    return route.fulfill({ status: 404, headers });
  });
  return state;
}

test('owner edits property details from the card; the price and active state are left alone', async ({ page }) => {
  const state = await owner(page, [priced]);
  await page.goto('/partner/');
  const card = page.getByRole('article', { name: 'Lake apartment', exact: true });
  await card.getByRole('button', { name: 'Edit property', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit property', exact: true });
  await expect(dialog.getByLabel('Property name', { exact: true })).toHaveValue('Lake apartment');
  await expect(dialog.getByLabel('City', { exact: true })).toHaveValue('Tampere');
  await dialog.getByLabel('Property name', { exact: true }).fill('Lake cottage');
  await dialog.getByLabel('City', { exact: true }).fill('Espoo');
  await dialog.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.getByText('Property details saved.', { exact: true })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Lake cottage', exact: true })).toBeVisible();
  const write = state.writes.find(item => item.method === 'PUT');
  expect(write?.path).toBe('/branches/lake-branch');
  expect(write?.body).toMatchObject({ name: 'Lake cottage', city: 'Espoo', roomCount: 2 });
  expect(write?.body).not.toHaveProperty('priceNet');
  expect(write?.body).not.toHaveProperty('isActive');
  expect(write?.body).not.toHaveProperty('companyId');
});

test('an inactive property cannot be edited from the owner portal', async ({ page }) => {
  await owner(page, [{ ...priced, id: 'old-branch', name: 'Old flat', isActive: false }]);
  await page.goto('/partner/');
  await expect(page.getByRole('article', { name: 'Old flat', exact: true }).getByRole('button', { name: 'Edit property', exact: true })).toBeDisabled();
});

test('owner withdraws a pending quote request and the quote badge goes away', async ({ page }) => {
  const state = await owner(page, [pendingQuote]);
  await page.goto('/partner/');
  const card = page.getByRole('article', { name: 'Cabin', exact: true });
  await expect(card.getByText('Quote requested — awaiting staff pricing', { exact: true })).toBeVisible();
  await card.getByRole('button', { name: 'Withdraw quote request', exact: true }).click();
  await expect(page.getByText('Quote request withdrawn.', { exact: true })).toBeVisible();
  await expect(card.getByRole('button', { name: 'Withdraw quote request', exact: true })).toHaveCount(0);
  await expect(card.getByRole('button', { name: 'Request quote', exact: true })).toBeEnabled();
  expect(state.writes).toContainEqual(expect.objectContaining({ path: '/branches/cabin-branch/quote-request/withdraw', method: 'POST' }));
});

test('owner sees the staff reason for a declined quote and can request a quote again', async ({ page }) => {
  const state = await owner(page, [declinedQuote]);
  await page.goto('/partner/');
  const card = page.getByRole('article', { name: 'Remote cabin', exact: true });
  await expect(card.getByRole('status')).toHaveText('Staff could not quote this property: Outside the service area');
  await card.getByRole('button', { name: 'Request quote', exact: true }).click();
  await expect(page.getByText('Quote requested.', { exact: true })).toBeVisible();
  await expect(card.getByRole('status')).toHaveCount(0);
  await expect(card.getByRole('button', { name: 'Withdraw quote request', exact: true })).toBeVisible();
  expect(state.writes.some(item => item.path === '/branches/remote-branch/quote-request' && item.method === 'POST')).toBe(true);
});

test('Finnish owner labels cover property edit, quote withdrawal and a declined quote', async ({ page }) => {
  await owner(page, [pendingQuote, declinedQuote]);
  await page.goto('/partner/');
  await page.getByRole('button', { name: 'Suomi', exact: true }).click();
  await expect(page.getByRole('main')).toHaveAttribute('lang', 'fi');
  const cabin = page.getByRole('article', { name: 'Cabin', exact: true });
  await expect(cabin.getByRole('button', { name: 'Peruuta tarjouspyyntö', exact: true })).toBeVisible();
  await expect(cabin.getByRole('button', { name: 'Muokkaa kohdetta', exact: true })).toBeEnabled();
  const remote = page.getByRole('article', { name: 'Remote cabin', exact: true });
  await expect(remote.getByRole('status')).toHaveText('Henkilökunta ei voinut hinnoitella tätä kohdetta: Outside the service area');
  await cabin.getByRole('button', { name: 'Muokkaa kohdetta', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Muokkaa kohdetta', exact: true });
  await expect(dialog.getByRole('button', { name: 'Tallenna kohde', exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Peruuta', exact: true }).click();
  await cabin.getByRole('button', { name: 'Peruuta tarjouspyyntö', exact: true }).click();
  await expect(page.getByText('Tarjouspyyntö peruutettu.', { exact: true })).toBeVisible();
});

// ----- staff side -----

async function staff(page: Page, quotes: Record<string, unknown>[], work: Record<string, unknown>[] = [], changes: Record<string, unknown>[] = []) {
  await setupAdminMocks(page);
  const state = { quotes, work, changes, writes: [] as Write[] };
  await page.route(/\/api\/v1\/admin\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'POST' && path.endsWith('/quote-rejection')) {
      const body = req.postDataJSON() as Record<string, unknown>;
      const id = path.split('/')[path.split('/').length - 2];
      state.writes.push({ path, method: 'POST', body });
      state.quotes = state.quotes.filter(branch => branch.id !== id);
      return route.fulfill({ headers, json: { ...enabledStaffQuote, id, quoteRequestedUtc: null, quoteRejectedUtc: '2026-11-03T09:00:00Z', quoteRejectionReason: body.reason } });
    }
    const json = path.endsWith('/partner/quotes') ? state.quotes
      : path.endsWith('/partner/requests') ? state.work
      : path.endsWith('/partner/change-requests') ? state.changes
      : path.endsWith('/partner/branches') ? state.quotes
      : path.endsWith('/companies') ? { items: [ownerCompany, otherCompany], total: 2 }
      : [];
    return route.fulfill({ headers, json });
  });
  return state;
}

test('staff reject needs a reason, leaves the queue, and the banner counts only partner-enabled companies', async ({ page }) => {
  const state = await staff(page, [enabledStaffQuote, disabledStaffQuote]);
  await page.goto('/admin/companies/');
  // The banner and badge count only partner-enabled companies, the same rule the Quote requests tab uses.
  await expect(page.getByRole('status', { name: 'Pending quote requests' })).toContainText('1 branch awaiting a quote');
  await expect(page.getByLabel('1 pending partner actions')).toHaveCount(1);
  await page.goto('/admin/partner-requests/?tab=quotes');
  await expect(page.getByRole('heading', { name: 'Lake apartment', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Disabled flat', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Reject quote', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Reject quote · Lake apartment', exact: true });
  const confirm = dialog.getByRole('button', { name: 'Confirm rejection', exact: true });
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel('Reason for rejecting the quote', { exact: true }).fill('   ');
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel('Reason for rejecting the quote', { exact: true }).fill('Outside the service area');
  await confirm.click();
  await expect(page.getByText('Quote rejected.', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Lake apartment', exact: true })).toHaveCount(0);
  expect(state.writes).toContainEqual(expect.objectContaining({ path: '/api/v1/admin/partner/branches/staff-quote/quote-rejection', body: { reason: 'Outside the service area' } }));
});

test('staff can keep a quote after opening the reject dialog without writing anything', async ({ page }) => {
  const state = await staff(page, [enabledStaffQuote]);
  await page.goto('/admin/partner-requests/?tab=quotes');
  await page.getByRole('button', { name: 'Reject quote', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Reject quote · Lake apartment', exact: true });
  await dialog.getByRole('button', { name: 'Keep quote', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Reject quote · Lake apartment', exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Lake apartment', exact: true })).toBeVisible();
  expect(state.writes).toHaveLength(0);
});

test('changes from companies without partner access stay out of the Changes list and the pending count', async ({ page }) => {
  await staff(page, [enabledStaffQuote], [], [
    { id: 'change-on', companyId: ownerCompanyId, branchId: 'lake-branch', requestId: null, kind: 'Property', message: 'Enable the sauna', createdUtc: '2026-11-01T10:00:00Z', resolvedUtc: null, resolution: null },
    { id: 'change-off', companyId: otherCompanyId, branchId: 'other-branch', requestId: null, kind: 'Property', message: 'Not partner company', createdUtc: '2026-11-01T10:00:00Z', resolvedUtc: null, resolution: null },
  ]);
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: /^Changes/ }).click();
  await expect(page.getByText('Enable the sauna', { exact: true })).toBeVisible();
  await expect(page.getByText('Not partner company', { exact: true })).toHaveCount(0);
  // One quote and one enabled change; the disabled company's change is not counted.
  await expect(page.getByLabel('2 pending partner actions')).toHaveCount(1);
});

test('owner edit and quote withdrawal work from the keyboard, and Escape returns focus to Edit property', async ({ page }) => {
  const state = await owner(page, [pendingQuote]);
  await page.goto('/partner/');
  const card = page.getByRole('article', { name: 'Cabin', exact: true });
  const edit = card.getByRole('button', { name: 'Edit property', exact: true });
  await edit.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Edit property', exact: true });
  await expect(dialog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(edit).toBeFocused();
  const withdraw = card.getByRole('button', { name: 'Withdraw quote request', exact: true });
  await withdraw.focus(); await page.keyboard.press('Enter');
  await expect(page.getByText('Quote request withdrawn.', { exact: true })).toBeVisible();
  expect(state.writes.some(item => item.path === '/branches/cabin-branch/quote-request/withdraw')).toBe(true);
});

test('staff reject from the keyboard: reason, Tab to confirm, Escape keeps the quote', async ({ page }) => {
  const state = await staff(page, [enabledStaffQuote]);
  await page.goto('/admin/partner-requests/?tab=quotes');
  const reject = page.getByRole('button', { name: 'Reject quote', exact: true });
  await reject.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Reject quote · Lake apartment', exact: true });
  await expect(dialog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  expect(state.writes).toHaveLength(0);
  await reject.focus(); await page.keyboard.press('Enter');
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Reason for rejecting the quote', { exact: true }).focus();
  await page.keyboard.type('Outside the service area');
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Confirm rejection', exact: true })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Quote rejected.', { exact: true })).toBeVisible();
  expect(state.writes).toContainEqual(expect.objectContaining({ body: { reason: 'Outside the service area' } }));
});

test('the pending partner actions badge counts only partner-enabled Requested requests', async ({ page }) => {
  const requested = (id: string, companyId: string) => ({ id, companyId, branchId: 'b-' + id, branchName: 'Flat ' + id, fromDate: '2026-12-01', toDate: '2026-12-01', status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, offeredEmployeeId: null, version: 1, notes: null, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120 });
  // One enabled quote and one enabled Requested request count; the disabled company's quote and request do not.
  await staff(page, [enabledStaffQuote, disabledStaffQuote], [requested('on-request', ownerCompanyId), requested('off-request', otherCompanyId)]);
  await page.goto('/admin/companies/');
  await expect(page.getByLabel('2 pending partner actions')).toHaveCount(1);
  await expect(page.getByLabel('3 pending partner actions')).toHaveCount(0);
});

