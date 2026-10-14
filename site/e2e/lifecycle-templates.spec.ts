import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { QualityCycleTemplate } from '../src/lib/qualityCycleApi';

const headers = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS',
  'Access-Control-Allow-Headers': 'Authorization,Content-Type',
};

function template(id: string, title: string, isActive: boolean): QualityCycleTemplate {
  return { id, title, isActive, items: ['Clean floor'], companyId: 'company-1', branchId: 'branch-1',
    createdUtc: '2026-10-14T08:00:00Z', updatedUtc: '2026-10-14T08:00:00Z' };
}

async function mockTemplates(page: Page, initial: QualityCycleTemplate[]) {
  await setupAdminMocks(page);
  let templates = initial;
  let loads = 0;
  let update: Record<string, unknown> | undefined;
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/forms(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/templates(?:\/[^/?]+)?(?:\?.*)?$/, async route => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (method === 'GET') { loads++; return route.fulfill({ headers, json: templates }); }
    const body = route.request().postDataJSON();
    if (method === 'POST') {
      const created = { ...template('created', body.title, true), ...body };
      templates = [...templates, created];
      return route.fulfill({ headers, json: created });
    }
    if (method === 'PUT') {
      update = body;
      const id = route.request().url().split('/').pop();
      templates = templates.map(t => t.id === id ? { ...t, ...body } : t);
      return route.fulfill({ headers, json: templates.find(t => t.id === id) });
    }
    return route.fulfill({ headers, status: 405 });
  });
  return { loads: () => loads, update: () => update };
}

test('template lifecycle tabs filter locally and remain independent of submissions and reports', async ({ page }) => {
  const requests = await mockTemplates(page, [template('active', 'Current checklist', true), template('inactive', 'Archived checklist', false)]);
  await page.goto('/admin/quality-cycle/');
  const tabs = page.getByRole('tablist', { name: 'Checklist template activity' });
  await expect(tabs.getByRole('tab', { name: 'Active (1)', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Current checklist', { exact: true })).toBeVisible();
  await expect(page.getByText('Archived checklist', { exact: true })).toBeHidden();
  const initialLoads = requests.loads();
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByText('Archived checklist', { exact: true })).toBeVisible();
  await expect(page.getByText('Current checklist', { exact: true })).toBeHidden();
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).press('ArrowLeft');
  await expect(page.getByText('Current checklist', { exact: true })).toBeVisible();
  expect(requests.loads()).toBe(initialLoads);
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await page.getByText('Submissions & PDF Reports', { exact: true }).click();
  await expect(tabs).toBeHidden();
  await expect(page.getByRole('heading', { name: 'Monthly branch and shift reports' })).toBeVisible();
  await expect(page.getByText('Quality Cycle Submissions Log', { exact: true })).toBeVisible();
  await page.getByText('Checklist Templates', { exact: true }).click();
  await expect(page.getByText('Archived checklist', { exact: true })).toBeVisible();
  expect(requests.loads()).toBe(initialLoads);
});

test('editing inactive templates preserves lifecycle and scope; creation returns to Active', async ({ page }) => {
  const requests = await mockTemplates(page, [template('inactive', 'Archived checklist', false)]);
  await page.goto('/admin/quality-cycle/');
  await expect(page.getByText('No active checklist templates. Select Inactive to view your other templates.')).toBeVisible();
  const tabs = page.getByRole('tablist', { name: 'Checklist template activity' });
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await page.getByPlaceholder('e.g. Daily Office Cleaning Checklist').fill('Updated archived checklist');
  await page.getByRole('button', { name: 'Update Template', exact: true }).click();
  await expect(page.getByText('Updated archived checklist', { exact: true })).toBeVisible();
  await expect(tabs.getByRole('tab', { name: 'Inactive (1)', exact: true })).toHaveAttribute('aria-selected', 'true');
  expect(requests.update()).toMatchObject({ isActive: false, companyId: 'company-1', branchId: 'branch-1' });
  await page.getByPlaceholder('e.g. Daily Office Cleaning Checklist').fill('New active checklist');
  await page.locator('input[placeholder^="Item "]').first().fill('Wash floor');
  await page.getByRole('button', { name: 'Create Template', exact: true }).click();
  await expect(tabs.getByRole('tab', { name: 'Active (1)', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('New active checklist', { exact: true })).toBeVisible();
  await expect(page.getByText('Updated archived checklist', { exact: true })).toBeHidden();
});

test('empty templates retain first-use messaging in either tab', async ({ page }) => {
  await mockTemplates(page, []);
  await page.goto('/admin/quality-cycle/');
  await expect(page.getByText('No quality cycle templates created yet.')).toBeVisible();
  await page.getByRole('tab', { name: 'Inactive (0)', exact: true }).click();
  await expect(page.getByText('No quality cycle templates created yet.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create Template', exact: true })).toBeVisible();
});
