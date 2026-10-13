import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
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
    const json = url.pathname.endsWith('/shifts') ? active || url.searchParams.get('includeInactive') === 'true' ? [{ ...shift, isActive: active }] : []
      : url.pathname.endsWith('/companies') ? { items: [], total: 0 } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/'); await expect(page.getByRole('cell', { name: 'Restorable shift' })).toHaveCount(0);
  await page.getByRole('checkbox', { name: 'Show deactivated shifts' }).check();
  await expect(page.getByRole('cell', { name: 'Restorable shift' })).toBeVisible();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByText('Could not reactivate shift. Check that its company and branch are active, then retry.')).toBeVisible();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click(); await expect(page.getByRole('button', { name: 'Deactivate', exact: true })).toBeVisible();
  await page.getByRole('checkbox', { name: 'Show deactivated shifts' }).uncheck(); await expect(page.getByRole('cell', { name: 'Restorable shift' })).toBeVisible();
  expect(restores).toBe(2);
});
