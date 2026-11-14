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

  await page.getByRole('textbox', { name: 'Filter by shift type' }).click();
  await page.getByRole('option', { name: 'One-time', exact: true }).click();
  await expect.poll(() => queries.at(-1)).toContain('scheduleType=6');
  await expect.poll(() => queries.at(-1)).toContain('companyId=22222222-2222-4222-8222-222222222222');
});

test("the shift board shows visits in progress now and today's scheduled visits and hours", async ({ page }) => {
  // Fix the browser date to 9 November; the mocked daily data gives that day three visits and six hours.
  await page.clock.setFixedTime(new Date(2026, 10, 9, 10, 0, 0));
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [], total: 0, counts: { active: 0, completed: 0, inactive: 0 } } }));
  await page.route(/\/api\/v1\/admin\/work-queue/, route => route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: [], total: 0, counts: { 'needs-action': 3, scheduled: 5, 'in-progress': 2, completed: 0, cancelled: 0 } } }));
  await page.route(/\/api\/v1\/admin\/dashboard\/daily/, route => route.fulfill({ headers, json: { month: '2026-11', days: [
    { date: '2026-11-09', scheduledVisits: 3, scheduledHours: 6, actualHours: 0, companies: [] },
  ] } }));
  await page.goto('/admin/shifts/');
  const today = page.getByRole('region', { name: 'Today' });
  await expect(today).toBeVisible();
  await expect(today.getByText('Visits in progress now').locator('xpath=..').getByText('2', { exact: true })).toBeVisible();
  await expect(today.getByText('Scheduled visits today').locator('xpath=..').getByText('3', { exact: true })).toBeVisible();
  await expect(today.getByText('Scheduled hours today').locator('xpath=..').getByText('6.0', { exact: true })).toBeVisible();
});

test('the Employees column names each assigned employee', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [
    { id: 'shift-named', companyId: '11111111-1111-4111-8111-111111111111', branchId: '33333333-3333-4333-8333-333333333333', name: 'Lake apartment', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  ], total: 1, counts: { active: 1, completed: 0, inactive: 0 } } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignee-names/, route => route.fulfill({ headers, json: { 'shift-named': ['Ann Adams', 'Ben Bell'] } }));
  await page.goto('/admin/shifts/');
  const row = page.getByRole('row').filter({ hasText: 'Lake apartment' });
  await expect(row.getByRole('gridcell', { name: 'Ann Adams, Ben Bell', exact: true })).toBeVisible();
});
