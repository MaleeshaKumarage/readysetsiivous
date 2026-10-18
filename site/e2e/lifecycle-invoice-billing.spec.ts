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

test('issuer settings save every company payment field and sample flag across reload', async ({ page }) => {
  await setup(page); let saved: any = { ...issuer, companyAddress: { ...issuer.companyAddress }, isSampleData: false }; const writes: any[] = [];
  await page.route(/\/api\/v1\/admin\/invoices\/issuer$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'PUT') { saved = route.request().postDataJSON(); writes.push(saved); }
    return route.fulfill({ headers, json: saved });
  });
  await page.goto('/admin/invoices/'); await page.getByRole('button', { name: 'Company and payment details' }).click();
  const fields = { 'Issuer company name': 'New supplier', 'Business ID': '7654321-0', 'Issuer email': 'billing@example.test', 'Issuer phone': '+358401234567', IBAN: 'FI2112345600000785', BIC: 'NDEAFIHH', 'Issuer street': 'New street 2', 'Issuer postalCode': '00200', 'Issuer city': 'Espoo', 'Issuer country': 'FI', 'Payment terms (days)': '10', 'Default VAT (%)': '25.5' };
  for (const [label, value] of Object.entries(fields)) await page.getByRole('textbox', { name: label, exact: true }).fill(value);
  await page.getByRole('checkbox', { name: 'Sample issuer details' }).check(); await page.getByRole('button', { name: 'Save company details' }).click();
  await expect(page.getByRole('status')).toHaveText('Company and payment details saved.');
  expect(writes).toEqual([{ companyName: 'New supplier', businessId: '7654321-0', email: 'billing@example.test', phone: '+358401234567', bankAccountIBAN: 'FI2112345600000785', bankBic: 'NDEAFIHH', companyAddress: { street: 'New street 2', postalCode: '00200', city: 'Espoo', country: 'FI' }, paymentTermsDays: 10, defaultVatRatePercent: 25.5, isSampleData: true }]);
  await page.reload(); await page.getByRole('button', { name: 'Company and payment details' }).click();
  for (const [label, value] of Object.entries(fields)) await expect(page.getByRole('textbox', { name: label, exact: true })).toHaveValue(value);
  await expect(page.getByRole('checkbox', { name: 'Sample issuer details' })).toBeChecked();
});

test('changing PDF language discards preview and uses the new language when issued', async ({ page }) => {
  const calls = await setup(page); await fillInvoice(page); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  await page.getByRole('textbox', { name: 'PDF language', exact: true }).click(); await page.getByRole('option', { name: 'Swedish', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.'); expect(calls.previews.map(value => value.language)).toEqual(['en', 'sv']); expect(calls.issues[0].language).toBe('sv');
});

test('removing a monthly override invalidates preview and removes it from the next payload', async ({ page }) => {
  const calls = await setup(page); await fillInvoice(page); await page.getByText('Override a monthly subtotal', { exact: true }).click();
  await page.getByRole('textbox', { name: 'Override shift', exact: true }).click(); await page.getByRole('option', { name: 'Biweekly', exact: true }).click();
  await page.getByLabel('Override month', { exact: true }).fill('2026-10'); await page.getByRole('textbox', { name: 'Monthly subtotal before VAT (€)', exact: true }).fill('150'); await page.getByLabel('Monthly override reason', { exact: true }).fill('Agreed amount');
  await page.getByRole('button', { name: 'Apply monthly override' }).click(); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  await page.getByRole('button', { name: 'Remove override', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible(); expect(calls.previews[0].monthlyOverrides).toHaveLength(1); expect(calls.previews[1].monthlyOverrides).toEqual([]);
});

test('invoice PDF downloads and paid or void actions refresh status with explicit void confirmation', async ({ page }) => {
  await setup(page); let status = 'Issued'; let reads = 0; const paid: any[] = []; let voids = 0; let downloads = 0;
  await page.route(/\/api\/v1\/admin\/invoices(?:\/invoice\/(?:pdf|mark-paid|void))?$/, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/pdf')) { downloads++; return route.fulfill({ headers, contentType: 'application/pdf', body: '%PDF-1.4 synthetic invoice' }); }
    if (path.endsWith('/mark-paid')) { paid.push(request.postDataJSON()); status = 'Paid'; return route.fulfill({ status: 204, headers }); }
    if (path.endsWith('/void')) { voids++; status = 'Voided'; return route.fulfill({ status: 204, headers }); }
    reads++; return route.fulfill({ headers, json: [{ ...invoice, status }] });
  });
  await page.goto('/admin/invoices/'); const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'INV-1', exact: true }) });
  const download = page.waitForEvent('download'); await row.getByRole('button', { name: 'PDF', exact: true }).click(); expect((await download).suggestedFilename()).toBe('invoice-invoice.pdf'); expect(downloads).toBe(1);
  const before = reads; await row.getByRole('button', { name: 'Mark paid', exact: true }).click(); await expect(row.getByRole('cell', { name: 'Paid', exact: true })).toBeVisible(); expect(reads).toBeGreaterThan(before); expect(paid).toHaveLength(1); expect(Number.isFinite(Date.parse(paid[0].paidAtUtc))).toBe(true); await expect(row.getByRole('button', { name: 'Mark paid', exact: true })).toHaveCount(0);
  status = 'Issued'; await page.reload(); await expect(row.getByRole('button', { name: 'Void', exact: true })).toBeVisible();
  page.once('dialog', dialog => { expect(dialog.message()).toContain('INV-1'); return dialog.dismiss(); }); await row.getByRole('button', { name: 'Void', exact: true }).click(); expect(voids).toBe(0); await expect(row.getByRole('cell', { name: 'Issued', exact: true })).toBeVisible();
  page.once('dialog', dialog => dialog.accept()); await row.getByRole('button', { name: 'Void', exact: true }).click(); await expect(row.getByRole('cell', { name: 'Voided', exact: true })).toBeVisible(); expect(voids).toBe(1); await expect(row.getByRole('button', { name: 'Void', exact: true })).toHaveCount(0);
});

