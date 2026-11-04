import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleanerOffer, CleaningRequest, PartnerChange } from '../src/lib/partnerApi';
import type { InvoiceIssuer, ShiftInvoice, ShiftInvoiceInput } from '../src/lib/invoiceBillingApi';
async function pickMonth(page: Page, label: string) { await page.getByRole('textbox', { name: 'Completion month', exact: true }).click(); await page.getByRole('option', { name: label, exact: true }).click(); }

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', street: 'Lake road 1', city: 'Tampere', isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const cleaning: CleaningRequest = { id: '33333333-3333-4333-8333-333333333333', version: 7, companyId, branchId: branch.id, branchName: branch.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: '', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null };
async function companies(page: Page) {
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
}

test('Partner Operations excludes ordinary properties and reveals editors below a long property list', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [
    { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true },
    { id: 'regular-company', name: 'Ordinary customer', isActive: true, isPartnerEnabled: false },
    { id: 'former-partner', name: 'Former partner', isActive: true, isPartnerEnabled: false },
  ], total: 3 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [] }));
  const partnerProperties: Branch[] = Array.from({ length: 25 }, (_, index) => ({ ...branch, id: `property-${index}`, name: `Partner property ${index}`, postalCode: String(index).padStart(5, '0') }));
  const ordinary = { ...branch, id: 'ordinary', companyId: 'regular-company', name: 'Ordinary property' };
  const former = { ...branch, id: 'former', companyId: 'former-partner', name: 'Former partner property' };
  const writes: { path: string; body: Record<string, unknown> }[] = [];
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/branches' ? [...partnerProperties, ordinary, former] : path === '/quotes' ? [ordinary, former] : [] });
    const body = req.postDataJSON() as Record<string, unknown>; writes.push({ path, body });
    Object.assign(partnerProperties[0], body);
    return route.fulfill({ headers, json: partnerProperties[0] });
  });
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page.getByRole('article')).toHaveCount(25);
  await expect(page.getByText('Ordinary property', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Former partner property', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Edit property', exact: true }).first().click();
  const editor = page.getByRole('dialog', { name: 'Edit property', exact: true });
  await expect(editor).toBeVisible();
  await editor.getByLabel('Rooms (optional)').fill('6');
  await editor.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.getByText('Property saved.', { exact: true })).toBeVisible();
  expect(writes[0]).toMatchObject({ path: '/branches/property-0', body: { roomCount: 6 } });
  await page.getByRole('button', { name: 'Set cleaning price', exact: true }).first().click();
  const pricing = page.getByRole('dialog', { name: /^Price / });
  await expect(pricing).toBeVisible();
  await pricing.getByLabel('Fixed cleaning price excl. VAT (€)').fill('95');
  await pricing.getByRole('button', { name: 'Save pricing', exact: true }).click();
  await expect(page.getByText('Pricing saved.', { exact: true })).toBeVisible();
  expect(writes[1]).toMatchObject({ path: '/branches/property-0/pricing', body: { priceNet: 95 } });
  await page.getByRole('button', { name: 'Add property', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Add property', exact: true }).getByRole('combobox').locator('option')).toHaveText(['Select company', 'Owner company']);
  await page.getByRole('dialog', { name: 'Add property', exact: true }).getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('tab', { name: 'Quote requests', exact: true }).click();
  await expect(page.getByText('No quotes awaiting pricing.', { exact: true })).toBeVisible();
});

