import { test, expect, type Page, type Route } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const SHIFT_REASON = 'This company still has 2 active shift(s). Deactivate or move them first, then deactivate the company.';
const HISTORY_REASON = 'This company has paid invoices. Paid invoices are kept as the record of what was paid; issue a credit note for any correction, then clear again.';
const BRANCH_REASON = "Delete or move the company's branches before deleting it.";
const CLEAR_BUTTON = 'Delete related shifts, unassign employees and clear all places';
const DIALOG = 'Clear related shifts and places?';
const PARTNER_REASON = 'This company has partner request history and must be retained.';

const activeCompany = { id: 'company-a', businessId: '1234567-8', name: 'Busy Oy', contactName: '', contactEmail: '', contactPhone: '', notes: '', isActive: true, isPartnerEnabled: false };
const inactiveCompany = { ...activeCompany, isActive: false };

async function routeCompanies(page: Page, company: typeof activeCompany) {
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, json: { items: [company], total: 1 } });
  });
}

test('clearing a refused deactivation deletes the shifts and places, then deactivates the company', async ({ page }) => {
  await setupAdminMocks(page);
  const clearCalls: string[] = [];
  const clearUrls: string[] = [];
  let deactivateCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/deactivate$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    deactivateCalls++;
    if (deactivateCalls === 1) return route.fulfill({ headers, status: 409, json: { title: 'Shift conflict', detail: SHIFT_REASON } });
    return route.fulfill({ headers, status: 204 });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearCalls.push(route.request().method());
    clearUrls.push(route.request().url());
    return route.fulfill({ headers, json: { shiftsDeleted: 2, employeesUnassigned: 3, branchesDeleted: 1 } });
  });
  await routeCompanies(page, activeCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText(SHIFT_REASON)).toBeVisible();
  await dialog.getByRole('button', { name: CLEAR_BUTTON }).click();
  await expect(dialog).toBeHidden();
  expect(clearCalls).toEqual(['POST']);
  expect(deactivateCalls).toBe(2);
});

test('skip deletion leaves the shifts, places and the company untouched', async ({ page }) => {
  await setupAdminMocks(page);
  const clearCalls: string[] = [];
  let deactivateCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/deactivate$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    deactivateCalls++;
    return route.fulfill({ headers, status: 409, json: { title: 'Shift conflict', detail: SHIFT_REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearCalls.push(route.request().method());
    return route.fulfill({ headers, json: { shiftsDeleted: 0, employeesUnassigned: 0, branchesDeleted: 0 } });
  });
  await routeCompanies(page, activeCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Skip deletion' }).click();
  await expect(dialog).toBeHidden();
  expect(clearCalls).toEqual([]);
  expect(deactivateCalls).toBe(1);
  await expect(page.getByRole('button', { name: 'Deactivate', exact: true }).first()).toBeVisible();
});

test('when the clear is refused for paid invoices nothing changes and the reason is shown', async ({ page }) => {
  await setupAdminMocks(page);
  let deactivateCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/deactivate$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    deactivateCalls++;
    return route.fulfill({ headers, status: 409, json: { title: 'Shift conflict', detail: SHIFT_REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, status: 409, json: { title: 'Delete conflict', detail: HISTORY_REASON } });
  });
  await routeCompanies(page, activeCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await dialog.getByRole('button', { name: CLEAR_BUTTON }).click();
  await expect(dialog.getByRole('alert').filter({ hasText: HISTORY_REASON })).toBeVisible();
  await expect(dialog).toBeVisible();
  expect(deactivateCalls).toBe(1);
});

const SUMMARY = { branches: 1, shifts: 2, shiftsWithChecklists: 1, invoices: 3, paidInvoices: 1, agreements: 1, checklistSubmissions: 4, partnerRequests: 2, partnerAccounts: 1, templates: 1, leads: 1 };
const REMOVED = { shiftsRemoved: 2, placesRemoved: 1, invoicesRemoved: 0, companyRemoved: false };

// Routes the removal summary and the removal request. `bodies` collects each removal request body.
async function routeRemoval(page: Page, bodies: unknown[], removeResponse?: (route: Route) => unknown) {
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/removal-summary(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, json: SUMMARY });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/remove$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    bodies.push(route.request().postDataJSON());
    if (removeResponse) return removeResponse(route);
    return route.fulfill({ headers, json: REMOVED });
  });
}

// Routes a delete that is refused with `reason`, so the removal dialog opens.
async function routeRefusedDelete(page: Page, reason: string) {
  let deleteCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (route.request().method() !== 'DELETE') return route.fallback();
    deleteCalls++;
    return route.fulfill({ headers, status: 409, json: { title: 'Record cannot be deleted', detail: reason } });
  });
  return () => deleteCalls;
}

