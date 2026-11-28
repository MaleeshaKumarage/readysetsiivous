import { test, expect, type Page } from '@playwright/test';
import { chooseEmployeeSection } from './employee-section';
import { setupAdminMocks } from './admin-helpers';
import { viaManagerPage } from './partner-operations-page';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const branch = { id: 'branch-one', companyId: 'company-one', name: 'Lake apartment', street: 'Lake road 1', city: 'Tampere', isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const request = { id: 'request-one', version: 1, companyId: 'company-one', branchId: branch.id, branchName: branch.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: '', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null };
async function partnerSetup(page: Page, options: { accepted?: boolean; disabled?: boolean; role?: string } = {}) {
  await setupAdminMocks(page);
  await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = [role]; }, options.role ?? 'partner');
  const writes: { path: string; body: any }[] = []; let requests: any[] = options.accepted ? [{ ...request, status: 'Accepted', firstAcceptedAtUtc: '2026-11-01T08:00:00Z', acceptedCleanerName: 'T.' }] : [];
  let companyReads = 0;
  await page.route(/\/api\/v1\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path === '/me') return route.fulfill({ headers, json: { realm: 'readysetsiivous', person: { id: 'owner', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: 'company-one', name: 'Owner company', isActive: true, isPartnerEnabled: !options.disabled }, timeZoneId: 'Europe/Helsinki' } });
    if (req.method() === 'GET') { companyReads++; return route.fulfill({ headers, json: path === '/branches' ? [branch, { ...branch, id: 'unpriced', name: 'New apartment', pricingReady: false, priceNet: null }] : path === '/requests' ? requests : path === '/change-requests' ? [{ id: 'change-one', branchId: branch.id, kind: 'Property', message: 'Add fresh towels', resolvedUtc: '2026-11-01T10:00:00Z', resolution: 'Fresh towels included.' }] : [{ id: 'legacy', branchId: branch.id, branchName: branch.name, name: 'Existing cleaning', isActive: true, schedule: {}, validFrom: '2026-01-01', validUntil: null }] }); }
    const body = req.postData() ? req.postDataJSON() : undefined; writes.push({ path, body });
    if (path === '/requests') { requests = [{ ...request, ...body }]; return route.fulfill({ headers, json: requests[0] }); }
    return route.fulfill({ headers, json: {} });
  });
  return { writes, get companyReads() { return companyReads; } };
}