test('completion month offers exactly 36 months without older shifts and can be cleared', async ({ page }) => {
  await page.clock.setFixedTime(new Date('2026-11-15T12:00:00Z'));
  await setupAdminMocks(page); await companies(page);
  const issuer: InvoiceIssuer = { companyName: 'ReadySetSiivous', businessId: '3404861-9', companyAddress: { street: 'Issuer road 1', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'office@example.test', phone: '', bankAccountIBAN: 'FI2112345600000785', bankBic: 'NDEAFIHH', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
  const previews: ShiftInvoiceInput[] = [];
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/invoices(?:\/.*)?$/, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/invoices')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/issuer' ? issuer : [] });
    if (path === '/shift-preview') previews.push(req.postDataJSON() as ShiftInvoiceInput);
    return route.fulfill({ status: 404, headers });
  });
  await page.goto('/admin/invoices/');
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Owner company', exact: true }).click();
  const month = page.getByRole('textbox', { name: 'Completion month', exact: true });
  await month.click();
  await expect(page.getByRole('option', { name: 'November 2026', exact: true })).toBeAttached();
  await expect(page.getByRole('option', { name: 'December 2023', exact: true })).toBeAttached();
  await expect(page.getByRole('option', { name: 'November 2023', exact: true })).toHaveCount(0);
  await expect(page.getByRole('option', { name: /^[A-Z][a-z]+ \d{4}$/ })).toHaveCount(36);
  await page.getByRole('option', { name: 'October 2026', exact: true }).click();
  await expect(month).toHaveValue('October 2026');
  const preview = page.getByRole('button', { name: 'Preview invoice', exact: true });
  await expect(preview).toBeEnabled();
  // Mantine hides the clear control from assistive tech (aria-hidden), so target it by its label attribute.
  await page.locator('button[aria-label="Clear completion month"]').click();
  await expect(month).toHaveValue('');
  await expect(preview).toBeDisabled();
  expect(previews).toHaveLength(0);
});

