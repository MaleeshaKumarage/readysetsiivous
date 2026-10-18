import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET, POST, PUT, OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type, Authorization' };
const companies = Array.from({ length: 101 }, (_, index) => ({ id: `company-${index}`, name: `Company ${index}`, businessId: `ID-${index}`, isActive: index < 100 }));

test('company tabs include records after page100 and branch tabs stay scoped', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const requests: number[] = [];
  await page.route('**/api/v1/admin/companies**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/company-100')) return route.fulfill({ headers, json: { company: companies[100], branches: [
      { id: 'branch-active', companyId: 'company-100', name: 'Active branch', isActive: true },
      { id: 'branch-inactive', companyId: 'company-100', name: 'Inactive branch', isActive: false },
    ] } });
    const skip = Number(url.searchParams.get('skip') || 0); requests.push(skip);
    return route.fulfill({ headers, json: { items: companies.slice(skip, skip + 100), total: companies.length } });
  });
  await page.goto('/admin/companies/');
  const tabs = page.getByRole('tablist', { name: 'Company activity' });
  await expect(tabs.getByRole('tab', { name: 'Active (100)', exact: true })).toHaveAttribute('aria-selected', 'true');
  const listRequestsBeforeTabs = [...requests];
  await expect(page.getByRole('cell', { name: 'Company 100', exact: true })).not.toBeVisible();
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Company 100', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  const branchTabs = page.getByRole('tablist', { name: 'Branch activity' });
  await expect(page.getByText('Active branch', { exact: true })).toBeVisible();
  await tabs.getByRole('tab', { name: /^Inactive/ }).press('End');
  await expect(branchTabs).toBeVisible();
  await tabs.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(branchTabs).toBeVisible();
  await expect(page.getByText('Inactive branch', { exact: true })).not.toBeVisible();
  await branchTabs.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByText('Inactive branch', { exact: true })).toBeVisible();
  await expect(page.getByText('Active branch', { exact: true })).not.toBeVisible();
  await tabs.getByRole('tab', { name: /^Active/ }).click();
  await expect(branchTabs).not.toBeVisible();
  expect(requests).toEqual(listRequestsBeforeTabs);
  expect(requests).toContain(0);
  expect(requests).toContain(100);
});

test('incomplete company pagination shows an error and refresh retries fresh pages', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let fail = true;
  const requests: number[] = [];
  await page.route('**/api/v1/admin/companies**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const skip = Number(new URL(route.request().url()).searchParams.get('skip') || 0); requests.push(skip);
    if (skip === 100 && fail) return route.fulfill({ status: 503, headers, json: {} });
    return route.fulfill({ headers, json: { items: companies.slice(skip, skip + 100), total: companies.length } });
  });
  await page.goto('/admin/companies/');
  await expect(page.getByRole('tabpanel').getByRole('alert')).toHaveText('Companies could not be loaded completely. Refresh to retry.');
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
  fail = false;
  const initialRequests = requests.length;
  await page.getByRole('button', { name: 'Refresh companies' }).click();
  await expect(page.getByRole('tab', { name: 'Inactive (1)', exact: true })).toBeVisible();
  await expect(page.getByRole('tabpanel').getByRole('alert')).not.toBeVisible();
  expect(requests.slice(initialRequests)).toEqual([0, 100]);
});

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
  await expect(page.getByRole('cell', { name: 'Service 0', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Service 1', exact: true })).not.toBeVisible();
  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByText('No active services.', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'Service 0', exact: true }) });
  await row.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Service 0', exact: true })).not.toBeVisible();
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('cell', { name: 'Service 0', exact: true })).toBeVisible();
});

for (const scenario of ['empty-page', 'changed-total', 'duplicate-record']) {
  test(`inconsistent company pagination is rejected: ${scenario}`, async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });
    await page.route('**/api/v1/admin/companies**', async route => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
      const skip = Number(new URL(route.request().url()).searchParams.get('skip') || 0);
      const items = skip === 0 ? companies.slice(0, 100) : scenario === 'empty-page' ? [] : scenario === 'duplicate-record' ? [companies[0]] : [companies[100]];
      const total = skip !== 0 && scenario === 'changed-total' ? 102 : 101;
      return route.fulfill({ headers, json: { items, total } });
    });
    await page.goto('/admin/companies/');
    await expect(page.getByRole('tabpanel').getByRole('alert')).toContainText('could not be loaded completely');
    await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
    await expect(page.getByRole('tab', { name: /^Active/ })).toHaveText('Active');
  });
}
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
  await page.getByRole('tabpanel').getByRole('button', { name: 'Edit', exact: true }).last().click();
  dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Contact email', { exact: true })).toHaveValue('branch@example.test');
  await dialog.getByLabel('Contact email', { exact: true }).fill('updated@example.test');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(saved[1].contactEmail).toBe('updated@example.test');
  await page.reload();
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('tabpanel').getByRole('button', { name: 'Edit', exact: true }).last().click();
  await expect(page.getByRole('dialog').getByLabel('Contact email', { exact: true })).toHaveValue('updated@example.test');
});
