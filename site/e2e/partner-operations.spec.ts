import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleanerOffer, CleaningRequest, PartnerChange } from '../src/lib/partnerApi';
import type { InvoiceIssuer, ShiftInvoice, ShiftInvoiceInput } from '../src/lib/invoiceBillingApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', street: 'Lake road 1', city: 'Tampere', isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const cleaning: CleaningRequest = { id: '33333333-3333-4333-8333-333333333333', version: 7, companyId, branchId: branch.id, branchName: branch.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: '', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null };
async function companies(page: Page) {
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
}

test('partner month previews all company visits and issues the exact approved payload', async ({ page }) => {
  await setupAdminMocks(page); await companies(page);
  const issuer: InvoiceIssuer = { companyName: 'ReadySetSiivous', businessId: '3404861-9', companyAddress: { street: 'Issuer road 1', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'office@example.test', phone: '', bankAccountIBAN: 'FI2112345600000785', bankBic: 'NDEAFIHH', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
  const preview: ShiftInvoice = { id: 'preview', invoiceNumber: '', customer: { name: 'Owner company' }, issuer, issueDate: '2026-12-31', dueDate: '2027-01-14', status: 'Preview', periodFrom: '2026-12-01', periodTo: '2026-12-31', previewToken: 'frozen-preview-token', total: { net: 140, vat: 35.7, gross: 175.7 }, lines: [{ lineKey: 'visit-1', description: 'Completed cleaning visits', shiftName: 'Lake apartment cleaning', branchName: branch.name, hours: 4, occurrences: 2, pricingMode: 'PerOccurrence', quantity: 2, unitPriceNet: 70, netAmount: 140, vatRatePercent: 25.5, vatAmount: 35.7, total: 175.7, periodFrom: '2026-12-01', periodTo: '2026-12-31' }] };
  const previews: ShiftInvoiceInput[] = [], issues: (ShiftInvoiceInput & { previewToken: string })[] = [];
  let records: ShiftInvoice[] = [];
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/invoices(?:\/.*)?$/, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/invoices')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/issuer' ? issuer : records });
    if (path === '/shift-preview') { const input = req.postDataJSON() as ShiftInvoiceInput; previews.push(input); return route.fulfill({ headers, json: { ...preview, periodFrom: input.from, periodTo: input.to, lines: preview.lines.map(line => ({ ...line, periodFrom: input.from, periodTo: input.to })) } }); }
    if (path === '/shifts') { issues.push(req.postDataJSON() as ShiftInvoiceInput & { previewToken: string }); records = [{ ...preview, id: 'issued-one', invoiceNumber: 'RSS-2026-001', status: 'Issued' }]; return route.fulfill({ headers, json: records[0] }); }
    return route.fulfill({ status: 404, headers });
  });
  await page.goto('/admin/invoices/'); await page.getByLabel('Partner company month', { exact: true }).check();
  await expect(page.getByLabel('Invoice shifts', { exact: true })).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Owner company', exact: true }).click();
  await page.getByLabel('Partner invoice month').fill('2026-12');
  await page.getByLabel(/^Billing street/).fill('Customer road 2'); await page.getByLabel(/^Billing postalCode/).fill('33100'); await page.getByLabel(/^Billing city/).fill('Tampere');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: /Lake apartment cleaning/ })).toBeVisible();
  await expect(page.getByText('Net €140.00 · VAT €35.70 · Total €175.70', { exact: true })).toBeVisible();
  expect(previews).toEqual([{ companyId, partnerMonth: true, shiftIds: [], from: '2026-12-01', to: '2026-12-31', billingAddress: { street: 'Customer road 2', postalCode: '33100', city: 'Tampere', country: 'FI' }, language: 'en', monthlyOverrides: [] }]);
  expect(issues).toHaveLength(0);
  await page.getByLabel('Partner invoice month').fill('2027-01'); await expect(page.getByRole('heading', { name: 'Invoice preview', exact: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
  await page.getByLabel('Partner company month', { exact: true }).uncheck(); await expect(page.getByLabel('Partner invoice month')).toHaveCount(0); await expect(page.getByRole('textbox', { name: 'Invoice shifts', exact: true })).toBeVisible(); await expect(page.getByLabel('Invoice from', { exact: true })).toBeVisible(); await expect(page.getByLabel('Invoice to (inclusive)', { exact: true })).toBeVisible();
  await page.getByLabel('Partner company month', { exact: true }).check(); await expect(page.getByLabel('Partner invoice month')).toHaveValue(''); await expect(page.getByRole('button', { name: 'Preview invoice', exact: true })).toBeDisabled();
  await page.getByLabel('Partner invoice month').fill('2027-02'); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview', exact: true })).toBeVisible(); await expect(page.getByText('Owner company · 2027-02-01 – 2027-02-28', { exact: true })).toBeVisible();
  expect(previews[1]).toEqual({ ...previews[0], from: '2027-02-01', to: '2027-02-28' }); expect(issues).toHaveLength(0);
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
  await page.goto('/me/shifts/'); await page.getByLabel('Decline reason (optional)').fill('Unavailable during checkout window'); await page.getByRole('button', { name: 'Decline cleaning', exact: true }).click();
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
  page.once('dialog', dialog => dialog.accept('Owner cancelled travel.'));
  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Lake apartment · Cancelled', exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Cancel request', exact: true })).toHaveCount(0);
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
