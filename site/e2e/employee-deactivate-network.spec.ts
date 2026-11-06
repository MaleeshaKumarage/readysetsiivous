import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const RELOAD_GUIDANCE = 'Reload to check whether the change was saved before retrying.';

test('a dropped connection while deactivating says the change may have been saved and to reload first', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let deactivations = 0;
  await page.route('**/api/v1/admin/employees**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (path.endsWith('/deactivate') && request.method() === 'POST') { deactivations++; return route.abort('failed'); }
    if (request.method() === 'GET') return route.fulfill({ status: 200, headers: CORS, json: [{ id: 'emp-1', firstName: 'Test', lastName: 'Cleaner', email: 'test.cleaner@example.test', role: 'employee', isActive: true }] });
    return route.fulfill({ status: 200, headers: CORS, json: {} });
  });
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).first().click();
  await expect(page.getByText(RELOAD_GUIDANCE).first()).toBeVisible();
  expect(deactivations).toBe(1);
});
