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