test('on-call occurrence price requires a reason and saves only that visit', async ({ page }) => {
  await setup(page); const writes: any[] = []; const visit = { startUtc: '2026-10-20T13:00:00Z', endUtc: '2026-10-20T15:00:00Z', assigneeIds: [] };
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers, json: [{ ...shift, schedule: { type: 4 } }] }));
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/(?:occurrences|occurrence-price)(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'PUT') { writes.push(route.request().postDataJSON()); return route.fulfill({ headers, json: {} }); }
    return route.fulfill({ headers, json: [visit] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Occurrences', exact: true }).click(); await page.getByRole('button', { name: 'Visit price', exact: true }).click();
  const dialog = page.getByRole('dialog').filter({ hasText: 'Visit price — Biweekly' });
  await dialog.getByRole('textbox', { name: 'Visit price before VAT (€)', exact: true }).fill('75'); await expect(dialog.getByRole('button', { name: 'Save price', exact: true })).toBeDisabled();
  await dialog.getByRole('textbox', { name: 'Reason for visit price override', exact: true }).fill('Extra cleaning requested'); await dialog.getByRole('button', { name: 'Save price', exact: true }).click(); await expect(dialog).toHaveCount(0);
  expect(writes).toEqual([{ occurrenceStartUtc: visit.startUtc, rateNet: 75, reason: 'Extra cleaning requested' }]);
});

test('hourly pricing shows history and saves VAT while cancelling edits sends no mutation', async ({ page }) => {
  const calls = await setup(page); const oldPlan = { mode: 'Monthly', rateNet: 150, vatRatePercent: 25.5, effectiveFrom: '2026-10-01' }; let plans = [oldPlan];
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/pricing$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'PUT') { const value = route.request().postDataJSON(); calls.pricing.push(value); plans = [...plans, value]; }
    return route.fulfill({ headers, json: { plans, overrides: [] } });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Pricing', exact: true }).click(); let dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('cell', { name: '€150.00', exact: true })).toBeVisible(); await dialog.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true }).fill('999'); await dialog.getByRole('button', { name: 'Cancel', exact: true }).click(); expect(calls.pricing).toEqual([]);
  await page.getByRole('button', { name: 'Pricing', exact: true }).click(); dialog = page.getByRole('dialog'); await expect(dialog.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true })).toHaveValue('150');
  await dialog.getByRole('textbox', { name: 'Pricing basis', exact: true }).click(); await page.getByRole('option', { name: 'Per scheduled hour', exact: true }).click(); await dialog.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true }).fill('35'); await dialog.getByRole('textbox', { name: 'VAT (%)', exact: true }).fill('14'); await dialog.getByRole('textbox', { name: 'Effective from', exact: true }).fill('2026-11-01'); await dialog.getByRole('button', { name: 'Save price', exact: true }).click(); await expect(dialog).toHaveCount(0);
  expect(calls.pricing).toEqual([{ mode: 'Hourly', rateNet: 35, vatRatePercent: 14, effectiveFrom: '2026-11-01' }]); await page.getByRole('button', { name: 'Pricing', exact: true }).click(); await expect(page.getByRole('dialog').getByRole('cell', { name: '€35.00', exact: true })).toBeVisible(); await expect(page.getByRole('dialog').getByRole('cell', { name: '€150.00', exact: true })).toBeVisible();
});
