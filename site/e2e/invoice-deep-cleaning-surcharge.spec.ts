import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// A completed Deep visit is billed as its own 15 EUR net surcharge line; the visit's own price line is unchanged.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'supplier@example.test', phone: '', bankAccountIBAN: 'FI123', bankBic: 'BIC', paymentTermsDays: 14, invoiceNumberPrefix: 'INV', nextInvoiceNumber: 1 };
const shift = { id: 'shift', companyId: 'company', branchId: 'branch', name: 'Biweekly', isActive: true, validFrom: '2026-10-01', schedule: { type: 3 } };

// Two completed normal visits at 30 EUR each, and one completed Deep visit: 60 + 30 + 15 = 105 net, VAT 25.5%.
function preview() {
  const visits = [{ amount: 30, description: 'Per completed visit', quantity: 1, pricingMode: 'PerOccurrence' }, { amount: 30, description: 'Per completed visit', quantity: 1, pricingMode: 'PerOccurrence' }];
  const lines = [
    { lineKey: 'base', shiftId: 'shift', shiftName: 'Biweekly', branchName: 'Main', hours: 0, occurrences: 2, pricingMode: 'PerOccurrence',
      quantity: 2, unitPriceNet: 30, netAmount: 60, vatRatePercent: 25.5, vatAmount: 15.3, total: 75.3, periodFrom: '2026-10-01', periodTo: '2026-10-31', description: 'Per completed visit' },
    { lineKey: 'deep', shiftId: 'shift', shiftName: 'Biweekly', branchName: 'Main', hours: 0, occurrences: 1, pricingMode: 'DeepCleaning',
      quantity: 1, unitPriceNet: 15, netAmount: 15, vatRatePercent: 25.5, vatAmount: 3.83, total: 18.83, periodFrom: '2026-10-01', periodTo: '2026-10-31', description: 'Deep cleaning surcharge 15.00 EUR × 1' },
  ];
  void visits;
  return { id: 'draft', invoiceNumber: '', issuer, customer: { name: 'Methehe' }, companyId: 'company', periodFrom: '2026-10-01', periodTo: '2026-10-31',
    issueDate: '2026-11-01', dueDate: '2026-11-15', status: 'Draft', previewToken: 'token', issuerIsSample: false,
    total: { net: 75, vat: 19.13, gross: 94.13 }, lines, unstartedVisits: [] };
}

async function setup(page: Page) {
  await setupAdminMocks(page);
  const calls = { previews: [] as any[] };
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = request.postData() ? request.postDataJSON() : undefined;
    if (path.endsWith('/shift-preview')) { calls.previews.push(body); return route.fulfill({ headers, json: preview() }); }
    return route.fulfill({ headers, json: path.endsWith('/issuer') ? issuer : path.endsWith('/invoices') ? [] : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Methehe', isActive: true, isPartnerEnabled: false }], total: 1 } : path.endsWith('/shifts') ? [shift] : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  return calls;
}

test('a completed Deep visit appears on the invoice as its own 15 EUR surcharge line', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/invoices/');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Methehe', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice shifts', exact: true }).click(); await page.getByRole('option', { name: 'Biweekly', exact: true }).click(); await page.keyboard.press('Escape');
  await page.getByLabel('Invoice from', { exact: true }).fill('2026-10-01'); await page.getByLabel('Invoice to (inclusive)', { exact: true }).fill('2026-10-31');
  await page.getByRole('textbox', { name: 'Billing street', exact: true }).fill('Customer street'); await page.getByRole('textbox', { name: 'Billing postalCode', exact: true }).fill('00100'); await page.getByRole('textbox', { name: 'Billing city', exact: true }).fill('Helsinki');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();

  // The surcharge is its own row, and the visit price row keeps its own amount.
  const surcharge = page.getByRole('row').filter({ hasText: 'Deep cleaning surcharge 15.00 EUR × 1' });
  await expect(surcharge).toBeVisible();
  await expect(surcharge).toContainText('€15.00');
  await expect(page.getByRole('row').filter({ hasText: 'Per completed visit' }).first()).toContainText('60');
  await expect(page.getByText('Net €75.00 · VAT €19.13 · Total €94.13')).toBeVisible();
});
