import { test, expect, type Page, type Route } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET, POST, PUT, OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type, Authorization' };
const CUSTOMER = '11111111-1111-4111-8111-111111111111';
const EMPTY = '22222222-2222-4222-8222-222222222222';
const OTHER_TENANT = '33333333-3333-4333-8333-333333333333';
const UNKNOWN = '44444444-4444-4444-8444-444444444444';

const company = (id: string, name: string, extra: Record<string, unknown> = {}) => ({ id, name, businessId: '1234567-8', contactName: 'Anna', contactEmail: 'anna@example.test', contactPhone: '+358 40 000', isActive: true, isPartnerEnabled: true, ...extra });
const branches = [
  { id: 'branch-active', companyId: CUSTOMER, name: 'Harbour flat', city: 'Helsinki', isActive: true },
  { id: 'branch-inactive', companyId: CUSTOMER, name: 'Old office', city: 'Espoo', isActive: false },
];
const agreements = [
  { id: 'agr-1', companyId: CUSTOMER, title: 'Main cleaning agreement', code: 'AGR-1', status: 'Completed', isActive: true, signerCount: 2, signedCount: 2, createdUtc: '2026-10-01T00:00:00Z', companyName: 'Acme Oy' },
  { id: 'agr-2', companyId: OTHER_TENANT, title: 'Other customer agreement', code: 'AGR-2', status: 'Draft', isActive: true, signerCount: 1, signedCount: 0, createdUtc: '2026-10-02T00:00:00Z', companyName: 'Other Oy' },
];
const shifts = [
  { id: 'shift-active', companyId: CUSTOMER, branchId: 'branch-active', name: 'Weekly visit', schedule: {}, isActive: true, validFrom: '2026-01-01T00:00:00Z', validUntil: null },
  { id: 'shift-inactive', companyId: CUSTOMER, branchId: 'branch-inactive', name: 'Closed route', schedule: {}, isActive: false, validFrom: '2025-01-01T00:00:00Z', validUntil: '2025-12-31T00:00:00Z' },
];
const invoices = [
  { id: 'inv-1', companyId: CUSTOMER, invoiceNumber: 'INV-100', issueDate: '2026-10-05T00:00:00Z', dueDate: '2026-11-05T00:00:00Z', status: 'Issued', total: { net: 100, vat: 25.5, gross: 125.5 } },
  { id: 'inv-2', companyId: OTHER_TENANT, invoiceNumber: 'INV-200', issueDate: '2026-10-06T00:00:00Z', dueDate: '2026-11-06T00:00:00Z', status: 'Paid', total: { net: 10, vat: 2.55, gross: 12.55 } },
];

type Options = {
  customers?: Record<string, ReturnType<typeof company>>;
  branchesFor?: Record<string, typeof branches>;
  agreementsList?: typeof agreements;
  shiftsList?: typeof shifts;
  invoicesList?: typeof invoices;
  failDetail?: boolean;
  failAgreements?: { value: boolean };
  failShifts?: { value: boolean };
  failInvoices?: { value: boolean };
  agreementsGate?: Promise<void>;
  requests?: string[];
};

/** Serves the customer workspace contract by exact path. Unknown admin paths fall through to the other mocks. */
async function mockWorkspace(page: Page, options: Options = {}) {
  const customers = options.customers ?? { [CUSTOMER]: company(CUSTOMER, 'Acme Oy'), [EMPTY]: company(EMPTY, 'Empty Oy', { contactName: null, contactEmail: null, contactPhone: null, isPartnerEnabled: false }) };
  const requests = options.requests ?? [];
  const agreementsList = options.agreementsList ?? agreements;
  const shiftsList = options.shiftsList ?? shifts;
  const invoicesList = options.invoicesList ?? invoices;
  const branchesFor = options.branchesFor ?? { [CUSTOMER]: branches, [EMPTY]: [] };
  await page.route('**/api/v1/admin/**', async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, headers, json: body });
    requests.push(`${request.method()} ${path}${url.search}`);
    if (path === '/api/v1/admin/companies/summary') return json({ all: 2, active: 2, inactive: 0 });
    const detail = path.match(/^\/api\/v1\/admin\/companies\/([^/]+)$/);
    if (detail) {
      const id = detail[1];
      if (options.failDetail) return json({}, 500);
      const found = customers[id];
      if (!found) return json({}, 404);
      return json({ company: found, branches: branchesFor[id] ?? [] });
    }
    if (path === '/api/v1/admin/companies') {
      const items = Object.values(customers);
      return json({ items, total: items.length });
    }
    if (path === '/api/v1/admin/agreements') {
      if (request.method() === 'POST') return json({}, 500);
      if (options.agreementsGate) await options.agreementsGate;
      if (options.failAgreements?.value) return json({}, 500);
      const companyId = url.searchParams.get('companyId');
      return json(companyId ? agreementsList.filter(item => item.companyId === companyId) : agreementsList);
    }
    if (path === '/api/v1/admin/agreements/legacy-repairs') return json({ agreements: [], companies: [] });
    if (path === '/api/v1/admin/shifts') {
      if (options.failShifts?.value) return json({}, 500);
      const companyId = url.searchParams.get('companyId');
      return json(companyId ? shiftsList.filter(item => item.companyId === companyId) : shiftsList);
    }
    if (path === '/api/v1/admin/invoices') {
      if (options.failInvoices?.value) return json({}, 500);
      const companyId = url.searchParams.get('companyId');
      return json(companyId ? invoicesList.filter(item => item.companyId === companyId) : invoicesList);
    }
    return route.fallback();
  });
  return requests;
}