test('partner month previews all company visits and issues the exact approved payload', async ({ page }, testInfo) => {
  // Month options are relative to "now"; freeze it so the labels picked below always exist.
  await page.clock.setFixedTime(new Date('2026-11-15T12:00:00Z'));
  await setupAdminMocks(page); await companies(page);
  const issuer: InvoiceIssuer = { companyName: 'ReadySetSiivous', businessId: '3404861-9', companyAddress: { street: 'Issuer road 1', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'office@example.test', phone: '', bankAccountIBAN: 'FI2112345600000785', bankBic: 'NDEAFIHH', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
  const preview: ShiftInvoice = { id: 'preview', invoiceNumber: '', customer: { name: 'Owner company' }, issuer, issueDate: '2026-12-31', dueDate: '2027-01-14', status: 'Preview', periodFrom: '2026-12-01', periodTo: '2026-12-31', previewToken: 'frozen-preview-token', total: { net: 140, vat: 35.7, gross: 175.7 }, lines: [{ lineKey: 'visit-1', description: 'Completed cleaning visits', shiftName: 'Lake apartment cleaning', branchName: branch.name, hours: 4, occurrences: 2, pricingMode: 'PerOccurrence', quantity: 2, unitPriceNet: 70, netAmount: 140, vatRatePercent: 25.5, vatAmount: 35.7, total: 175.7, periodFrom: '2026-12-01', periodTo: '2026-12-31' }] };
  const baseLine = preview.lines[0];
  preview.lines = [
    { ...baseLine, lineKey: 'a1', branchId: branch.id, shiftName: 'Lake apartment cleaning', quantity: 1, occurrences: 1, unitPriceNet: 40, netAmount: 40, vatAmount: 10.2, total: 50.2, periodFrom: '2026-11-30', periodTo: '2026-11-30', completedOn: '2026-12-01' },
    { ...baseLine, lineKey: 'a2', branchId: branch.id, shiftName: 'Second lake visit', quantity: 1, occurrences: 1, unitPriceNet: 45, netAmount: 45, vatAmount: 11.48, total: 56.48 },
    { ...baseLine, lineKey: 'b1', branchId: 'other-branch', branchName: 'Forest apartment', shiftName: 'Forest visit', quantity: 1, occurrences: 1, unitPriceNet: 55, netAmount: 55, vatAmount: 14.02, total: 69.02 },
  ];
  const previews: ShiftInvoiceInput[] = [], issues: (ShiftInvoiceInput & { previewToken: string })[] = [];
  let records: ShiftInvoice[] = [];
  // An old shift for the company extends the month list past the default 36-month window.
  const oldShift = { id: 'old-shift', companyId, branchId: branch.id, name: 'Old lake cleaning', schedule: {}, isActive: false, validFrom: '2021-03-10' };
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers, json: [oldShift] }));
  await page.route(/\/api\/v1\/admin\/invoices(?:\/.*)?$/, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/invoices')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/issuer' ? issuer : records });
    if (path === '/shift-preview') { const input = req.postDataJSON() as ShiftInvoiceInput; previews.push(input); return route.fulfill({ headers, json: { ...preview, periodFrom: input.from, periodTo: input.to, lines: preview.lines } }); }
    if (path === '/shifts') { issues.push(req.postDataJSON() as ShiftInvoiceInput & { previewToken: string }); records = [{ ...preview, id: 'issued-one', invoiceNumber: 'RSS-2026-001', status: 'Issued' }]; return route.fulfill({ headers, json: records[0] }); }
    return route.fulfill({ status: 404, headers });
  });
  await page.goto('/admin/invoices/');
  await expect(page.getByLabel('Completed cleaning month', { exact: true })).not.toBeChecked();
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Owner company', exact: true }).click();
  await expect(page.getByLabel('Completed cleaning month', { exact: true })).toBeChecked();
  await expect(page.getByRole('textbox', { name: 'Completion month', exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'Completion month', exact: true }).click();
  await expect(page.getByRole('option', { name: 'November 2026', exact: true })).toBeAttached();
  await expect(page.getByRole('option', { name: 'March 2021', exact: true })).toBeAttached();
  await expect(page.getByRole('option', { name: 'February 2021', exact: true })).toHaveCount(0);
  await expect(page.getByRole('option', { name: 'December 2026', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(page.getByLabel('Invoice shifts', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Invoice from', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Invoice to (inclusive)', { exact: true })).toHaveCount(0);
  await pickMonth(page, 'October 2026');
  await page.getByLabel(/^Billing street/).fill('Customer road 2'); await page.getByLabel(/^Billing postalCode/).fill('33100'); await page.getByLabel(/^Billing city/).fill('Tampere');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: /Lake apartment cleaning/ })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: branch.name, exact: true })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Forest apartment', exact: true })).toBeVisible();
  await expect(page.getByText('Completed on: 2026-12-01', { exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Invoice preview', exact: true })).not.toContainText(/partner/i);
  await expect(page.getByText('Net €140.00 · VAT €35.70 · Total €175.70', { exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('branch-grouped-invoice-preview.png'), fullPage: true });
  expect(previews).toEqual([{ companyId, partnerMonth: true, shiftIds: [], from: '2026-10-01', to: '2026-10-31', billingAddress: { street: 'Customer road 2', postalCode: '33100', city: 'Tampere', country: 'FI' }, language: 'en', monthlyOverrides: [] }]);
  expect(issues).toHaveLength(0);
  await pickMonth(page, 'September 2026'); await expect(page.getByRole('heading', { name: 'Invoice preview', exact: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
  await page.getByLabel('Completed cleaning month', { exact: true }).uncheck(); await expect(page.getByRole('textbox', { name: 'Completion month', exact: true })).toHaveCount(0); await expect(page.getByRole('textbox', { name: 'Invoice shifts', exact: true })).toBeVisible(); await expect(page.getByLabel('Invoice from', { exact: true })).toBeVisible(); await expect(page.getByLabel('Invoice to (inclusive)', { exact: true })).toBeVisible();
  await page.getByLabel('Completed cleaning month', { exact: true }).check(); await expect(page.getByRole('textbox', { name: 'Completion month', exact: true })).toHaveValue(''); await expect(page.getByRole('button', { name: 'Preview invoice', exact: true })).toBeDisabled();
  await pickMonth(page, 'August 2026'); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview', exact: true })).toBeVisible(); await expect(page.getByText('Owner company · 2026-08-01 – 2026-08-31', { exact: true })).toBeVisible();
  expect(previews[1]).toEqual({ ...previews[0], from: '2026-08-01', to: '2026-08-31' }); expect(issues).toHaveLength(0);
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByText('Invoice RSS-2026-001 issued.', { exact: true })).toBeVisible();
  expect(issues).toEqual([{ ...previews[1], previewToken: 'frozen-preview-token' }]);
  await expect(page.getByRole('cell', { name: 'RSS-2026-001', exact: true })).toBeVisible();
});

test('cleaner declines an offer with its revision and reason then sees it removed', async ({ page }) => {
  await setupAdminMocks(page); await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['employee']; });
  let offers: CleanerOffer[] = [{ id: 'offer-one', requestId: cleaning.id, version: 7, branchName: branch.name, street: branch.street ?? null, postalCode: null, city: branch.city ?? null, fromDate: cleaning.fromDate, toDate: cleaning.toDate, exactStartLocalTime: null, plannedStartUtc: null, estimatedDurationMinutes: 120, notes: null, timeZoneId: 'Europe/Helsinki' }];
  const decisions: { path: string; method: string; body: { version: number; reason: string } }[] = [];
  await page.route(/\/api\/v1\/me\/shifts\/calendar(?:\?.*)?$/, route => route.fulfill({ headers, json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-12-01', toLocalDate: '2026-12-01' } }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\/.*)?$/, route => {
    const req = route.request(); if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: offers });
    decisions.push({ path: new URL(req.url()).pathname, method: req.method(), body: req.postDataJSON() as { version: number; reason: string } }); offers = []; return route.fulfill({ status: 204, headers });
  });
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } })); await page.goto('/me/shifts/'); await page.getByLabel('Decline reason (optional)').fill('Unavailable during checkout window'); await page.getByRole('button', { name: 'Decline cleaning', exact: true }).click();
  await expect(page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Lake apartment offer', exact: true })).toHaveCount(0);
  expect(decisions).toEqual([{ path: '/api/v1/me/partner-offers/offer-one/decline', method: 'POST', body: { version: 7, reason: 'Unavailable during checkout window' } }]);
});