async function openRemovalFromDelete(page: Page) {
  page.once('dialog', dialog => void dialog.accept());
  await page.getByRole('tab', { name: /inactive/i }).click();
  await page.getByRole('button', { name: 'Delete', exact: true }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Remove company' });
  await expect(dialog).toBeVisible();
  return dialog;
}

test('a refused delete opens the removal dialog with what it will remove', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  const bodies: unknown[] = [];
  await routeRemoval(page, bodies);
  await routeRefusedDelete(page, BRANCH_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  await expect(dialog.getByText(BRANCH_REASON)).toBeVisible();
  await expect(dialog.getByRole('checkbox', { name: /^Places \(1\)/ })).toBeVisible();
  await expect(dialog.getByRole('checkbox', { name: /^Shifts \(2, 1 with completed checklists\)/ })).toBeVisible();
  await expect(dialog.getByRole('checkbox', { name: /^Invoices \(3, 1 paid\)/ })).toBeVisible();
  expect(bodies).toEqual([]);
});

test('places need their shifts: ticking places ticks shifts, unticking shifts unticks places', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  await routeRemoval(page, []);
  await routeRefusedDelete(page, BRANCH_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  const places = dialog.getByRole('checkbox', { name: /^Places/ });
  const shifts = dialog.getByRole('checkbox', { name: /^Shifts/ });
  await places.check();
  await expect(shifts).toBeChecked();
  await shifts.uncheck();
  await expect(places).not.toBeChecked();
});

test('ticking places and shifts removes those and keeps the company and its invoices', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  const bodies: unknown[] = [];
  await routeRemoval(page, bodies);
  await routeRefusedDelete(page, BRANCH_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  await dialog.getByRole('checkbox', { name: /^Places/ }).check();
  await dialog.getByRole('button', { name: 'Remove selected' }).click();
  await expect(dialog).toBeHidden();
  expect(bodies).toEqual([{ branches: true, shifts: true, invoices: false, confirmPaidInvoices: false }]);
});

test('ticking all three shows what else goes, and paid invoices need the company name typed first', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  const bodies: unknown[] = [];
  await routeRemoval(page, bodies);
  await routeRefusedDelete(page, BRANCH_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  await dialog.getByRole('checkbox', { name: /^Places/ }).check();
  await dialog.getByRole('checkbox', { name: /^Invoices/ }).check();
  await expect(dialog.getByText(/also removes the company record/)).toBeVisible();
  await expect(dialog.getByText(/1 signed agreement/)).toBeVisible();
  const remove = dialog.getByRole('button', { name: 'Remove selected' });
  const name = dialog.getByLabel(/Type “Busy Oy” to confirm removing 1 paid invoice/);
  await expect(remove).toBeDisabled();
  await name.fill('Busy');
  await expect(remove).toBeDisabled();
  await name.fill('Busy Oy');
  await expect(remove).toBeEnabled();
  await remove.click();
  await expect(dialog).toBeHidden();
  expect(bodies).toEqual([{ branches: true, shifts: true, invoices: true, confirmPaidInvoices: true }]);
});

test('skip on a refused delete changes nothing and the company stays', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  const bodies: unknown[] = [];
  await routeRemoval(page, bodies);
  const deleteCalls = await routeRefusedDelete(page, BRANCH_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(dialog).toBeHidden();
  expect(bodies).toEqual([]);
  expect(deleteCalls()).toBe(1);
  await expect(page.getByRole('button', { name: 'Delete', exact: true }).first()).toBeVisible();
});

test('a refused removal shows the reason and keeps the dialog open', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  const bodies: unknown[] = [];
  await routeRemoval(page, bodies, route => route.fulfill({ headers, status: 409, json: { title: 'Record cannot be deleted', detail: HISTORY_REASON } }));
  await routeRefusedDelete(page, BRANCH_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  await dialog.getByRole('checkbox', { name: /^Places/ }).check();
  await dialog.getByRole('button', { name: 'Remove selected' }).click();
  await expect(dialog.getByRole('alert').filter({ hasText: HISTORY_REASON })).toBeVisible();
  await expect(dialog).toBeVisible();
  expect(bodies).toHaveLength(1);
});

test('a partner refusal on delete also opens the removal dialog, which names the partner accounts', async ({ page }) => {
  await setupAdminMocks(page);
  await routeCompanies(page, inactiveCompany);
  await routeRemoval(page, []);
  await routeRefusedDelete(page, PARTNER_REASON);
  await page.goto('/admin/companies/');

  const dialog = await openRemovalFromDelete(page);
  await expect(dialog.getByText(PARTNER_REASON)).toBeVisible();
  await dialog.getByRole('checkbox', { name: /^Places/ }).check();
  await dialog.getByRole('checkbox', { name: /^Invoices/ }).check();
  await expect(dialog.getByText(/1 partner account\(s\) are deactivated and their login is disabled/)).toBeVisible();
});

test('the signed-agreements box is off by default and sends its choice with the clear', async ({ page }) => {
  await setupAdminMocks(page);
  const clearUrls: string[] = [];
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/deactivate$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, status: 409, json: { title: 'Shift conflict', detail: SHIFT_REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearUrls.push(route.request().url());
    return route.fulfill({ headers, json: { shiftsDeleted: 1, employeesUnassigned: 0, branchesDeleted: 0, invoicesVoided: 0, partnerRecordsDeleted: 0, agreementsDeleted: 0 } });
  });
  await routeCompanies(page, activeCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await expect(dialog.getByRole('checkbox', { name: 'Also delete this company\'s signed agreements' })).not.toBeChecked();
  await dialog.getByRole('checkbox', { name: 'Also delete this company\'s signed agreements' }).check();
  await dialog.getByRole('button', { name: CLEAR_BUTTON }).click();
  await expect(dialog).toBeHidden();
  expect(clearUrls).toHaveLength(1);
  expect(new URL(clearUrls[0]).searchParams.get('deleteSignedAgreements')).toBe('true');
});
