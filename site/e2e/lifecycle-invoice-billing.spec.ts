import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const shift = { id: 'shift', companyId: 'company', branchId: 'branch', name: 'Biweekly', isActive: true, validFrom: '2026-10-01', schedule: { type: 3 } };
const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'supplier@example.test', phone: '', bankAccountIBAN: 'FI123', bankBic: 'BIC', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
const invoice = { id: 'invoice', invoiceNumber: 'INV-1', issuer, customer: { name: 'Methehe' }, companyId: 'company', periodFrom: '2026-10-01', periodTo: '2026-10-31', issueDate: '2026-11-01', dueDate: '2026-11-15', status: 'Issued', previewToken: 'bound-token', total: { net: 150, vat: 38.25, gross: 188.25 }, lines: [{ lineKey: 'line', shiftId: 'shift', shiftName: 'Biweekly', branchName: 'Main', hours: 4, occurrences: 2, pricingMode: 'Monthly', quantity: 1, unitPriceNet: 150, netAmount: 150, vatRatePercent: 25.5, vatAmount: 38.25, total: 188.25, periodFrom: '2026-10-01', periodTo: '2026-10-31' }] };
async function setup(page: Page, options: { failPreview?: boolean; delayPreview?: boolean } = {}) {
  await setupAdminMocks(page); const calls = { previews: [] as any[], issues: [] as any[], pricing: [] as any[] }; let issued = false;
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = request.postData() ? request.postDataJSON() : undefined;
    if (path.endsWith('/shift-preview')) { calls.previews.push(body); if (options.delayPreview) await new Promise(resolve => setTimeout(resolve, 600)); return route.fulfill({ status: options.failPreview ? 409 : 200, headers, json: options.failPreview ? { detail: 'No pricing is configured for this billing period.' } : invoice }); }
    if (path.endsWith('/invoices/shifts')) { calls.issues.push(body); issued = true; return route.fulfill({ headers, json: invoice }); }
    if (path.endsWith('/pricing')) { if (body) calls.pricing.push(body); return route.fulfill({ headers, json: { plans: body ? [body] : [], overrides: [] } }); }
    return route.fulfill({ headers, json: path.endsWith('/issuer') ? issuer : path.endsWith('/invoices') ? issued ? [invoice] : [] : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Methehe', isActive: true }], total: 1 } : path.endsWith('/shifts') ? [shift] : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  return calls;
}
async function fillInvoice(page: Page) {
  await page.goto('/admin/invoices/');
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Methehe', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice shifts', exact: true }).click(); await page.getByRole('option', { name: 'Biweekly', exact: true }).click(); await page.keyboard.press('Escape');
  await page.getByLabel('Invoice from', { exact: true }).fill('2026-10-01'); await page.getByLabel('Invoice to (inclusive)', { exact: true }).fill('2026-10-31');
  await page.getByRole('textbox', { name: 'Billing street', exact: true }).fill('Customer street'); await page.getByRole('textbox', { name: 'Billing postalCode', exact: true }).fill('00100'); await page.getByRole('textbox', { name: 'Billing city', exact: true }).fill('Helsinki');
}
test('admin previews shift charges then issues the exact preview snapshot', async ({ page }) => {
  const calls = await setup(page); await fillInvoice(page);
  await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  await expect(page.getByText('Net €150.00 · VAT €38.25 · Total €188.25')).toBeVisible();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click(); await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.');
  expect(calls.issues).toEqual([{ ...calls.previews[0], previewToken: 'bound-token' }]); await expect(page.getByRole('cell', { name: 'INV-1', exact: true })).toBeVisible();
});
test('changing filters invalidates a preview and its in-flight response', async ({ page }) => {
  const calls = await setup(page, { delayPreview: true }); await fillInvoice(page); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect.poll(() => calls.previews.length).toBe(1); await page.getByLabel('Invoice to (inclusive)', { exact: true }).fill('2026-10-30');
  await expect(page.getByRole('button', { name: 'Preview invoice', exact: true })).not.toHaveAttribute('data-loading');
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0); expect(calls.issues).toHaveLength(0);
});
test('preview exposes the real billing validation reason', async ({ page }) => {
  await setup(page, { failPreview: true }); await fillInvoice(page); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'No pricing is configured' })).toHaveText('No pricing is configured for this billing period.'); await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
});
test('admin saves an effective monthly rate before VAT outside quality cycle', async ({ page }) => {
  const calls = await setup(page); await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Pricing', exact: true }).click();
  await page.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true }).fill('150'); await page.getByRole('textbox', { name: 'Effective from', exact: true }).fill('2026-10-01');
  await page.getByRole('button', { name: 'Save price', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(calls.pricing).toEqual([{ mode: 'Monthly', rateNet: 150, vatRatePercent: 25.5, effectiveFrom: '2026-10-01' }]);
});
test('monthly subtotal override is explicit and included in preview and issuance', async ({ page }) => {
  const calls = await setup(page); await fillInvoice(page);
  await page.getByText('Override a monthly subtotal', { exact: true }).click();
  await page.getByRole('textbox', { name: 'Override shift', exact: true }).click(); await page.getByRole('option', { name: 'Biweekly', exact: true }).click();
  await page.getByLabel('Override month', { exact: true }).fill('2026-10');
  await page.getByRole('textbox', { name: 'Monthly subtotal before VAT (€)', exact: true }).fill('150');
  await expect(page.getByRole('button', { name: 'Apply monthly override' })).toBeDisabled();
  await page.getByLabel('Monthly override reason', { exact: true }).fill('Full monthly fee agreed');
  await page.getByRole('button', { name: 'Apply monthly override' }).click();
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click(); await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.');
  expect(calls.previews[0].monthlyOverrides).toEqual([{ shiftId: 'shift', month: '2026-10', amountNet: 150, reason: 'Full monthly fee agreed' }]);
  expect(calls.issues[0].monthlyOverrides).toEqual(calls.previews[0].monthlyOverrides);
});
test('company selection loads later pages and retries an initial data failure', async ({ page }) => {
  await setup(page); let attempts = 0; let fail = true;
  await page.route(/\/api\/v1\/admin\/companies\?/, route => {
    const url = new URL(route.request().url()); if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    attempts++; if (fail) return route.fulfill({ status: 503, headers, json: {} });
    const skip = Number(url.searchParams.get('skip'));
    return route.fulfill({ headers, json: { total: 101, items: skip === 0 ? Array.from({ length: 100 }, (_, index) => ({ id: `c${index}`, name: `Company ${index}`, isActive: true })) : [{ id: 'company', name: 'Methehe', isActive: true }] } });
  });
  await page.goto('/admin/invoices/'); await expect(page.getByRole('button', { name: 'Retry loading invoice data' })).toBeVisible(); fail = false; const priorAttempts = attempts; await page.getByRole('button', { name: 'Retry loading invoice data' }).click();
  await page.getByRole('textbox', { name: 'Company', exact: true }).fill('Methehe'); await expect(page.getByRole('option', { name: 'Methehe', exact: true })).toBeVisible();
  expect(attempts - priorAttempts).toBe(2);
});