test('staff resolves owner changes, creates a property and cancels a versioned request', async ({ page }) => {
  await setupAdminMocks(page); await companies(page);
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [] }));
  let properties: Branch[] = [branch], requests: CleaningRequest[] = [cleaning];
  let changes: PartnerChange[] = [{ id: 'change-one', companyId, branchId: branch.id, requestId: null, kind: 'Property', message: 'Add spare towels.', createdUtc: '2026-11-01T10:00:00Z', resolvedUtc: null, resolution: null }];
  const writes: { path: string; method: string; body: Record<string, unknown> }[] = [];
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/requests' ? requests : path === '/branches' ? properties : path === '/change-requests' ? changes : [] });
    const body = req.postDataJSON() as Record<string, unknown>; writes.push({ path, method: req.method(), body });
    if (path === '/change-requests/change-one/resolve') { changes = [{ ...changes[0], resolvedUtc: '2026-11-02T10:00:00Z', resolution: String(body.resolution) }]; return route.fulfill({ headers, json: changes[0] }); }
    if (path === '/branches') { const added: Branch = { ...branch, id: 'new-branch', name: String(body.name), street: String(body.street), city: String(body.city), roomCount: Number(body.roomCount), bedCount: Number(body.bedCount), priceNet: null, pricingReady: false }; properties = [...properties, added]; return route.fulfill({ headers, json: added }); }
    if (path === `/requests/${cleaning.id}/cancel`) { requests = [{ ...requests[0], version: 8, status: 'Cancelled' }]; return route.fulfill({ headers, json: requests[0] }); }
    return route.fulfill({ status: 404, headers });
  });
  await page.goto('/admin/partner-requests/'); await page.getByRole('tab', { name: 'Changes', exact: true }).click();
  await expect(page.getByText('Add spare towels.', { exact: true })).toBeVisible(); await page.getByRole('button', { name: 'Resolve change request', exact: true }).click();
  await page.getByLabel('Resolution', { exact: true }).fill('Spare towels added to cleaning instructions.'); await page.getByRole('button', { name: 'Save resolution', exact: true }).click();
  await expect(page.getByText('Resolved: Spare towels added to cleaning instructions.', { exact: true })).toBeVisible();
  expect(writes[0]).toEqual({ path: '/change-requests/change-one/resolve', method: 'POST', body: { resolution: 'Spare towels added to cleaning instructions.' } });
  await page.getByRole('tab', { name: 'Properties', exact: true }).click(); await page.getByRole('button', { name: 'Add property', exact: true }).click();
  await page.getByRole('combobox').selectOption(companyId); await page.getByLabel('Property name', { exact: true }).fill('Forest apartment'); await page.getByLabel('street', { exact: true }).fill('Forest road 8'); await page.getByLabel('city', { exact: true }).fill('Oulu'); await page.getByLabel('Rooms (optional)').fill('3'); await page.getByLabel('Beds (optional)').fill('4');
  await page.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Forest apartment · Owner company', exact: true })).toBeVisible();
  expect(writes[1]).toMatchObject({ path: '/branches', method: 'POST', body: { companyId, name: 'Forest apartment', street: 'Forest road 8', city: 'Oulu', roomCount: 3, bedCount: 4, priceNet: null, isActive: true } });
  await page.getByRole('tab', { name: 'Requests', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();
  const cancelDialog = page.getByRole('dialog', { name: /^Cancel / });
  await expect(cancelDialog.getByRole('button', { name: 'Confirm cancellation', exact: true })).toBeDisabled();
  await cancelDialog.getByLabel('Reason for cancellation', { exact: true }).fill('Owner cancelled travel.');
  await cancelDialog.getByRole('button', { name: 'Confirm cancellation', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Lake apartment · Cancelled', exact: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Cancel request', exact: true })).toHaveCount(0);
  expect(writes[2]).toEqual({ path: `/requests/${cleaning.id}/cancel`, method: 'POST', body: { version: 7, reason: 'Owner cancelled travel.' } });
});