test('owner sees scoped properties, requests one visit and existing read-only shifts', async ({ page }) => {
  const state = await partnerSetup(page); await page.goto('/partner/');
  await expect(page.getByText('Owner company')).toBeVisible();
  await expect(page.getByRole('article', { name: 'New apartment', exact: true }).getByRole('button', { name: 'Request cleaning' })).toBeDisabled();
  await page.getByRole('article', { name: 'Lake apartment', exact: true }).getByRole('button', { name: 'Request cleaning' }).click();
  await page.getByLabel('From date', { exact: true }).fill('2026-12-01'); await page.getByLabel('To date', { exact: true }).fill('2026-12-02');
  await page.getByLabel('Cleaning instructions').fill('Clean after checkout.'); await page.getByRole('button', { name: 'Save cleaning request' }).click();
  await expect(page.getByText('Cleaning request saved. Staff will arrange an employee.')).toBeVisible();
  expect(state.writes[0].body).toMatchObject({ branchId: branch.id, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null });
  await page.getByRole('tab', { name: 'Change requests', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Staff response' })).toBeVisible(); await expect(page.getByText('Fresh towels included.')).toBeVisible();
  expect(state.writes[0].body.clientRequestId).toMatch(/^[a-f0-9-]{36}$/); expect(state.writes[0].body).not.toHaveProperty('companyId'); expect(state.writes[0].body).not.toHaveProperty('priceNet');
  await page.getByRole('tab', { name: 'Company shifts', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Existing cleaning' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Start shift|Assign employee|Edit shift/ })).toHaveCount(0);
});

test('accepted request is locked and owner requests staff changes', async ({ page }) => {
  const state = await partnerSetup(page, { accepted: true }); await page.goto('/partner/'); await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await expect(page.getByText('Employee:', { exact: false })).toHaveCount(0); await expect(page.getByText('Test Employee')).toHaveCount(0); await expect(page.getByRole('button', { name: 'Edit request', exact: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Cancel request', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Request a change or cancellation' }).click(); await page.getByLabel('Describe the requested change').fill('Please arrange a later visit.'); await page.getByRole('button', { name: 'Send change request' }).click();
  await expect(page.getByText('Change request sent to staff.')).toBeVisible(); expect(state.writes[0].path).toBe('/requests/request-one/change-requests');
});

test('disabled company prevents mounting company data', async ({ page }) => {
  const state = await partnerSetup(page, { disabled: true }); await page.goto('/partner/'); await expect(page.getByText('Access denied.', { exact: false })).toBeVisible(); expect(state.companyReads).toBe(0);
});

test('staff role cannot enter partner portal', async ({ page }) => {
  const state = await partnerSetup(page, { role: 'manager' }); await page.goto('/partner/'); await expect(page.getByText('Access denied.', { exact: false })).toBeVisible(); expect(state.companyReads).toBe(0);
});

test('employee may accept an unscheduled offer without a time, which becomes flexible within the dates; acceptance reloads work', async ({ page }, testInfo) => {
  await setupAdminMocks(page); await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  let offer: any = { id: 'offer-one', requestId: request.id, version: 4, branchName: branch.name, street: branch.street, city: branch.city, fromDate: request.fromDate, toDate: request.toDate, plannedStartUtc: null, exactStartLocalTime: null, estimatedDurationMinutes: 120, notes: 'After checkout', timeZoneId: 'Europe/Helsinki' };
  const decisions: any[] = []; let loads = 0;
  await page.route(/\/api\/v1\/me\/shifts\/calendar(?:\?.*)?$/, route => { if (route.request().method() === 'OPTIONS') return route.fulfill({ headers }); loads++; return route.fulfill({ headers, json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-01', toLocalDate: '2026-11-01' } }); });
  await page.route(/\/api\/v1\/me\/partner-offers(?:\/.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'GET') return route.fulfill({ headers, json: offer ? [offer] : [] });
    decisions.push(route.request().postDataJSON()); offer = null; return route.fulfill({ headers, json: { ...request, status: 'Accepted' } });
  });
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Offers');
  const offerCard = page.getByRole('article', { name: 'Lake apartment offer', exact: true }).locator('.rounded-2xl');
  await expect(offerCard).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await page.evaluate(() => document.documentElement.classList.add('dark'));
  await expect(offerCard).toHaveCSS('background-color', 'rgb(31, 41, 55)');
  await expect(page.getByLabel('Planned start for Lake apartment', { exact: true })).toHaveCSS('color', 'rgb(243, 244, 246)');
  await page.screenshot({ path: testInfo.outputPath('cleaner-acceptance-dark.png'), fullPage: true });
  await page.getByRole('button', { name: 'Accept cleaning' }).click();
  await expect(page.getByText('Cleaning accepted for Lake apartment. Choose your start any time from 2026-12-01 to 2026-12-02.', { exact: true })).toBeVisible();
  await expect(page.getByText('No pending cleaning offers.')).toBeVisible(); expect(decisions).toEqual([{ version: 4, plannedStartLocal: null }]); expect(loads).toBeGreaterThan(1);
});

test('administrator prices a quote and offers work only to staff', async ({ page }) => {
  await setupAdminMocks(page); const writes: any[] = [];
  let currentRequest = { ...request, offeredEmployeeId: null as string | null };
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [{ id: 'employee', firstName: 'Test', lastName: 'Employee', role: 'employee', isActive: true }, { id: 'owner', firstName: 'Property', lastName: 'Owner', role: 'partner', isActive: true }] }));
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: branch.companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1]; if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/requests' ? [currentRequest] : path === '/change-requests' ? [] : path === '/quotes' ? [{ ...branch, pricingReady: false, quoteRequestedUtc: '2026-11-01T10:00:00Z' }] : path === '/key-requests' ? { feeNet: 0, vatRatePercent: 25.5, items: [] } : [branch] });
    const body = req.postDataJSON(); writes.push({ path, body });
    if (path.endsWith('/offer')) currentRequest = { ...currentRequest, version: currentRequest.version + 1, status: 'Offered', offeredEmployeeId: body.employeeId };
    return route.fulfill({ headers, json: path.includes('pricing') ? branch : currentRequest });
  });
  await page.goto('/admin/');
  // This navigation test uses the real mobile drawer; the shared helper normally
  // hides that drawer so unrelated form tests cannot be obscured by it.
  await page.evaluate(() => document.getElementById('mock-test-style')?.remove());
  // Partner work is handled in the Work queue; there is no Partner operations entry any more.
  const queueNav = page.locator('a[aria-label="Work queue"]').first(); const navToggle = page.getByRole('button', { name: 'Toggle navigation', exact: true });
  await expect(queueNav.or(navToggle).first()).toBeVisible(); if (!(await queueNav.isVisible())) await navToggle.click();
  await expect(page.locator('a[aria-label="Partner operations"]')).toHaveCount(0);
  await queueNav.click();
  await page.getByLabel('Type').selectOption('quote');
  await page.getByRole('button', { name: /^Set price/ }).click();
  await page.getByLabel('Price excl. VAT (€)').fill('85'); await page.getByLabel('Estimated duration (minutes)').fill('90'); await page.getByRole('button', { name: 'Save price' }).click();
  await expect(page.locator('.mantine-Notification-root').filter({ hasText: 'is priced at €85.00 excl. VAT' })).toBeVisible();
  expect(writes[0].body).toEqual({ priceNet: 85, vatRatePercent: 25.5, estimatedDurationMinutes: 90 });
  await page.getByLabel('Type').selectOption('cleaning');
  await page.getByRole('button', { name: /^Offer cleaning/ }).click();
  // Only office staff can be offered work: the property owner's account is not in the list.
  const employee = page.getByRole('dialog').getByRole('combobox', { name: 'Employee' });
  await expect(employee.locator('option', { hasText: 'Property Owner' })).toHaveCount(0);
  await employee.selectOption('employee');
  await page.getByRole('dialog').getByRole('button', { name: 'Send offer to Test Employee' }).click();
  await expect(page.locator('.mantine-Notification-root').filter({ hasText: 'sent to Test Employee' })).toBeVisible();
  expect(writes[1]).toEqual({ path: `/requests/${request.id}/offer`, body: { version: 1, employeeId: 'employee', plannedStartLocal: `${request.fromDate}T09:00` } });
  // Offered again once the queue has refreshed: the employee it waits for is chosen first, and a tapped time is sent as the planned start.
  await expect(page.getByRole('listitem').filter({ hasText: 'Awaiting acceptance · offered to Test Employee' })).toBeVisible();
  await page.getByRole('button', { name: /^Offer cleaning/ }).click();
  await expect(page.getByRole('dialog').getByRole('combobox', { name: 'Employee' })).toHaveValue('employee');
  const bar = page.getByRole('dialog').getByTestId('day-bar'); const box = (await bar.boundingBox())!;
  await bar.click({ position: { x: box.width * (11.25 / 24), y: 4 } });
  await page.getByRole('dialog').getByRole('button', { name: /^Send offer to Test Employee/ }).click();
  await expect.poll(() => writes.length).toBe(3);
  expect(writes[2]).toEqual({ path: `/requests/${request.id}/offer`, body: { version: 2, employeeId: 'employee', plannedStartLocal: `${request.fromDate}T10:15` } });
});

test('switching inline staff editors resets fields to the newly selected record', async ({ page }) => {
  await setupAdminMocks(page); const writes: { path: string; body: any }[] = [];
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: branch.companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
  const secondBranch = { ...branch, id: 'branch-two', name: 'Forest apartment', street: 'Forest road 9', roomCount: 4 };
  const secondRequest = { ...request, id: 'request-two', branchId: secondBranch.id, branchName: secondBranch.name, fromDate: '2026-12-10', toDate: '2026-12-11', notes: 'Forest instructions' };
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/requests' ? [request, secondRequest] : path === '/branches' ? [branch, secondBranch] : [] });
    writes.push({ path, body: req.postDataJSON() }); return route.fulfill({ headers, json: {} });
  });
  await viaManagerPage(page); await page.goto('/manager/partner-requests/');
  await page.getByRole('article', { name: 'Lake apartment request', exact: true }).getByRole('button', { name: 'Edit / reschedule request' }).click();
  await page.getByLabel('From date', { exact: true }).fill('2026-12-05');
  // Modal dialog blocks the page: close it (discarding the draft) before opening the other request.
  await page.getByRole('dialog', { name: /^Edit \/ reschedule / }).getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('article', { name: 'Forest apartment request', exact: true }).getByRole('button', { name: 'Edit / reschedule request' }).click();
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('2026-12-10');
  await expect(page.getByLabel('Cleaning instructions')).toHaveValue('Forest instructions');
  await page.getByRole('button', { name: 'Save request changes' }).click();
  await expect(page.getByText('Request updated. Arrange a new employee offer.')).toBeVisible();
  expect(writes[0]).toMatchObject({ path: '/requests/request-two', body: { fromDate: '2026-12-10', notes: 'Forest instructions' } });
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Lake apartment · Owner company' }) }).getByRole('button', { name: 'Edit property' }).click();
  await page.getByLabel('Property name', { exact: true }).fill('Unsaved lake edit');
  await page.getByRole('dialog', { name: 'Edit property', exact: true }).getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Forest apartment · Owner company' }) }).getByRole('button', { name: 'Edit property' }).click();
  await expect(page.getByLabel('Property name', { exact: true })).toHaveValue('Forest apartment');
  await expect(page.getByLabel('street', { exact: true })).toHaveValue('Forest road 9');
  await page.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.getByText('Property saved.')).toBeVisible();
  expect(writes[1]).toMatchObject({ path: '/branches/branch-two', body: { name: 'Forest apartment', street: 'Forest road 9', roomCount: 4 } });
});
