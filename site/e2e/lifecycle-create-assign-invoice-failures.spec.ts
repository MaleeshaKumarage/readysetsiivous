import { selectDailyScheduleIfEmpty } from './admin-helpers';
import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { shiftAction } from './shift-actions';
import { autoShiftName } from '../src/lib/shiftName';
const FLOW_NAME = autoShiftName(undefined);

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'supplier@example.test', phone: '', bankAccountIBAN: 'FI2112345600000785', bankBic: 'NDEAFIHH', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
const company = { id: 'company', name: 'Methehe Oy', businessId: '1234567-8', isActive: true, isPartnerEnabled: false };
const branch = { id: 'branch', companyId: 'company', name: 'Main branch', isActive: true };
const alice = { id: 'alice', firstName: 'Alice', lastName: 'Cleaner', role: 'employee', isActive: true };

type Failures = { createShift?: string; assign?: boolean; preview?: string; issue?: string; seedShift?: boolean };

// Shared mock state with one-shot failures: each failure applies to the next matching request only, so retries succeed.
async function setup(page: Page, failures: Failures = {}) {
  await setupAdminMocks(page);
  // Invoice-only flows start with the shift already created, as a real tenant would have it.
  const seeded = failures.seedShift ? [{ id: 'shift-1', name: 'Flow clean', companyId: 'company', branchId: 'branch', isActive: true }] : [];
  const state = { shifts: seeded as any[], assignments: [] as string[], issues: [] as any[], creates: 0, assigns: 0 };
  const pending = { ...failures };
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const url = request.url(); const path = new URL(url).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/assignment-counts')) return route.fulfill({ headers, json: Object.fromEntries(state.shifts.map(s => [s.id, state.assignments.length])) });
    if (path.includes('/assignments')) {
      if (method === 'GET') return route.fulfill({ headers, json: state.assignments.map(id => ({ id: 'a-' + id, employeeId: id, isActive: true })) });
      state.assigns++;
      if (pending.assign) { pending.assign = false; return route.fulfill({ status: 500, headers, json: { detail: 'Internal diagnostic that should not be displayed' } }); }
      const body = request.postDataJSON(); state.assignments.push(body.employeeId);
      return route.fulfill({ status: 200, headers, json: { id: 'a-' + body.employeeId, employeeId: body.employeeId, isActive: true } });
    }
    if (isShiftsRoute(path) && method === 'GET') return route.fulfill({ headers, json: shiftsResponse(state.shifts, url) });
    if (path === '/api/v1/admin/shifts' && method === 'POST') {
      state.creates++;
      if (pending.createShift) { const detail = pending.createShift; pending.createShift = undefined; return route.fulfill({ status: 409, headers, json: { detail } }); }
      const body = request.postDataJSON(); const created = { id: 'shift-' + (state.shifts.length + 1), isActive: true, ...body };
      state.shifts.push(created); return route.fulfill({ status: 201, headers, json: created });
    }
    if (path.endsWith('/shift-preview')) {
      if (pending.preview) { const detail = pending.preview; pending.preview = undefined; return route.fulfill({ status: 409, headers, json: { detail } }); }
      const input = request.postDataJSON(); const included = (input.unstartedDecisions ?? []).some((d: any) => d.include);
      const net = 100 + (included ? 50 : 0); const vat = Math.round(net * 25.5) / 100;
      return route.fulfill({ headers, json: { id: 'draft', invoiceNumber: '', issuer, customer: { name: company.name }, companyId: 'company', periodFrom: input.from, periodTo: input.to,
        issueDate: '2026-11-07', dueDate: '2026-11-21', status: 'Draft', previewToken: `token-${included ? 1 : 0}`, issuerIsSample: false,
        total: { net, vat, gross: net + vat }, lines: [{ lineKey: 'line', shiftId: input.shiftIds[0], shiftName: 'Flow clean', branchName: 'Main branch', hours: included ? 2 : 0, occurrences: included ? 1 : 0,
          pricingMode: 'Hourly', quantity: included ? 2 : 0, unitPriceNet: 50, netAmount: net, vatRatePercent: 25.5, vatAmount: vat, total: net + vat, periodFrom: '2026-11-01', periodTo: '2026-11-30', description: 'Hourly' }],
        unstartedVisits: [{ shiftId: input.shiftIds[0], shiftName: 'Flow clean', startUtc: '2026-11-06T14:00:00Z', endUtc: '2026-11-06T16:00:00Z', included }] } });
    }
    if (path.endsWith('/invoices/shifts')) {
      const input = request.postDataJSON();
      if (pending.issue) { const detail = pending.issue; pending.issue = undefined; return route.fulfill({ status: 409, headers, json: { detail } }); }
      state.issues.push(input); return route.fulfill({ status: 201, headers, json: { id: 'invoice', invoiceNumber: 'INV-1', status: 'Issued', customer: { name: company.name }, issuer, issueDate: '2026-11-07', dueDate: '2026-11-21', total: { net: 150, vat: 38.25, gross: 188.25 }, lines: [] } });
    }
    if (path.endsWith('/companies/company')) return route.fulfill({ headers, json: { company, branches: [branch] } });
    if (path.endsWith('/companies')) return route.fulfill({ headers, json: { items: [company], total: 1 } });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [alice] });
    if (path.endsWith('/issuer')) return route.fulfill({ headers, json: issuer });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  return state;
}

