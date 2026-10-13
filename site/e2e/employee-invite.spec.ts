import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

test('admin invite sends setup email and exposes failure then retry', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee: any = { id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true };
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ status: 200, headers, json: [employee] }));
  let failed = true;
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/invite$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (failed) { failed = false; return route.fulfill({ status: 503, headers, json: { detail: 'Configure SMTP before sending invitations.' } }); }
    const already = Boolean(employee.keycloakUserId); employee.keycloakUserId = 'user-a';
    return route.fulfill({ status: 200, headers, json: { email: employee.email, keycloakUserId: 'user-a', status: already ? 'AlreadySent' : 'Sent', sentUtc: new Date().toISOString() } });
  });
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Invite', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Configure SMTP');
  await page.getByRole('button', { name: 'Invite', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Account setup email sent to cleaner@example.invalid');
  await expect(page.locator('p[role="alert"]')).toHaveCount(0);
  await page.getByRole('button', { name: 'Resend setup email', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('A setup email was sent recently');
});

test('employee account landing confirms binding without admin link', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers, json: { realm: 'tenant-a', employee: { id: 'employee-a', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true } } }));
  await page.goto('/account/?tenant=tenant-a');
  await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();
  await expect(page.getByText('Open My shifts to start your shift, record the activities you did, and end it.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveAttribute('href', '/me/shifts/');
  await expect(page.getByRole('link', { name: 'Continue to administration' })).toHaveCount(0);
  expect(await page.evaluate(() => sessionStorage.getItem('__AUTH_TENANT__'))).toBe('tenant-a');
});

test('admin account landing offers valid administration route', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers, json: { realm: 'tenant-a', employee: null } }));
  await page.goto('/account/?tenant=tenant-a');
  await expect(page.getByRole('link', { name: 'Continue to administration' })).toHaveAttribute('href', '/admin/');
});

test('mismatched account realm is not shown as ready', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers, json: { realm: 'other-tenant', employee: null } }));
  await page.goto('/account/?tenant=tenant-a');
  await expect(page.locator('p[role="alert"]')).toContainText('different tenant');
  await expect(page.getByRole('heading', { name: 'Account ready' })).toHaveCount(0);
});

test('invalid tenant link does not begin login', async ({ page }) => {
  await page.goto('/account/?tenant=../master');
  await expect(page.locator('p[role="alert"]')).toContainText('Invalid tenant');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0);
});


test('employee cannot use invite or employee mutation controls', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ status: 200, headers, json: [{ id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }] }));
  await page.goto('/admin/employees/');
  await expect(page.getByRole('cell', { name: 'Test Cleaner' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Invite', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Deactivate', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add employee', exact: true })).toHaveCount(0);
});

test('deactivation failure is visible instead of silently disappearing', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ status: 200, headers, json: [{ id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }] }));
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/deactivate$/, route => route.fulfill({ status: 502, headers, json: { title: 'Login synchronization failed' } }));
  await page.goto('/admin/employees/'); await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('could not be confirmed');
});
