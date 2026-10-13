import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

for (const role of ['employee', 'manager', 'staff', 'no-role']) {
  test(`${role} direct navigation never mounts administrative data reads`, async ({ page }) => {
    test.setTimeout(60000);
    await setupAdminMocks(page);
    await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = role === 'no-role' ? [] : [role]; }, role);
    let adminReads = 0;
    await page.route(/\/api\/v1\/admin\//, route => { adminReads++; return route.fulfill({ status: 200, headers, json: [] }); });
    for (const path of ['employees', 'agreements', 'services', 'companies', 'shifts', 'quality-cycle']) {
      await page.goto(`/admin/${path}/`);
      await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
      await expect(page.getByRole('link', { name: 'My account' })).toHaveAttribute('href', '/account/?tenant=readysetsiivous');
      await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
      expect(adminReads).toBe(0);
    }
  });
}

test('transient identity verification cannot mount children and retry recovers', async ({ page }) => {
  await setupAdminMocks(page);
  let identityCalls = 0; let adminReads = 0;
  await page.route(/\/api\/v1\/me$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    identityCalls++;
    return route.fulfill(identityCalls === 1 ? { status: 503, headers } : { status: 200, headers,
      json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], employee: null } });
  });
  await page.route(/\/api\/v1\/admin\/employees/, route => { adminReads++; return route.fulfill({ status: 200, headers, json: [] }); });
  await page.goto('/admin/employees/');
  await expect(page.getByRole('heading', { name: 'Could not verify access' })).toBeVisible();
  expect(adminReads).toBe(0);
  await page.getByRole('button', { name: 'Retry verification' }).click();
  await expect(page.getByRole('button', { name: 'Add employee' })).toBeVisible();
  expect(identityCalls).toBe(2); expect(adminReads).toBeGreaterThan(0);
});

for (const failure of ['stale-role', 'foreign-realm', 'persisted-employee-role', 'malformed-roles']) {
  test(`admin token with ${failure} verification does not read administrative data`, async ({ page }) => {
    await setupAdminMocks(page);
    let adminReads = 0;
    await page.route(/\/api\/v1\/admin\//, route => { adminReads++; return route.fulfill({ status: 200, headers, json: [] }); });
    await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: failure === 'stale-role' ? 403 : 200, headers,
      json: { realm: failure === 'foreign-realm' ? 'another-tenant' : 'readysetsiivous',
        roles: failure === 'malformed-roles' ? ['bad-json'] : [JSON.stringify({ roles: ['admin'] })],
        employee: failure === 'persisted-employee-role' ? { id: 'employee-a', role: 'employee', isActive: true } : null } }));
    await page.goto('/admin/employees/');
    await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
    expect(adminReads).toBe(0);
  });
}
