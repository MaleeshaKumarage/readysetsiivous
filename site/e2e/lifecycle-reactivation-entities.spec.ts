import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('company and branch reactivation preserves metadata and independent lifecycle', async ({ page }) => {
  await setupAdminMocks(page);
  const company = { id: 'company', businessId: '123', name: 'Archived client', contactName: 'Contact', contactEmail: 'contact@example.com', notes: 'Keep notes', isActive: false };
  const branch = { id: 'branch', companyId: company.id, name: 'Archived office', street: 'Keep street', city: 'Helsinki', isActive: false };
  const commands: string[] = [];
  await page.route(/\/api\/v1\/admin\/companies(?:.*)?$/, async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (req.method() === 'POST') {
      expect(req.postData()).toBeNull(); commands.push(path);
      if (path.includes('/branches/')) branch.isActive = path.endsWith('/reactivate');
      else company.isActive = path.endsWith('/reactivate');
      return route.fulfill({ headers, status: 204 });
    }
    return route.fulfill({ headers, json: path.endsWith('/companies') ? { items: [company], total: 1 } : { company, branches: [branch] } });
  });
  await page.goto('/admin/companies/');
  const companyTabs = page.getByRole('tablist', { name: 'Company activity' });
  await companyTabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  const branchTabs = page.getByRole('tablist', { name: 'Branch activity' });
  await branchTabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await page.getByText('Archived office', { exact: false }).locator('..').getByRole('button', { name: 'Reactivate' }).click();
  await expect(branchTabs.getByRole('tab', { name: 'Inactive (0)', exact: true })).toBeVisible();
  expect(company.isActive).toBe(false); expect(branch.street).toBe('Keep street');
  await page.getByRole('row').filter({ has: page.getByText('Archived client', { exact: true }) }).first().getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(companyTabs.getByRole('tab', { name: 'Inactive (0)', exact: true })).toBeVisible();
  expect(company.notes).toBe('Keep notes'); expect(branch.isActive).toBe(true);
  expect(commands).toEqual(['/api/v1/admin/companies/branches/branch/reactivate', '/api/v1/admin/companies/company/reactivate']);
});

test('checklist reactivation failure settles and retry preserves scope and items', async ({ page }) => {
  await setupAdminMocks(page);
  const template = { id: 'template', title: 'Archived checklist', description: 'Keep description', companyId: 'company', branchId: 'branch', items: ['Floor', 'Windows'], isActive: false, createdUtc: '2026-10-14T08:00:00Z', updatedUtc: '2026-10-14T08:00:00Z' };
  let attempts = 0;
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/templates(?:.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (route.request().method() === 'POST') {
      expect(route.request().postData()).toBeNull(); attempts++;
      if (attempts === 1) return route.fulfill({ headers, status: 503, json: { detail: 'Temporarily unavailable. Please retry.' } });
      template.isActive = new URL(route.request().url()).pathname.endsWith('/reactivate');
      return route.fulfill({ headers, status: 204 });
    }
    return route.fulfill({ headers, json: [template] });
  });
  await page.goto('/admin/quality-cycle/');
  const tabs = page.getByRole('tablist', { name: 'Checklist template activity' });
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toHaveText('Temporarily unavailable. Please retry.');
  await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(tabs.getByRole('tab', { name: 'Inactive (0)', exact: true })).toBeVisible();
  await tabs.getByRole('tab', { name: 'Active (1)', exact: true }).click();
  await expect(page.getByText('Archived checklist', { exact: true })).toBeVisible();
  expect(template.items).toEqual(['Floor', 'Windows']); expect(template.branchId).toBe('branch');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(tabs.getByRole('tab', { name: 'Active (0)', exact: true })).toBeVisible();
});
