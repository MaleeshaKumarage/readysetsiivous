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