test('staff updates reusable cleaning pricing from Properties and sees the refreshed price', async ({ page }) => {
  await setupAdminMocks(page); await companies(page); let property: Branch = { ...branch }; const writes: { path: string; method: string; body: Record<string, unknown> }[] = [];
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/branches' ? [property] : [] });
    const body = req.postDataJSON() as Record<string, unknown>; writes.push({ path, method: req.method(), body });
    if (path !== `/branches/${branch.id}/pricing` || req.method() !== 'PUT') return route.fulfill({ status: 404, headers });
    property = { ...property, priceNet: Number(body.priceNet), vatRatePercent: Number(body.vatRatePercent), estimatedDurationMinutes: Number(body.estimatedDurationMinutes), pricingReady: true };
    return route.fulfill({ headers, json: property });
  });
  await page.goto('/admin/partner-requests/'); await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page.getByText('€70.00 excl. VAT · 120 minutes', { exact: true })).toBeVisible(); await page.getByRole('button', { name: 'Set cleaning price', exact: true }).click();
  await expect(page.getByLabel('Fixed cleaning price excl. VAT (€)')).toHaveValue('70'); await expect(page.getByLabel('VAT (%)')).toHaveValue('25.5'); await expect(page.getByLabel('Estimated duration (minutes)')).toHaveValue('120');
  await page.getByLabel('Fixed cleaning price excl. VAT (€)').fill('95'); await page.getByLabel('VAT (%)').fill('14'); await page.getByLabel('Estimated duration (minutes)').fill('150'); await page.getByRole('button', { name: 'Save pricing', exact: true }).click();
  await expect(page.getByText('Pricing saved.', { exact: true })).toBeVisible(); await expect(page.getByText('€95.00 excl. VAT · 150 minutes', { exact: true })).toBeVisible();
  expect(writes).toEqual([{ path: `/branches/${branch.id}/pricing`, method: 'PUT', body: { priceNet: 95, vatRatePercent: 14, estimatedDurationMinutes: 150 } }]);
  await page.getByRole('button', { name: 'Set cleaning price', exact: true }).click(); await expect(page.getByLabel('VAT (%)')).toHaveValue('14');
});

