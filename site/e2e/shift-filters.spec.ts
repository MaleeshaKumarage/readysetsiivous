import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The admin shift list filters by company and shift type on the server, so each page of results is filtered as a whole.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('the shift list filters by company and then by shift type', async ({ page }) => {
  await setupAdminMocks(page);
  const queries: string[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => {
    queries.push(new URL(route.request().url()).search);
    return route.fulfill({ headers, json: { items: [], total: 0, counts: { active: 0, completed: 0, inactive: 0 } } });
  });
  await page.route(/\/api\/v1\/admin\/companies\?/, route => route.fulfill({ headers, json: { items: [
    { id: '11111111-1111-4111-8111-111111111111', name: 'Alpha Oy', status: 'active' },
    { id: '22222222-2222-4222-8222-222222222222', name: 'Beta Ltd', status: 'active' },
  ], total: 2 } }));
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('textbox', { name: 'Filter by company' })).toBeVisible();

  await page.getByRole('textbox', { name: 'Filter by company' }).click();
  await page.getByRole('option', { name: 'Beta Ltd', exact: true }).click();
  await expect.poll(() => queries.at(-1)).toContain('companyId=22222222-2222-4222-8222-222222222222');

  await page.getByRole('textbox', { name: 'Filter by type' }).click();
  await page.getByRole('option', { name: 'One-time', exact: true }).click();
  await expect.poll(() => queries.at(-1)).toContain('scheduleType=6');
  await expect.poll(() => queries.at(-1)).toContain('companyId=22222222-2222-4222-8222-222222222222');
});

test('the work orders page has the design header and no separate today board', async ({ page }) => {
  // The design shows each work order's visits in its panel; the list page has no separate today board.
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [], total: 0, counts: { active: 0, completed: 0, inactive: 0 } } }));
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('heading', { name: 'Work orders', exact: true })).toBeVisible();
  await expect(page.getByText('One work order per property. Its visits are the cleanings.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Export', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'New work order', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Today' })).toHaveCount(0);
});

test('the list shows Property, Visits and Actions; assignees are not a list column', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [
    { id: 'shift-named', companyId: '11111111-1111-4111-8111-111111111111', branchId: '33333333-3333-4333-8333-333333333333', name: 'Lake apartment', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  ], total: 1, counts: { active: 1, completed: 0, inactive: 0 } } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignee-names/, route => route.fulfill({ headers, json: { 'shift-named': ['Ann Adams', 'Ben Bell'] } }));
  await page.goto('/admin/shifts/');
  // The list has no Employees column: assignees are managed from the row's Assign employees action and each visit's cleaner.
  const columnHeaders = page.getByRole('region', { name: 'Work orders table' }).getByRole('columnheader');
  await expect(columnHeaders).toHaveText(['Property', '', 'Visits', 'Actions']);
});
