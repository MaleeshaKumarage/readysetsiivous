import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

test('company and branch can be edited in Inactive without reactivating', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const company = { id: 'company-old', businessId: 'OLD-1', name: 'Old company', contactName: 'Admin', contactEmail: 'admin@example.test', contactPhone: '123', notes: 'Old notes', isActive: false };
  const branch = { id: 'branch-old', companyId: company.id, name: 'Old branch', street: 'Old road', postalCode: '00100', city: 'Helsinki', country: 'Finland', contactPhone: '456', isActive: false };
  const updates: { path: string; body: Record<string, unknown> }[] = [];
  await page.route(/\/api\/v1\/admin\/companies(?:\/.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON(); updates.push({ path: url.pathname, body });
      if (url.pathname.endsWith('/branches/branch-old')) Object.assign(branch, body);
      else Object.assign(company, body);
      return route.fulfill({ status: 200, headers, json: url.pathname.endsWith('/branches/branch-old') ? branch : company });
    }
    if (url.pathname.endsWith('/company-old')) return route.fulfill({ headers, json: { company, branches: [branch] } });
    return route.fulfill({ headers, json: { items: [company], total: 1 } });
  });

  await page.goto('/admin/companies/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  const companyRow = page.getByRole('row').filter({ hasText: 'OLD-1' });
  await companyRow.getByRole('button', { name: 'Edit' }).click();
  const companyDialog = page.getByRole('dialog', { name: 'Edit company' });
  await companyDialog.getByLabel('Name').fill('Updated company');
  await companyDialog.getByRole('button', { name: 'Save changes' }).click();
  await expect(companyRow).toContainText('Updated company');
  await page.getByRole('button', { name: 'Branches' }).click();
  await page.getByRole('tablist', { name: 'Branch activity' }).getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByText('Old branch', { exact: true }).locator('..').getByRole('button', { name: 'Edit' }).click();
  const branchDialog = page.getByRole('dialog', { name: 'Edit branch' });
  await branchDialog.getByLabel('Name').fill('Updated branch');
  await branchDialog.getByRole('button', { name: 'Save changes' }).click();
  expect(updates).toHaveLength(2);
  expect(updates.every(update => update.body.isActive === false)).toBe(true);
  await expect(page.getByText('Updated branch', { exact: true })).toBeVisible();
});

test('inactive shift and employee edits keep their existing activity state', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const shift = { id: 'shift-old', companyId: 'company-old', branchId: 'branch-old', name: 'Old shift', isActive: false, schedule: { type: 2, weeklyDay: 1, weeklyDays: [1, 3], weeklyStart: '09:00', weeklyEnd: '11:00' }, notes: 'Old shift notes' };
  const employee = { id: 'employee-old', email: 'old@example.test', firstName: 'Old', lastName: 'Cleaner', phone: '', role: 'employee', isActive: false, skills: [], serviceAreas: [], certifications: [], defaultHours: {} };
  const changes: { path: string; body: Record<string, any> }[] = [];
  await page.route(/\/api\/v1\/admin\/(?:shifts|companies|employees|tenant|quality-cycle)(?:\/.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url()); const path = url.pathname;
    if (path.endsWith('/shifts/shift-old') && route.request().method() === 'PUT') {
      const body = route.request().postDataJSON(); changes.push({ path, body }); Object.assign(shift, body); return route.fulfill({ headers, json: shift });
    }
    if (path.endsWith('/employees/employee-old') && route.request().method() === 'PUT') {
      const body = route.request().postDataJSON(); changes.push({ path, body }); Object.assign(employee, body.fields); employee.isActive = body.isActive; return route.fulfill({ status: 204, headers });
    }
    if (path.endsWith('/shifts')) return route.fulfill({ headers, json: [shift] });
    if (path.endsWith('/companies/company-old')) return route.fulfill({ headers, json: { company: { id: 'company-old', name: 'Old company', isActive: false }, branches: [{ id: 'branch-old', companyId: 'company-old', name: 'Old branch', isActive: false }] } });
    if (path.endsWith('/companies')) return route.fulfill({ headers, json: { items: [{ id: 'company-old', name: 'Old company', isActive: false }], total: 1 } });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [employee] });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'Tenant', timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });

  await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('row').filter({ hasText: 'Old shift' }).getByRole('button', { name: 'Edit' }).click();
  const shiftDialog = page.getByRole('dialog', { name: 'Edit shift' });
  await shiftDialog.getByLabel('Name').fill('Updated shift');
  await shiftDialog.getByRole('button', { name: 'Save changes' }).click();
  expect(changes[0].body.isActive).toBe(false);
  await expect(page.getByRole('row').filter({ hasText: 'Updated shift' })).toBeVisible();

  await page.goto('/admin/employees/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('row').filter({ hasText: 'old@example.test' }).getByRole('button', { name: 'Edit' }).click();
  const employeeDialog = page.getByRole('dialog', { name: 'Edit employee' });
  await employeeDialog.getByLabel('First name').fill('Updated');
  await employeeDialog.getByRole('button', { name: 'Save changes' }).click();
  expect(changes[1].body.isActive).toBe(false);
  await expect(page.getByRole('row').filter({ hasText: 'old@example.test' })).toContainText('Updated Cleaner');
});