test('A01 automatic cleaner can take a flexible visit; a start time is optional for both cleaner types', async ({ page }) => {
  await setupAdminMocks(page); await companies(page);
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [
    { id: 'auto', firstName: 'Auto', lastName: 'Cleaner', isActive: true, role: 'employee', autoAcceptWork: true },
    { id: 'manual', firstName: 'Manual', lastName: 'Cleaner', isActive: true, role: 'employee', autoAcceptWork: false },
  ] }));
  let current = { ...cleaning }, writes: any[] = [];
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const request = route.request(), path = new URL(request.url()).pathname.split('/partner')[1];
    if (request.method() === 'OPTIONS') return route.fulfill({ headers });
    if (request.method() === 'GET') return route.fulfill({ headers, json: path === '/requests' ? [current] : path === '/branches' ? [branch] : [] });
    if (path.endsWith('/offer')) { const body = request.postDataJSON(); writes.push(body); current = { ...current, status: body.employeeId === 'auto' ? 'Accepted' : 'Offered', version: current.version + 1 }; return route.fulfill({ headers, json: current }); }
    return route.fulfill({ status: 404, headers });
  });
  await page.goto('/admin/partner-requests/');
  await page.getByRole('button', { name: 'Arrange cleaner / time' }).click();
  await page.getByRole('combobox', { name: 'Cleaner', exact: true }).selectOption('auto');
  const time = page.getByLabel(/^Planned start/); await expect(time).not.toHaveAttribute('required', '');
  await page.getByRole('button', { name: 'Send cleaning offer' }).click();
  await expect(page.getByText('Work scheduled automatically.', { exact: true })).toBeVisible();
  expect(writes[0]).toMatchObject({ employeeId: 'auto', plannedStartLocal: null });
  await page.getByRole('tab', { name: 'Assigned', exact: true }).click();
  await page.getByRole('button', { name: 'Arrange cleaner / time' }).click();
  await page.getByRole('combobox', { name: 'Cleaner', exact: true }).selectOption('manual');
  await expect(time).not.toHaveAttribute('required', ''); await page.getByRole('button', { name: 'Send cleaning offer' }).click();
  await expect(page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible(); expect(writes[1].plannedStartLocal).toBeNull();
  current = { ...cleaning, exactStartLocalTime: '09:00:00' };
  await page.reload(); await page.getByRole('button', { name: 'Arrange cleaner / time' }).click();
  await page.getByRole('combobox', { name: 'Cleaner', exact: true }).selectOption('auto');
  await expect(time).not.toHaveAttribute('required', '');
  // A fixed appointment supplies its own start, so the optional field may stay blank.
  await page.getByRole('button', { name: 'Send cleaning offer' }).click();
  await expect(page.getByText('Work scheduled automatically.', { exact: true })).toBeVisible();
  expect(writes[2]).toMatchObject({ employeeId: 'auto', plannedStartLocal: null });
  current = { ...cleaning, fromDate: '2026-12-01', toDate: '2026-12-01', exactStartLocalTime: '09:00:00' };
  await page.reload(); await page.getByRole('button', { name: 'Arrange cleaner / time' }).click();
  await page.getByRole('combobox', { name: 'Cleaner', exact: true }).selectOption('auto'); await expect(time).not.toHaveAttribute('required', '');
  await page.getByRole('button', { name: 'Send cleaning offer' }).click();
  await expect(page.getByText('Work scheduled automatically.', { exact: true })).toBeVisible(); expect(writes).toHaveLength(4);
});