const ws = (id: string, section?: string, back?: string) => {
  const params = new URLSearchParams({ id });
  if (section) params.set('section', section);
  if (back) params.set('back', back);
  return `/admin/companies/customer/?${params.toString()}`;
};

test('Customer workspace opens with Overview and the implemented tabs only', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  await expect(page.getByRole('heading', { name: 'Acme Oy' })).toBeVisible();
  const tabs = page.getByRole('tablist', { name: 'Customer sections' });
  await expect(tabs.getByRole('tab')).toHaveText(['Overview', 'Properties', 'Agreements', 'Work', 'Invoices']);
  await expect(tabs.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Properties: 1 active · 1 inactive')).toBeVisible();
});

test('unimplemented tabs are not shown', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  const tabs = page.getByRole('tablist', { name: 'Customer sections' });
  await expect(tabs.getByRole('tab', { name: 'Expenses' })).toHaveCount(0);
  await expect(tabs.getByRole('tab', { name: 'Reports' })).toHaveCount(0);
  await expect(tabs.getByRole('tab', { name: 'Incidents' })).toHaveCount(0);
});

test('opening the workspace does not fetch secondary tabs', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const requests = await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  await expect(page.getByRole('heading', { name: 'Acme Oy' })).toBeVisible();
  expect(requests.filter(r => /agreements|shifts|invoices/.test(r))).toEqual([]);
});

test('direct link to a section loads only that tab', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const requests = await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'agreements'));
  await expect(page.getByRole('cell', { name: 'Main cleaning agreement', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Other customer agreement', exact: true })).toHaveCount(0);
  expect(requests.filter(r => /shifts|invoices/.test(r))).toEqual([]);
});

test('unknown section falls back to overview', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'expenses'));
  await expect(page.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
});

test('unknown customer id shows not found without tabs', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(UNKNOWN));
  await expect(page.getByRole('heading', { name: 'Customer not found' })).toBeVisible();
  await expect(page.getByRole('tablist', { name: 'Customer sections' })).toHaveCount(0);
});

test('malformed customer id shows not found and sends no detail request', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const requests = await mockWorkspace(page);
  await page.goto(ws('not-a-guid'));
  await expect(page.getByRole('heading', { name: 'Customer not found' })).toBeVisible();
  expect(requests.filter(r => r.includes('/api/v1/admin/companies/not-a-guid'))).toEqual([]);
});

test('another tenant customer id shows not found', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(OTHER_TENANT, 'agreements'));
  await expect(page.getByRole('heading', { name: 'Customer not found' })).toBeVisible();
  await expect(page.getByText('Main cleaning agreement', { exact: true })).toHaveCount(0);
});

test('failed customer load shows a retryable error', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { failDetail: true });
  await page.goto(ws(CUSTOMER));
  await expect(page.getByRole('alert').filter({ hasText: 'Customer could not be loaded.' })).toBeVisible();
});

test('a failing agreements tab does not block overview', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { failAgreements: { value: true } });
  await page.goto(ws(CUSTOMER));
  await expect(page.getByText('Properties: 1 active · 1 inactive')).toBeVisible();
  await page.getByRole('tab', { name: 'Agreements' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Agreements could not be loaded.' })).toBeVisible();
});

test('retrying a failed tab loads it once', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const failure = { value: true };
  const requests = await mockWorkspace(page, { failAgreements: failure });
  await page.goto(ws(CUSTOMER, 'agreements'));
  await expect(page.getByRole('alert').filter({ hasText: 'Agreements could not be loaded.' })).toBeVisible();
  failure.value = false;
  await page.getByRole('button', { name: 'Retry agreements' }).click();
  await expect(page.getByRole('cell', { name: 'Main cleaning agreement', exact: true })).toBeVisible();
  expect(requests.filter(r => r.startsWith('GET /api/v1/admin/agreements?')).length).toBe(2);
});

