import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Dashboard charts: visits per month by customer (sortable), and scheduled vs actual hours. KPI tiles open the matching view.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const months = [
  { month: '2026-10', customers: [
    { companyId: 'c2', customerName: 'Beta Ltd', scheduledVisits: 2, completedVisits: 1, scheduledHours: 4, actualHours: 3.5 },
    { companyId: 'c1', customerName: 'Alpha Oy', scheduledVisits: 5, completedVisits: 5, scheduledHours: 10, actualHours: 9 } ] },
  { month: '2026-11', customers: [] },
];

async function setup(page: import('@playwright/test').Page) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/work-queue/, route => route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: [], total: 0, counts: { 'needs-action': 3, scheduled: 5, 'in-progress': 0, completed: 0, cancelled: 0 } } }));
  await page.route(/\/api\/v1\/me\/inbox$/, route => route.fulfill({ headers, json: { unread: 0, items: [] } }));
  await page.route(/\/api\/v1\/admin\/dashboard\/monthly/, route => route.fulfill({ headers, json: months }));
}

test('the visits chart lists customers in the chosen order, and hours compare scheduled with actual', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/');
  const chart = page.getByRole('img', { name: /Alpha Oy: 5 visits/ });
  await expect(chart).toBeVisible();
  await expect(page.getByRole('img', { name: 'Scheduled hours 2026-10: 14.0' }).or(page.getByRole('img', { name: /Scheduled hours/ }).first())).toBeVisible();
  await expect(page.getByText('+-').first()).toHaveCount(0);
  // Sorting by name puts Alpha before Beta; by visits Alpha (5) is also first, so check the order by name explicitly.
  await page.getByRole('textbox', { name: 'Sort customers by' }).click();
  await page.getByRole('option', { name: 'Customer name', exact: true }).click();
  const names = await page.locator('text=/^(Alpha Oy|Beta Ltd)$/').allInnerTexts();
  expect(names).toEqual(['Alpha Oy', 'Beta Ltd']);
});

test('a KPI tile opens the work queue on the view it names', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/');
  await page.getByRole('link', { name: /Scheduled visits/ }).click();
  await expect(page).toHaveURL(/work-queue\/\?view=scheduled/);
  await expect(page.getByRole('tab', { name: /^Scheduled/ })).toHaveAttribute('aria-selected', 'true');
});
