import { selectDailyScheduleIfEmpty } from './admin-helpers';
import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { shiftAction } from './shift-actions';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'supplier@example.test', phone: '', bankAccountIBAN: 'FI2112345600000785', bankBic: 'NDEAFIHH', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
const company = { id: 'company', name: 'Methehe Oy', businessId: '1234567-8', isActive: true, isPartnerEnabled: false };
const branch = { id: 'branch', companyId: 'company', name: 'Main branch', isActive: true };
const alice = { id: 'alice', firstName: 'Alice', lastName: 'Cleaner', role: 'employee', isActive: true };

// One shared state for the whole flow: a shift created in the dialog is what the assignment and invoice steps use.
function flowState() {
  return { shifts: [] as any[], assignments: [] as string[], shiftWrites: [] as any[], assignWrites: [] as string[], issues: [] as any[], previews: [] as any[] };
}

function previewFor(input: any, shifts: any[]) {
  const decisions: { shiftId: string; startUtc: string; include: boolean }[] = input?.unstartedDecisions ?? [];
  const visit = { shiftId: input?.shiftIds?.[0], startUtc: '2026-11-06T14:00:00Z', endUtc: '2026-11-06T16:00:00Z' };
  const included = decisions.some(d => d.shiftId === visit.shiftId && d.startUtc === visit.startUtc && d.include);
  // The only scheduled visit is unstarted, so nothing bills until it is ticked; the preview has no lines before that.
  const net = included ? 50 : 0;
  const vat = Math.round(net * 25.5) / 100;
  const name = shifts.find(s => s.id === visit.shiftId)?.name ?? 'Shift';
  return { id: 'draft', invoiceNumber: '', issuer, customer: { name: company.name }, companyId: 'company', periodFrom: input?.from, periodTo: input?.to,
    issueDate: '2026-11-07', dueDate: '2026-11-21', status: 'Draft', previewToken: `token-${included ? 1 : 0}`, issuerIsSample: false,
    total: { net, vat, gross: net + vat },
    lines: included ? [{ lineKey: 'line', shiftId: visit.shiftId, shiftName: name, branchName: 'Main branch', hours: 2, occurrences: 1,
      pricingMode: 'Hourly', quantity: 2, unitPriceNet: 50, netAmount: net, vatRatePercent: 25.5, vatAmount: vat, total: net + vat,
      periodFrom: '2026-11-01', periodTo: '2026-11-30', description: 'Hourly' }] : [],
    unstartedVisits: [{ ...visit, shiftName: name, included }] };
}

async function setup(page: Page) {
  await setupAdminMocks(page);
  const state = flowState();
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const url = request.url(); const path = new URL(url).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/assignment-counts')) return route.fulfill({ headers, json: Object.fromEntries(state.shifts.map(s => [s.id, state.assignments.length])) });
    if (path.includes('/assignments')) {
      if (method === 'GET') return route.fulfill({ headers, json: state.assignments.map(id => ({ id: 'a-' + id, employeeId: id, isActive: true })) });
      const body = request.postDataJSON(); state.assignWrites.push(body.employeeId); state.assignments.push(body.employeeId);
      return route.fulfill({ status: 200, headers, json: { id: 'a-' + body.employeeId, employeeId: body.employeeId, isActive: true } });
    }
    if (isShiftsRoute(path) && method === 'GET') return route.fulfill({ headers, json: shiftsResponse(state.shifts, url) });
    if (path === '/api/v1/admin/shifts' && method === 'POST') {
      const body = request.postDataJSON(); state.shiftWrites.push(body);
      const created = { id: 'shift-' + (state.shifts.length + 1), isActive: true, ...body };
      state.shifts.push(created);
      return route.fulfill({ status: 201, headers, json: created });
    }
    if (path.endsWith('/shift-preview')) { const input = request.postDataJSON(); state.previews.push(input); return route.fulfill({ headers, json: previewFor(input, state.shifts) }); }
    if (path.endsWith('/invoices/shifts')) { const input = request.postDataJSON(); state.issues.push(input); return route.fulfill({ status: 201, headers, json: { ...previewFor(input, state.shifts), id: 'invoice', invoiceNumber: 'INV-1', status: 'Issued' } }); }
    if (path.endsWith('/companies/company')) return route.fulfill({ headers, json: { company, branches: [branch] } });
    if (path.endsWith('/companies')) return route.fulfill({ headers, json: { items: [company], total: 1 } });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [alice] });
    if (path.endsWith('/issuer')) return route.fulfill({ headers, json: issuer });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  return state;
}

test('admin creates a shift, assigns a cleaner, and invoices its unstarted visit after ticking it in', async ({ page }) => {
  const state = await setup(page);

  // 1. Create the shift.
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'Methehe Oy', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'Main branch', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Flow clean');
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('cell', { name: 'Flow clean', exact: true })).toBeVisible();
  expect(state.shiftWrites).toHaveLength(1); expect(state.shiftWrites[0]).toMatchObject({ companyId: 'company', branchId: 'branch', name: 'Flow clean' });

  // 2. Assign a cleaner from the row's More actions.
  await shiftAction(page, 'Flow clean', 'Employees');
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).click();
  await page.getByRole('option', { name: 'Alice Cleaner', exact: true }).click();
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).press('Escape');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner', exact: true })).toBeVisible();
  expect(state.assignWrites).toEqual(['alice']);

  // 3. Invoice the shift. The visit has no started work, so it is listed unticked and not billed.
  await page.goto('/admin/invoices/');
  await page.getByRole('button', { name: 'New invoice', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company', exact: true }).click(); await page.getByRole('option', { name: 'Methehe Oy', exact: true }).click();
  await page.getByRole('textbox', { name: 'Invoice shifts', exact: true }).click(); await page.getByRole('option', { name: 'Flow clean', exact: true }).click(); await page.keyboard.press('Escape');
  await page.getByLabel('Invoice from', { exact: true }).fill('2026-11-01'); await page.getByLabel('Invoice to (inclusive)', { exact: true }).fill('2026-11-30');
  await page.getByRole('textbox', { name: 'Billing street', exact: true }).fill('Customer street');
  await page.getByRole('textbox', { name: 'Billing postalCode', exact: true }).fill('00100');
  await page.getByRole('textbox', { name: 'Billing city', exact: true }).fill('Helsinki');
  await page.getByRole('button', { name: 'Preview invoice', exact: true }).click();
  const list = page.getByLabel('Unstarted visits', { exact: true });
  await expect(list.getByRole('checkbox')).toHaveCount(1);
  await expect(list.getByRole('checkbox')).not.toBeChecked();
  await expect(page.getByText('Net €0.00 · VAT €0.00 · Total €0.00')).toBeVisible();

  // 4. Tick it in: the preview refreshes and the issued invoice carries the choice.
  await list.getByRole('checkbox').click();
  await expect(page.getByText('Net €50.00 · VAT €12.75 · Total €62.75')).toBeVisible();
  await page.getByRole('button', { name: 'Issue invoice', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Invoice INV-1 issued.');
  expect(state.issues).toHaveLength(1);
  expect(state.issues[0].unstartedDecisions).toEqual([{ shiftId: 'shift-1', startUtc: '2026-11-06T14:00:00Z', include: true }]);
  expect(state.issues[0].previewToken).toBe('token-1');
});
