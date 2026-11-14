import { test, expect } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const shift = { id: 'shift-1', companyId: 'company', branchId: 'branch', name: 'Office clean', isActive: true, validFrom: '2026-10-01', schedule: { type: 3 } };

test('a shift list reply without the page shape explains the problem and recovers on refresh', async ({ page }) => {
  await setupAdminMocks(page);
  let broken = true;
  await page.route(/\/api\/v1\/admin\//, route => {
    const url = route.request().url(); const path = new URL(url).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (isShiftsRoute(path)) return route.fulfill({ headers, json: broken && path.endsWith('/shifts/page') ? [] : shiftsResponse([shift], url) });
    if (path.endsWith('/companies')) return route.fulfill({ headers, json: { items: [{ id: 'company', name: 'Methehe', isActive: true }], total: 1 } });
    return route.fulfill({ headers, json: path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('alert').filter({ hasText: 'The shift list could not be read. Refresh the page to retry.' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Shifts', exact: true })).toBeVisible();
  broken = false;
  await page.reload();
  await expect(page.getByRole('gridcell', { name: 'Office clean', exact: true })).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'could not be read' })).toHaveCount(0);
});
