import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'supplier@example.test', phone: '', bankAccountIBAN: 'FI123', bankBic: 'BIC', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
const shift = { id: 'shift', companyId: 'company', branchId: 'branch', name: 'Biweekly', isActive: true, validFrom: '2026-10-01', schedule: { type: 3 } };
// Two scheduled visits in the period have no started work. Each one is billed at 50 EUR net only when ticked.
const unstarted = [
  { shiftId: 'shift', shiftName: 'Biweekly', startUtc: '2026-10-23T13:00:00Z', endUtc: '2026-10-23T15:00:00Z' },
  { shiftId: 'shift', shiftName: 'Biweekly', startUtc: '2026-10-30T14:00:00Z', endUtc: '2026-10-30T16:00:00Z' },
];

function previewFor(input: any) {
  const decisions: { shiftId: string; startUtc: string; include: boolean }[] = input?.unstartedDecisions ?? [];
  const isIncluded = (visit: typeof unstarted[number]) => decisions.some(d => d.shiftId === visit.shiftId && d.startUtc === visit.startUtc && d.include);
  const ticked = unstarted.filter(isIncluded).length;
  const net = 100 + 50 * ticked;
  const vat = Math.round(net * 25.5) / 100;
  const total = net + vat;
  return {
    id: 'draft', invoiceNumber: '', issuer, customer: { name: 'Methehe' }, companyId: 'company', periodFrom: '2026-10-01', periodTo: '2026-10-31',
    issueDate: '2026-11-01', dueDate: '2026-11-15', status: 'Draft', previewToken: `token-${ticked}`, issuerIsSample: false,
    total: { net, vat, gross: total },
    lines: [{ lineKey: 'line', shiftId: 'shift', shiftName: 'Biweekly', branchName: 'Main', hours: 2 + 2 * ticked, occurrences: 1 + ticked,
      pricingMode: 'Hourly', quantity: 2 + 2 * ticked, unitPriceNet: 50, netAmount: net, vatRatePercent: 25.5, vatAmount: vat, total,
      periodFrom: '2026-10-01', periodTo: '2026-10-31', description: 'Hourly' }],
    unstartedVisits: unstarted.map(visit => ({ ...visit, included: isIncluded(visit) })),
  };
}

async function setup(page: Page, timeZoneId = 'Europe/Helsinki') {
  await setupAdminMocks(page);
  const calls = { previews: [] as any[], issues: [] as any[] };
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = request.postData() ? request.postDataJSON() : undefined;
    if (path.endsWith('/shift-preview')) { calls.previews.push(body); return route.fulfill({ headers, json: previewFor(body) }); }
    if (path.endsWith('/invoices/shifts')) { calls.issues.push(body); return route.fulfill({ headers, json: { ...previewFor(body), id: 'invoice', invoiceNumber: 'INV-1', status: 'Issued' } }); }
    return route.fulfill({ headers, json: path.endsWith('/issuer') ? issuer : path.endsWith('/invoices') ? [] : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Methehe', isActive: true, isPartnerEnabled: false }], total: 1 } : path.endsWith('/shifts') ? [shift] : path.endsWith('/tenant') ? { timeZoneId } : [] });
  });
  return calls;
}

async function previewInvoice(page: Page) {
  await page.goto('/admin/invoices/');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Methehe', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice shifts', exact: true }).click(); await page.getByRole('option', { name: 'Biweekly', exact: true }).click(); await page.keyboard.press('Escape');
  await page.getByLabel('Invoice from', { exact: true }).fill('2026-10-01'); await page.getByLabel('Invoice to (inclusive)', { exact: true }).fill('2026-10-31');
  await page.getByRole('textbox', { name: 'Billing street', exact: true }).fill('Customer street'); await page.getByRole('textbox', { name: 'Billing postalCode', exact: true }).fill('00100'); await page.getByRole('textbox', { name: 'Billing city', exact: true }).fill('Helsinki');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
}

test('unstarted visits are unticked by default, and ticking one re-previews and issues it as billed', async ({ page }) => {
  const calls = await setup(page); await previewInvoice(page);
  const list = page.getByLabel('Unstarted visits', { exact: true });
  await expect(list.getByRole('checkbox')).toHaveCount(2);
  await expect(list.getByRole('checkbox').first()).not.toBeChecked();
  await expect(page.getByText('Net €100.00 · VAT €25.50 · Total €125.50')).toBeVisible();
  await list.getByLabel(/23 Oct 2026/).click();
  await expect(page.getByText('Net €150.00 · VAT €38.25 · Total €188.25')).toBeVisible();
  await expect(list.getByLabel(/23 Oct 2026/)).toBeChecked();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.');
  expect(calls.issues).toEqual([{ ...calls.previews.at(-1), previewToken: 'token-1' }]);
  expect(calls.issues[0].unstartedDecisions).toEqual([
    { shiftId: 'shift', startUtc: '2026-10-23T13:00:00Z', include: true },
    { shiftId: 'shift', startUtc: '2026-10-30T14:00:00Z', include: false },
  ]);
});

test('unticking a ticked visit returns the preview to the excluded total', async ({ page }) => {
  const calls = await setup(page); await previewInvoice(page);
  const list = page.getByLabel('Unstarted visits', { exact: true });
  await expect(page.getByText('Net €100.00 · VAT €25.50 · Total €125.50')).toBeVisible();
  await list.getByLabel(/30 Oct 2026/).click();
  await expect(page.getByText('Net €150.00 · VAT €38.25 · Total €188.25')).toBeVisible();
  // Choices are disabled while the refreshed preview is in flight, so wait for the list to be usable again.
  await expect(list.getByLabel(/30 Oct 2026/)).toBeEnabled();
  await list.getByLabel(/30 Oct 2026/).click();
  await expect(page.getByText('Net €100.00 · VAT €25.50 · Total €125.50')).toBeVisible();
  await expect(list.getByLabel(/30 Oct 2026/)).not.toBeChecked();
  expect(calls.previews.length).toBe(3);
});

test('visit times in the tick list use the tenant time zone', async ({ page }) => {
  // 13:00 UTC on 23 Oct is 09:00 in New York (EDT) rather than 16:00 in Helsinki.
  await setup(page, 'America/New_York'); await previewInvoice(page);
  const list = page.getByLabel('Unstarted visits', { exact: true });
  await expect(list.getByLabel(/23 Oct 2026, 09:00/)).toBeVisible();
});

test('the invoice form explains that monthly fees follow completed visits', async ({ page }) => {
  await setup(page); await page.goto('/admin/invoices/');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await expect(page.getByText(/Monthly fees are charged on completed visits/)).toBeVisible();
  await expect(page.getByText(/Monthly fees are prorated/)).toHaveCount(0);
});
