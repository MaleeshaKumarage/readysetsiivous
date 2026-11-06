import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const SHIFT_REASON = 'This company still has 2 active shift(s). Deactivate or move them first, then deactivate the company.';
const HISTORY_REASON = 'This company has paid invoices. Paid invoices are kept as the record of what was paid; issue a credit note for any correction, then clear again.';
const BRANCH_REASON = "Delete or move the company's branches before deleting it.";
const CLEAR_BUTTON = 'Delete related shifts, unassign employees and clear all places';
const DIALOG = 'Clear related shifts and places?';

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

test('a refused delete of an inactive company can clear its places first, then delete the company', async ({ page }) => {
  await setupAdminMocks(page);
  const clearCalls: string[] = [];
  let deleteCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (route.request().method() !== 'DELETE') return route.fallback();
    deleteCalls++;
    if (deleteCalls === 1) return route.fulfill({ headers, status: 409, json: { title: 'Delete conflict', detail: BRANCH_REASON } });
    return route.fulfill({ headers, status: 204 });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearCalls.push(route.request().method());
    return route.fulfill({ headers, json: { shiftsDeleted: 0, employeesUnassigned: 0, branchesDeleted: 1 } });
  });
  await routeCompanies(page, inactiveCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('tab', { name: /inactive/i }).click();
  page.once('dialog', dialog => void dialog.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: CLEAR_BUTTON }).click();
  await expect(dialog).toBeHidden();
  expect(clearCalls).toEqual(['POST']);
  expect(deleteCalls).toBe(2);
});

const PARTNER_REASON = 'This company has partner request history and must be retained.';

test('skip deletion on a refused delete leaves the company, its shifts and places untouched', async ({ page }) => {
  await setupAdminMocks(page);
  const clearCalls: string[] = [];
  let deleteCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (route.request().method() !== 'DELETE') return route.fallback();
    deleteCalls++;
    return route.fulfill({ headers, status: 409, json: { title: 'Delete conflict', detail: BRANCH_REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearCalls.push(route.request().method());
    return route.fulfill({ headers, json: { shiftsDeleted: 0, employeesUnassigned: 0, branchesDeleted: 0 } });
  });
  await routeCompanies(page, inactiveCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('tab', { name: /inactive/i }).click();
  page.once('dialog', dialog => void dialog.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Skip deletion' }).click();
  await expect(dialog).toBeHidden();
  expect(clearCalls).toEqual([]);
  expect(deleteCalls).toBe(1);
  await expect(page.getByRole('button', { name: 'Delete', exact: true }).first()).toBeVisible();
});

test('a refused clear on the delete path shows the reason and does not retry the delete', async ({ page }) => {
  await setupAdminMocks(page);
  const clearCalls: string[] = [];
  let deleteCalls = 0;
  await page.route(/\/api\/v1\/admin\/companies\/company-a$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (route.request().method() !== 'DELETE') return route.fallback();
    deleteCalls++;
    return route.fulfill({ headers, status: 409, json: { title: 'Delete conflict', detail: BRANCH_REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearCalls.push(route.request().method());
    return route.fulfill({ headers, status: 409, json: { title: 'Delete conflict', detail: HISTORY_REASON } });
  });
  await routeCompanies(page, inactiveCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('tab', { name: /inactive/i }).click();
  page.once('dialog', dialog => void dialog.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).first().click();

  const dialog = page.getByRole('dialog', { name: DIALOG });
  await dialog.getByRole('button', { name: CLEAR_BUTTON }).click();
  await expect(dialog.getByRole('alert').filter({ hasText: HISTORY_REASON })).toBeVisible();
  expect(clearCalls).toEqual(['POST']);
  expect(deleteCalls).toBe(1);
});

test('a partner refusal on delete shows only the reason and no clear prompt', async ({ page }) => {
  await setupAdminMocks(page);
  const clearCalls: string[] = [];
  await page.route(/\/api\/v1\/admin\/companies\/company-a$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (route.request().method() !== 'DELETE') return route.fallback();
    return route.fulfill({ headers, status: 409, json: { title: 'Delete conflict', detail: PARTNER_REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/clear-related(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    clearCalls.push(route.request().method());
    return route.fulfill({ headers, json: { shiftsDeleted: 0, employeesUnassigned: 0, branchesDeleted: 0 } });
  });
  await routeCompanies(page, inactiveCompany);
  await page.goto('/admin/companies/');
  await page.getByRole('tab', { name: /inactive/i }).click();
  page.once('dialog', dialog => void dialog.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).first().click();

  await expect(page.getByRole('alert').filter({ hasText: PARTNER_REASON }).first()).toBeVisible();
  await expect(page.getByRole('dialog', { name: DIALOG })).toBeHidden();
  expect(clearCalls).toEqual([]);
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