test('a retry after a failed customer load recovers', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { failDetail: true });
  await page.goto(ws(CUSTOMER));
  await expect(page.getByRole('alert').filter({ hasText: 'Customer could not be loaded.' })).toBeVisible();
  await page.unroute('**/api/v1/admin/**');
  await mockWorkspace(page);
  await page.getByRole('button', { name: 'Retry customer' }).click();
  await expect(page.getByRole('heading', { name: 'Acme Oy' })).toBeVisible();
});

test('late agreements response does not overwrite the Work tab', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await mockWorkspace(page, { agreementsGate: gate });
  await page.goto(ws(CUSTOMER, 'agreements'));
  await page.getByRole('tab', { name: 'Work' }).click();
  await expect(page.getByRole('cell', { name: 'Weekly visit', exact: true })).toBeVisible();
  release();
  await expect(page.getByRole('cell', { name: 'Weekly visit', exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Work' })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('tab', { name: 'Agreements' }).click();
  await expect(page.getByRole('cell', { name: 'Main cleaning agreement', exact: true })).toBeVisible();
});

test('Back returns to the filtered companies list page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto('/admin/companies/?status=active&page=1&size=25');
  await expect(page.getByRole('cell', { name: 'Acme Oy', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Open Acme Oy' }).click();
  await expect(page.getByRole('heading', { name: 'Acme Oy' })).toBeVisible();
  await page.goBack();
  await expect(page).toHaveURL(/\/admin\/companies\/\?status=active/);
  await expect(page.getByRole('cell', { name: 'Acme Oy', exact: true })).toBeVisible();
});

test('opening a customer keeps the list filter in the back link', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto('/admin/companies/?status=active&page=1&size=50&q=Acme');
  await page.getByRole('link', { name: 'Open Acme Oy' }).click();
  await expect(page.getByRole('link', { name: 'Back to customers' })).toHaveAttribute('href', '/admin/companies/?page=1&size=50&q=Acme');
});

test('back link restores the list state carried in the URL', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'overview', 'status=inactive&page=2&size=50&q=acme&evil=1'));
  await expect(page.getByRole('link', { name: 'Back to customers' })).toHaveAttribute('href', '/admin/companies/?status=inactive&page=2&size=50&q=acme');
});

test('Back steps back through workspace tabs', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  await page.getByRole('tab', { name: 'Properties' }).click();
  await expect(page).toHaveURL(/section=properties/);
  await page.getByRole('tab', { name: 'Work' }).click();
  await expect(page).toHaveURL(/section=work/);
  await page.goBack();
  await expect(page.getByRole('tab', { name: 'Properties' })).toHaveAttribute('aria-selected', 'true');
});

test('empty state: customer has no agreements', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { agreementsList: [] });
  await page.goto(ws(CUSTOMER, 'agreements'));
  await expect(page.getByText('No agreements for this customer yet.', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'New agreement' })).toHaveAttribute('href', `/admin/agreements/?company=${CUSTOMER}`);
});

test('properties tab shows an empty state when the customer has no branches', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(EMPTY, 'properties'));
  await expect(page.getByText('No properties yet.', { exact: true })).toBeVisible();
});

test('work tab shows inactive shifts only on the Inactive tab', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { shiftsList: [shifts[1]] });
  await page.goto(ws(CUSTOMER, 'work'));
  await expect(page.getByText('No active shifts.', { exact: true })).toBeVisible();
  await page.getByRole('tablist', { name: 'Shift activity' }).getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('cell', { name: 'Closed route', exact: true })).toBeVisible();
});

test('invoices tab lists only this customer’s invoices', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const requests = await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'invoices'));
  await expect(page.getByRole('cell', { name: 'INV-100', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'INV-200', exact: true })).toHaveCount(0);
  expect(requests.some(r => r.startsWith('GET /api/v1/admin/invoices?companyId=' + CUSTOMER))).toBe(true);
});

test('edit in companies list link opens the pinned customer', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  await page.getByRole('link', { name: 'Edit in companies list' }).click();
  await expect(page).toHaveURL(url => url.pathname === '/admin/companies/' && url.searchParams.get('company') === CUSTOMER);
  await expect(page.getByRole('group', { name: 'Branch Harbour flat' })).toBeVisible();
});

test('manage properties link opens the pinned customer', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'properties'));
  await page.getByRole('link', { name: 'Manage properties' }).click();
  await expect(page).toHaveURL(url => url.pathname === '/admin/companies/' && url.searchParams.get('company') === CUSTOMER);
  await expect(page.getByRole('group', { name: 'Branch Harbour flat' })).toBeVisible();
});

