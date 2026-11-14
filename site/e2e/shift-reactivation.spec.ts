import { test, expect } from '@playwright/test';
import { shiftAction, confirmShiftPopup } from './shift-actions';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('inactive shift deletion removes the shift after confirmation without blocking on QC history', async ({ page }) => {
  await setupAdminMocks(page); let deleted = false; let deleteCalls = 0;
  const shift = { id: '11111111-1111-4111-8111-111111111111', name: 'Shift with recorded QC', isActive: false,
    schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, companyId: 'company', branchId: 'branch' };
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const url = new URL(request.url());
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (request.method() === 'DELETE' && url.pathname.endsWith(`/shifts/${shift.id}`)) {
      deleteCalls++; deleted = true; return route.fulfill({ status: 204, headers });
    }
    if (url.pathname.endsWith('/shifts/page')) return route.fulfill({ status: 200, headers, json: shiftsResponse(deleted ? [] : [shift], request.url()) });
    return route.fulfill({ status: 200, headers, json: url.pathname.endsWith('/shifts') ? deleted ? [] : [shift]
      : url.pathname.endsWith('/companies') ? { items: [], total: 0 } : [] });
  });
  await page.goto('/admin/shifts/'); await expect(page.getByRole('button', { name: 'More actions for Shift with recorded QC', exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await shiftAction(page, shift.name, 'Delete');
  const popup = page.getByRole('dialog', { name: /^Delete “/ });
  await expect(popup.getByText('Completed checklist records, photos and reports will remain in history.', { exact: false })).toBeVisible();
  await popup.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(deleteCalls).toBe(0); await expect(page.getByRole('gridcell', { name: shift.name, exact: true })).toBeVisible();
  await shiftAction(page, shift.name, 'Delete'); await confirmShiftPopup(page, 'Delete');
  await expect(page.getByRole('gridcell', { name: shift.name, exact: true })).toHaveCount(0);
  expect(deleteCalls).toBe(1);
  await expect(page.getByRole('tab', { name: 'Inactive (0)', exact: true })).toBeVisible();
  await page.reload();
  await page.getByRole('tab', { name: 'Inactive (0)', exact: true }).click();
  await expect(page.getByRole('gridcell', { name: shift.name, exact: true })).toHaveCount(0);
});
test('admin discovers deactivated shift, sees restore failure and reactivates it', async ({ page }) => {
  await setupAdminMocks(page); let active = false; let restores = 0;
  const shift = { id: '11111111-1111-4111-8111-111111111111', name: 'Restorable shift', schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, companyId: 'company', branchId: 'branch' };
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const url = new URL(request.url());
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (url.pathname.endsWith('/reactivate')) {
      restores++; if (restores === 1) return route.fulfill({ status: 409, headers, json: { detail: 'Parents inactive' } });
      active = true; return route.fulfill({ status: 200, headers, json: { ...shift, isActive: true } });
    }
    if (url.pathname.endsWith('/shifts/page')) return route.fulfill({ status: 200, headers, json: shiftsResponse([{ ...shift, isActive: active }], request.url()) });
    const json = url.pathname.endsWith('/shifts') ? active || url.searchParams.get('includeInactive') === 'true' ? [{ ...shift, isActive: active }] : []
      : url.pathname.endsWith('/companies') ? { items: [], total: 0 } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/'); await expect(page.getByRole('gridcell', { name: 'Restorable shift', exact: true })).toHaveCount(0);
  await expect(page.getByRole('tab', { name: /^Active/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('No active shifts. Check the Inactive tab.')).toBeVisible();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('gridcell', { name: 'Restorable shift', exact: true })).toBeVisible();
  await shiftAction(page, 'Restorable shift', 'Reactivate'); await confirmShiftPopup(page, 'Reactivate');
  await expect(page.getByText('Could not reactivate shift. Check that its company and branch are active, then retry.')).toBeVisible();
  await shiftAction(page, 'Restorable shift', 'Reactivate'); await confirmShiftPopup(page, 'Reactivate');
  await expect(page.getByRole('gridcell', { name: 'Restorable shift', exact: true })).toHaveCount(0);
  await expect(page.getByText('No inactive shifts. Check the Active tab.')).toBeVisible();
  await page.getByRole('tab', { name: /^Active/ }).click(); await expect(page.getByRole('gridcell', { name: 'Restorable shift', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'More actions for Restorable shift', exact: true }).click(); await expect(page.getByRole('menuitem', { name: 'Deactivate', exact: true })).toBeVisible(); await page.keyboard.press('Escape');
  expect(restores).toBe(2);
});
