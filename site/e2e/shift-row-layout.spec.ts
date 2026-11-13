import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Each shift row shows its property address, with size, rooms and beds as separate tags.
// A click on the row expands the visits under it; the row's actions menu does not.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const COMPANY = '11111111-1111-4111-8111-111111111111';
const BRANCH = '33333333-3333-4333-8333-333333333333';

test('shift rows show the address with size, rooms and beds as tags, and a row click expands its visits', async ({ page }, testInfo) => {
  await setupAdminMocks(page);
  const tomorrow = new Date(Date.now() + 86400000).toISOString().slice(0, 10);
  await page.route(/\/api\/v1\/admin\/shifts\/shift-layout\/occurrences/, route => route.fulfill({ headers, json: [
    { startUtc: `${tomorrow}T06:00:00Z`, endUtc: `${tomorrow}T08:00:00Z`, exceptionId: null, status: 1, visitNumber: 1, visitCleaningType: 'Deep', assigneeIds: [], offeredEmployeeId: null, workStatus: null },
  ] }));
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [
    { id: 'shift-layout', companyId: COMPANY, branchId: BRANCH, name: 'Cleaning - Lakeside 4, 00100 Helsinki |34m2|3H|5V', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  ], total: 1, counts: { active: 1, completed: 0, inactive: 0 } } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignee-names/, route => route.fulfill({ headers, json: { 'shift-layout': ['Ann Adams'] } }));
  await page.route(new RegExp(`/api/v1/admin/companies/${COMPANY}$`), route => route.fulfill({ headers, json: {
    company: { id: COMPANY, name: 'Alpha Oy', status: 'active' },
    branches: [{ id: BRANCH, name: 'Lake apartment', street: 'Lakeside 4', postalCode: '00100', city: 'Helsinki', sizeM2: 34, roomCount: 3, bedCount: 5, isActive: true, priceVersions: [] }],
  } }));
  await page.goto('/admin/shifts/');

  const row = page.getByRole('row').filter({ hasText: 'Lake apartment' }).or(page.getByRole('row').filter({ hasText: 'Lakeside 4' }));
  await expect(row.getByText('Lakeside 4, 00100 Helsinki', { exact: true })).toBeVisible();
  await expect(row.getByText('34 m²', { exact: true })).toBeVisible();
  await expect(row.getByText('3 rooms', { exact: true })).toBeVisible();
  await expect(row.getByText('5 beds', { exact: true })).toBeVisible();
  await expect(row.getByRole('cell', { name: 'Ann Adams', exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('shift-rows.png'), fullPage: true });

  await expect(page.getByTestId('row-visits')).toHaveCount(0);
  await row.getByText('Lakeside 4, 00100 Helsinki', { exact: true }).click();
  const visits = page.getByTestId('row-visits');
  await expect(visits).toBeVisible();
  await expect(visits.getByRole('cell', { name: 'Deep cleaning', exact: true })).toBeVisible();
  await expect(visits.getByRole('cell', { name: 'Unassigned', exact: true })).toBeVisible();
  // The month list sits on the Visits header line and reloads the visits for the chosen month.
  const requests: string[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/shift-layout\/occurrences/, route => { requests.push(route.request().url()); return route.continue(); });
  const header = visits.getByRole('button', { name: /Visits/ });
  const month = page.getByRole('combobox', { name: 'Month' });
  await expect(month).toBeVisible();
  const headerBox = await header.boundingBox();
  const monthBox = await month.boundingBox();
  expect(Math.abs((headerBox?.y ?? 0) - (monthBox?.y ?? 0))).toBeLessThan(30);
  const chosen = await month.locator('option').nth(5).getAttribute('value');
  await month.selectOption({ index: 5 });
  await expect(month).toHaveValue(chosen ?? '');
  await expect.poll(() => requests.length).toBeGreaterThan(0);
  await page.screenshot({ path: testInfo.outputPath('shift-rows-expanded.png'), fullPage: true });
  // The visits section collapses on its own header without collapsing the row.
  await visits.getByRole('button', { name: /Visits/ }).click();
  await expect(visits.getByRole('cell', { name: 'Deep cleaning', exact: true })).toHaveCount(0);
  await expect(visits.getByRole('button', { name: /Visits/ })).toHaveAttribute('aria-expanded', 'false');
  await expect(visits).toBeVisible();
  await visits.getByRole('button', { name: /Visits/ }).click();
  await expect(visits.getByRole('cell', { name: 'Deep cleaning', exact: true })).toBeVisible();
  await row.getByText('Lakeside 4, 00100 Helsinki', { exact: true }).click();
  await expect(visits).toHaveCount(0);
  await expect(page.getByRole('dialog')).toHaveCount(0);

  await row.getByRole('button', { name: /^More actions for / }).click();
  await expect(page.getByRole('menuitem', { name: 'View details', exact: true })).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByTestId('row-visits')).toHaveCount(0);
});