test('populated properties list shows branches and inactive badges', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'properties'));
  await expect(page.getByText('Harbour flat · Helsinki', { exact: true })).toBeVisible();
  await expect(page.getByText('Old office · Espoo', { exact: true })).toBeVisible();
  await expect(page.getByRole('tabpanel', { name: 'Properties' }).getByText('Inactive', { exact: true })).toBeVisible();
  await expect(page.getByText('No properties yet.', { exact: true })).toHaveCount(0);
});

test('work tab shows an error and retry reloads shifts once', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const failure = { value: true };
  const requests = await mockWorkspace(page, { failShifts: failure });
  await page.goto(ws(CUSTOMER, 'work'));
  await expect(page.getByRole('alert').filter({ hasText: 'Shifts could not be loaded.' })).toBeVisible();
  failure.value = false;
  await page.getByRole('button', { name: 'Retry shifts' }).click();
  await expect(page.getByRole('cell', { name: 'Weekly visit', exact: true })).toBeVisible();
  expect(requests.filter(r => r.startsWith('GET /api/v1/admin/shifts?')).length).toBe(2);
});

test('work tab shows the no-shifts empty state for a customer with none', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { shiftsList: [] });
  await page.goto(ws(CUSTOMER, 'work'));
  await expect(page.getByText('No shifts for this customer yet.', { exact: true })).toBeVisible();
  await expect(page.getByText('No active shifts.', { exact: true })).toHaveCount(0);
});

test('open invoices button navigates to the invoices page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'invoices'));
  await page.getByRole('link', { name: 'Open invoices' }).click();
  await expect(page).toHaveURL(/\/admin\/invoices\/$/);
});

test('invoices error shows retry while overview stays usable', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const failure = { value: true };
  const requests = await mockWorkspace(page, { failInvoices: failure });
  await page.goto(ws(CUSTOMER, 'invoices'));
  await expect(page.getByRole('alert').filter({ hasText: 'Invoices could not be loaded.' })).toBeVisible();
  await page.getByRole('tab', { name: 'Overview' }).click();
  await expect(page.getByText('Properties: 1 active · 1 inactive')).toBeVisible();
  await page.getByRole('tab', { name: 'Invoices' }).click();
  failure.value = false;
  await page.getByRole('button', { name: 'Retry invoices' }).click();
  await expect(page.getByRole('cell', { name: 'INV-100', exact: true })).toBeVisible();
  expect(requests.filter(r => r.startsWith('GET /api/v1/admin/invoices?companyId=')).length).toBe(2);
});

test('invoices tab shows an empty state', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(EMPTY, 'invoices'));
  await expect(page.getByText('No invoices for this customer yet.', { exact: true })).toBeVisible();
});

test('new agreement link preselects this customer', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'agreements'));
  await page.getByRole('link', { name: 'New agreement' }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/agreements/\\?company=${CUSTOMER}`));
  const dialog = page.getByRole('dialog', { name: 'New agreement', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveValue('Acme Oy');
});

test('add shift link preselects this customer', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER, 'work'));
  await page.getByRole('link', { name: 'Add shift' }).click();
  await expect(page).toHaveURL(new RegExp(`/admin/shifts/\\?company=${CUSTOMER}`));
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveValue('Acme Oy');
});

test('agreement summaries show the customer name', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page, { agreementsList: [agreements[0]] });
  await page.goto('/admin/agreements/');
  await page.getByRole('dialog', { name: 'New agreement', exact: true }).waitFor({ state: 'detached' }).catch(() => undefined);
  const row = page.getByRole('row').filter({ hasText: 'Main cleaning agreement' });
  await expect(row.getByRole('cell', { name: 'Acme Oy', exact: true })).toBeVisible();
});

test('non-admin session cannot read the customer workspace', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: null } }));
  const requests = await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Acme Oy' })).toHaveCount(0);
  expect(requests.filter(r => r.includes('/companies/'))).toEqual([]);
});

test('keyboard can switch workspace sections', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.goto(ws(CUSTOMER));
  await page.getByRole('tab', { name: 'Overview' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Properties' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('No properties yet.', { exact: true })).toHaveCount(0);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/section=properties/);
});

test('customer workspace has no horizontal overflow at phone width', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'mobile-touch', 'phone width is checked on the mobile project');
  await setupAdminMocks(page, { authenticated: true });
  await mockWorkspace(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(ws(CUSTOMER, 'work'));
  await expect(page.getByRole('heading', { name: 'Acme Oy' })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  await expect(page.getByRole('tab', { name: 'Invoices' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Back to customers' })).toBeVisible();
});
