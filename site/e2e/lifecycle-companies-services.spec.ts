import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET, POST, PUT, OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type, Authorization' };

test('service activation and deactivation move records between lifecycle tabs', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const services = [true, false].map((isActive, i) => ({ id: `service-${i}`, slug: `service-${i}`, name: { values: { fi: `Service ${i}` } }, description: { values: { fi: 'Description' } }, isActive, priceNet: 10, durationMinutes: 60, vatRatePercent: 25.5, category: 'cleaning', icon: 'Sparkles', sortOrder: i, isFeatured: false }));
  await page.route('**/api/v1/admin/services**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const item = services.find(s => route.request().url().includes(`/${s.id}/`))!;
      item.isActive = route.request().url().endsWith('/reactivate');
      return route.fulfill({ headers, json: {} });
    }
    return route.fulfill({ headers, json: services });
  });
  await page.goto('/admin/services/');
  await expect(page.getByRole('gridcell', { name: 'Service 0', exact: true })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: 'Service 1', exact: true })).not.toBeVisible();
  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByText('No active services.', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  const row = page.getByRole('row').filter({ has: page.getByRole('gridcell', { name: 'Service 0', exact: true }) });
  await row.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByRole('gridcell', { name: 'Service 0', exact: true })).not.toBeVisible();
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('gridcell', { name: 'Service 0', exact: true })).toBeVisible();
});

test('branch contact email persists on creation and editing after reload', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const company = { id: 'email-company', name: 'Email Company', isActive: true };
  let branch: any = null;
  const saved: any[] = [];
  await page.route('**/api/v1/admin/companies**', async route => {
    const method = route.request().method();
    const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (method === 'POST' || method === 'PUT') {
      const body = route.request().postDataJSON(); saved.push(body);
      branch = { ...body, id: 'email-branch', companyId: company.id, isActive: true };
      return route.fulfill({ headers, json: branch });
    }
    return route.fulfill({ headers, json: path.endsWith('/email-company') ? { company, branches: branch ? [branch] : [] } : { items: [company], total: 1 } });
  });
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  let dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Email Branch');
  await dialog.getByLabel('Contact email', { exact: true }).fill('branch@example.test');
  await dialog.getByRole('button', { name: 'Add branch', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(saved[0].contactEmail).toBe('branch@example.test');
  await page.reload();
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('tabpanel').getByRole('tabpanel').getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Contact email', { exact: true })).toHaveValue('branch@example.test');
  await dialog.getByLabel('Contact email', { exact: true }).fill('updated@example.test');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(saved[1].contactEmail).toBe('updated@example.test');
  await page.reload();
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('tabpanel').getByRole('tabpanel').getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('dialog').getByLabel('Contact email', { exact: true })).toHaveValue('updated@example.test');
});
