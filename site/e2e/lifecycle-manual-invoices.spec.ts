import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Supplier street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: '', phone: '', bankAccountIBAN: 'FI123', bankBic: 'BIC', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
async function setup(page: Page, options: { lostResponse?: boolean; delayPreview?: boolean; stale?: boolean; failPreview?: boolean } = {}) {
  await setupAdminMocks(page);
  const calls = { previews: [] as any[], issues: [] as any[], downloads: 0 }; let saved: any = null;
  const invoice = (input: any) => {
    const lines = input.lines.map((row: any, i: number) => {
      const netAmount = Math.round(row.quantity * row.unitPriceNet * 100) / 100; const vatAmount = Math.round(netAmount * 25.5) / 100;
      return { ...row, lineKey: `manual-${i}`, netAmount, vatAmount, vatRatePercent: 25.5, total: netAmount + vatAmount };
    });
    const net = lines.reduce((n: number, l: any) => n + l.netAmount, 0); const vat = lines.reduce((n: number, l: any) => n + l.vatAmount, 0);
    return { id: 'manual', invoiceNumber: '2026-0001', customer: { name: 'Customer' }, issuer, lines, total: { net, vat, gross: net + vat }, status: 'Issued', issueDate: '2026-11-02', dueDate: '2026-11-16', previewToken: 'manual-token' };
  };
  await page.route(/\/api\/v1\/admin\//, async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/manual-preview')) {
      const input = req.postDataJSON(); calls.previews.push(input);
      if (options.delayPreview) await new Promise(resolve => setTimeout(resolve, 600));
      return route.fulfill({ status: options.failPreview ? 400 : 200, headers, json: options.failPreview ? { detail: 'The invoice total must be greater than zero.' } : invoice(input) });
    }
    if (path.endsWith('/invoices/manual')) {
      const input = req.postDataJSON(); calls.issues.push(input);
      if (options.stale) return route.fulfill({ status: 409, headers, json: { detail: 'Invoice details changed. Load a new preview before issuing.' } });
      saved = saved || invoice(input);
      if (options.lostResponse && calls.issues.length === 1) return route.abort('failed');
      return route.fulfill({ headers, json: saved });
    }
    if (path.endsWith('/pdf')) { calls.downloads++; return route.fulfill({ headers, contentType: 'application/pdf', body: '%PDF-1.4 manual invoice' }); }
    return route.fulfill({ headers, json: path.endsWith('/issuer') ? issuer : path.endsWith('/invoices') ? saved ? [saved] : [] : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Customer', isActive: true }], total: 1 } : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  return calls;
}
async function fill(page: Page) {
  await page.goto('/admin/invoices/');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice type', exact: true }).click(); await page.getByRole('option', { name: 'Manual invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Customer', exact: true }).click();
  await page.getByRole('textbox', { name: 'Billing street', exact: true }).fill('Customer street'); await page.getByRole('textbox', { name: 'Billing postalCode', exact: true }).fill('00100'); await page.getByRole('textbox', { name: 'Billing city', exact: true }).fill('Helsinki');
  await page.getByRole('textbox', { name: 'Row 1 description', exact: true }).fill('Additional agreed service');
  await page.getByRole('textbox', { name: 'Row 1 quantity (pieces)', exact: true }).fill('2');
  await page.getByRole('textbox', { name: 'Row 1 unit price before VAT (€)', exact: true }).fill('50');
}
test('manual rows preview VAT and issue into existing history and PDF download', async ({ page }) => {
  const calls = await setup(page); await fill(page);
  await expect(page.getByRole('textbox', { name: 'Invoice shifts', exact: true })).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Invoice from', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Remove row 1', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Add row', exact: true }).click();
  await page.getByRole('textbox', { name: 'Row 2 description', exact: true }).fill('Materials'); await page.getByRole('textbox', { name: 'Row 2 unit price before VAT (€)', exact: true }).fill('20');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByText('Net €120.00 · VAT €30.60 · Total €150.60')).toBeVisible();
  if (process.env.MANUAL_INVOICE_QA_OUTPUT) await page.screenshot({ path: `${process.env.MANUAL_INVOICE_QA_OUTPUT}/manual-preview-${test.info().project.name}.png`, fullPage: true });
  await expect(page.getByRole('columnheader', { name: 'Scheduled hours' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice 2026-0001 issued.');
  expect(calls.issues).toEqual([{ ...calls.previews[0], previewToken: 'manual-token' }]);
  expect(calls.issues[0].lines).toEqual([{ description: 'Additional agreed service', quantity: 2, unitPriceNet: 50 }, { description: 'Materials', quantity: 1, unitPriceNet: 20 }]);
  expect(calls.issues[0].shiftIds).toBeUndefined(); expect(calls.issues[0].submissionId).toMatch(/^[0-9a-f-]{36}$/);
  await page.getByRole('button', { name: 'PDF', exact: true }).click(); await expect.poll(() => calls.downloads).toBe(1);
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Row 1 description', exact: true })).toHaveValue('');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
test('row changes and removal invalidate preview; mode switch restores shift controls', async ({ page }) => {
  const calls = await setup(page); await fill(page); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  const baseRows = calls.previews.at(-1).lines.length;
  await page.keyboard.press('Escape'); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  // Each re-preview must send the rows as they are now, so a stale preview cannot be issued.
  await page.getByRole('button', { name: 'Add row', exact: true }).click();
  await page.getByRole('textbox', { name: 'Row 2 description', exact: true }).fill('Extra row');
  await page.getByRole('textbox', { name: 'Row 2 quantity (pieces)', exact: true }).fill('1');
  await page.getByRole('textbox', { name: 'Row 2 unit price before VAT (€)', exact: true }).fill('10');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  expect(calls.previews.at(-1).lines).toHaveLength(baseRows + 1);
  await page.keyboard.press('Escape'); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('button', { name: 'Remove row 2', exact: true }).click();
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  expect(calls.previews.at(-1).lines).toHaveLength(baseRows);
  await page.keyboard.press('Escape'); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Row 1 unit price before VAT (€)', exact: true }).fill('51');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  expect(calls.previews.at(-1).lines[0].unitPriceNet).toBe(51);
  await page.keyboard.press('Escape'); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice type', exact: true }).click(); await page.getByRole('option', { name: 'Shift invoice', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Invoice from', exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Add row', exact: true })).toHaveCount(0);
});
test('lost issue response locks edits and retry sends same submission exactly once into history', async ({ page }) => {
  const calls = await setup(page, { lostResponse: true }); await fill(page);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByText(/The issue result could not be confirmed/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'New invoice', exact: true })).toBeDisabled();
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog', { name: 'Invoice preview' })).toBeVisible();
  await page.getByRole('button', { name: 'Retry issuing invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice 2026-0001 issued.'); expect(calls.issues).toHaveLength(2); expect(calls.issues[0]).toEqual(calls.issues[1]);
  await expect(page.getByRole('cell', { name: '2026-0001', exact: true })).toHaveCount(1);
});
test('stale preview requires preview again and preserves rows', async ({ page }) => {
  await setup(page, { stale: true }); await fill(page); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('main').getByRole('alert').filter({ hasText: 'Invoice details changed' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); await expect(page.getByRole('textbox', { name: 'Row 1 description', exact: true })).toHaveValue('Additional agreed service');
});
test('late preview after a row edit cannot be issued', async ({ page }) => {
  const calls = await setup(page, { delayPreview: true }); await fill(page); await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect.poll(() => calls.previews.length).toBe(1); await page.getByRole('textbox', { name: 'Row 1 description', exact: true }).fill('Updated agreement');
  await expect(page.getByRole('button', { name: 'Preview invoice', exact: true })).not.toHaveAttribute('data-loading');
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toHaveCount(0); expect(calls.issues).toHaveLength(0);
});
test('required rows fail locally and backend validation preserves input', async ({ page }) => {
  const calls = await setup(page, { failPreview: true }); await fill(page); await page.getByRole('textbox', { name: 'Row 1 description', exact: true }).fill('');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('main').getByRole('alert').filter({ hasText: 'every row' })).toBeVisible(); expect(calls.previews).toHaveLength(0);
  await page.getByRole('textbox', { name: 'Row 1 description', exact: true }).fill('Free service'); await page.getByRole('textbox', { name: 'Row 1 unit price before VAT (€)', exact: true }).fill('0');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('main').getByRole('alert').filter({ hasText: 'greater than zero' })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Row 1 description', exact: true })).toHaveValue('Free service');
});

test('manual VAT uses saved settings and saving issuer details clears preview', async ({ page }) => {
  await setup(page); await fill(page);
  await page.route(/\/api\/v1\/admin\/invoices\/issuer$/, route => route.fulfill({ headers, json: route.request().method() === 'PUT' ? route.request().postDataJSON() : issuer }));
  await page.keyboard.press('Escape'); await page.getByRole('button', { name: 'Company and payment details' }).click();
  await page.getByRole('textbox', { name: 'Default VAT (%)', exact: true }).fill('10');
  await page.getByRole('button', { name: 'Save company details', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Invoice issuer' })).toHaveCount(0); await expect(page.getByRole('status')).toHaveText('Company and payment details saved.');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await expect(page.getByText(/VAT \(10%\) is added/)).toBeVisible();
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
});
