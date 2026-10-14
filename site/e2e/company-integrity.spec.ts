import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type, Authorization' };

test('company conflict retains values and permits corrected retry', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const companies: any[] = [];
  await page.route('**/api/v1/admin/companies**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'GET') return route.fulfill({ json: { items: companies, total: companies.length }, headers });
    const body = route.request().postDataJSON();
    if (body.businessId === '1234567-8') return route.fulfill({ status: 409, json: { detail: 'A company with this business ID already exists in this tenant.' }, headers });
    const company = { id: 'company-new', ...body, isActive: true };
    companies.push(company);
    return route.fulfill({ status: 201, json: company, headers });
  });
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Add company', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Business ID').fill('1234567-8');
  await dialog.getByLabel(/^Name/).fill('New company');
  await dialog.getByRole('button', { name: 'Create company', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('A company with this business ID already exists in this tenant.');
  await expect(dialog.getByLabel(/^Name/)).toHaveValue('New company');
  await expect(dialog.getByLabel('Business ID')).toHaveValue('1234567-8');
  await dialog.getByLabel('Business ID').fill('7654321-0');
  await dialog.getByRole('button', { name: 'Create company', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('cell', { name: 'New company', exact: true })).toBeVisible();
});

test('branch parent failure is visible and retains values', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const company = { id: 'company-existing', name: 'Existing company', businessId: '1234567-8', isActive: true };
  await page.route('**/api/v1/admin/companies**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') return route.fulfill({ status: 404, json: { detail: 'Company not found.' }, headers });
    return route.fulfill({ json: route.request().url().includes('/company-existing') ? { company, branches: [] } : { items: [company], total: 1 }, headers });
  });
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Branches' }).click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Name/).fill('Corridor');
  await dialog.getByRole('button', { name: 'Add branch', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Company not found.');
  await expect(dialog.getByLabel(/^Name/)).toHaveValue('Corridor');
  await expect(dialog.getByRole('button', { name: 'Add branch', exact: true })).toBeEnabled();
});
