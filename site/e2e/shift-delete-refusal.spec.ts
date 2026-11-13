import { test, expect } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { confirmShiftPopup } from './shift-actions';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const REFUSAL = 'This shift has started or completed visits that may not be invoiced yet. Keep it deactivated so its work and billing stay available.';

test('deleting an inactive shift with started visits is refused, explains why, and the shift stays listed', async ({ page }) => {
  await setupAdminMocks(page);
  const shift = { id: 'shift-1', name: 'Morning clean', companyId: 'company', branchId: 'branch', isActive: false, notes: null, validFrom: '2026-11-01T00:00:00Z', validUntil: null, schedule: { type: 0, dailyStart: '08:00:00', dailyEnd: '10:00:00', dailyTimes: {}, weeklyDays: [] } };
  let deletes = 0;
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts/shift-1') && method === 'DELETE') { deletes++; return route.fulfill({ status: 409, headers, json: { title: 'Inactive record conflict', detail: REFUSAL } }); }
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse([shift], route.request().url())
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('button', { name: 'More actions for Morning clean' }).click();
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('menuitem', { name: 'Delete', exact: true }).click();
  await confirmShiftPopup(page, 'Delete');
  await expect(page.getByText(REFUSAL).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'More actions for Morning clean' })).toBeVisible();
  expect(deletes).toBe(1);
});