async function fillShiftDialog(page: Page) {
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'Methehe Oy', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'Main branch', exact: true }).click();
  return dialog;
}

async function fillInvoice(page: Page) {
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Methehe Oy', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice shifts', exact: true }).click(); await page.getByRole('option', { name: 'Flow clean', exact: true }).click(); await page.keyboard.press('Escape');
  await page.getByLabel('Invoice from', { exact: true }).fill('2026-11-01'); await page.getByLabel('Invoice to (inclusive)', { exact: true }).fill('2026-11-30');
  await page.getByRole('textbox', { name: 'Billing street', exact: true }).fill('Customer street');
  await page.getByRole('textbox', { name: 'Billing postalCode', exact: true }).fill('00100');
  await page.getByRole('textbox', { name: 'Billing city', exact: true }).fill('Helsinki');
}

test('a refused shift save shows the reason in the dialog, keeps the company and branch, and a retry creates the shift', async ({ page }) => {
  const state = await setup(page, { createShift: 'A shift named Flow clean already exists for this branch. Rename it and save again.' });
  await page.goto('/admin/shifts/');
  const dialog = await fillShiftDialog(page);
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByText('Shift not created').first()).toBeVisible();
  await expect(dialog.getByText('A shift named Flow clean already exists for this branch.')).toBeVisible();
  // The chosen company and branch are kept for the retry.
  await expect(dialog.getByLabel('Company', { exact: true })).toHaveValue('Methehe Oy');
  await expect(dialog.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue(/.+/);
  await expect(dialog.getByLabel('Branch', { exact: true })).toHaveValue('Main branch');
  await expect(page.getByRole('gridcell', { name: FLOW_NAME, exact: true })).toHaveCount(0);
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('gridcell', { name: FLOW_NAME, exact: true })).toBeVisible();
  expect(state.creates).toBe(2);
});

test('a failed assignment shows an assignment error, nothing is assigned, and a retry assigns the cleaner', async ({ page }) => {
  const state = await setup(page, { assign: true });
  await page.goto('/admin/shifts/');
  const dialog = await fillShiftDialog(page);
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('gridcell', { name: FLOW_NAME, exact: true })).toBeVisible();
  await shiftAction(page, FLOW_NAME, 'Assign employees');
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).click();
  await page.getByRole('option', { name: 'Alice Cleaner', exact: true }).click();
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).press('Escape');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('alert', { name: 'Assignment error' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner', exact: true })).toBeVisible();
  expect(state.assignments).toEqual(['alice']);
});

test('a refused invoice preview shows the billing reason, and a retry previews and issues the invoice', async ({ page }) => {
  const state = await setup(page, { seedShift: true, preview: 'No pricing is configured for this billing period. Add a price for October in the shift pricing and try again.' });
  await page.goto('/admin/invoices/');
  await fillInvoice(page);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('No pricing is configured for this billing period.');
  await expect(page.getByRole('button', { name: 'Issue invoice', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.');
  expect(state.issues).toHaveLength(1);
});

test('a stale invoice issue is refused with a reload message, and a new preview lets the admin issue', async ({ page }) => {
  const state = await setup(page, { seedShift: true, issue: 'Invoice inputs, pricing or work records changed. Load a new preview before issuing.' });
  await page.goto('/admin/invoices/');
  await fillInvoice(page);
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Invoice preview' })).toBeVisible();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  // Scoped to the preview: the closing form dialog can briefly keep a copy of the same message.
  await expect(page.getByRole('dialog', { name: 'Invoice preview', exact: true }).getByRole('alert')).toContainText('Load a new preview before issuing.');
  expect(state.issues).toHaveLength(0);
  // Closing the preview returns to the form with its values kept, so the admin can preview again without retyping.
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Invoice shifts', exact: true })).toBeVisible();
  await expect(page.getByLabel('Invoice from', { exact: true })).toHaveValue('2026-11-01');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.');
  expect(state.issues).toHaveLength(1);
});
