import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

test('admin invite sends setup email and exposes failure then retry', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee: any = { id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true };
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [employee] }));
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
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveCount(0);
  await expect(page.getByText('To receive shifts, add an Administrator profile', { exact: false })).toBeVisible();
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
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [{ id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }] }));
  await page.goto('/admin/employees/');
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Invite', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Deactivate', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add employee', exact: true })).toHaveCount(0);
});

test('deactivation failure is visible instead of silently disappearing', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [{ id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }] }));
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/deactivate$/, route => route.fulfill({ status: 502, headers, json: { title: 'Login synchronization failed' } }));
  await page.goto('/admin/employees/'); await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Login synchronization failed');
  await expect(page.locator('p[role="alert"]')).toContainText('Reload to check whether the change was saved');
});

test('A01 account preference persists, rejects pending offers and retries without optimistic saving', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'tenant-a', employee: { id: 'employee-a', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true } } }));
  let stored = false, blocked = true, gets = 0;
  let finishPending!: () => void;
  const pendingSave = new Promise<void>(resolve => { finishPending = resolve; });
  const preferenceHeaders = { ...headers, 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS' };
  await page.route(/\/api\/v1\/me\/work-preferences$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers: preferenceHeaders });
    if (route.request().method() === 'PUT') {
      expect(typeof route.request().postDataJSON().autoAcceptWork).toBe('boolean');
      if (blocked) { await pendingSave; blocked = false; return route.fulfill({ status: 409, headers: preferenceHeaders, json: { detail: 'Accept or decline your pending offers before enabling automatic acceptance.' } }); }
      stored = route.request().postDataJSON().autoAcceptWork;
    } else gets++;
    return route.fulfill({ headers: preferenceHeaders, json: { autoAcceptWork: stored } });
  });
  await page.goto('/account/?tenant=tenant-a');
  const toggle = page.getByRole('checkbox', { name: 'Automatically accept cleaning offers' });
  await expect(toggle).not.toBeChecked(); await toggle.check();
  await page.getByRole('button', { name: 'Save work preferences', exact: true }).click();
  await expect(page.getByText('Saving work preferences...', { exact: true })).toBeVisible();
  await expect(toggle).toBeDisabled(); await expect(page.getByRole('button', { name: 'Save work preferences', exact: true })).toBeDisabled();
  finishPending();
  await expect(page.locator('p[role="alert"]')).toContainText('Accept or decline');
  expect(stored).toBe(false); await expect(page.getByText('Work preferences saved.', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Retry work preferences', exact: true }).click();
  await expect(page.getByText('Work preferences saved.', { exact: true })).toBeVisible();
  await page.reload(); await expect(toggle).toBeChecked(); expect(gets).toBe(2);
  await toggle.uncheck(); await page.getByRole('button', { name: 'Save work preferences', exact: true }).click();
  await expect(page.getByText('Work preferences saved.', { exact: true })).toBeVisible();
  await page.reload(); await expect(toggle).not.toBeChecked(); expect(stored).toBe(false);
});

for (const profile of [null, { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'partner', isActive: true }, { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: false }, { id: 'employee-a', keycloakUserId: 'user-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true, identitySyncPending: true }]) {
  test(`A01 account does not expose preference for unsupported binding ${profile?.role ?? 'unbound'} ${profile?.isActive ?? ''} sync pending ${profile?.identitySyncPending ?? false}`, async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });
    let requests = 0;
    await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'tenant-a', employee: profile } }));
    await page.route(/\/api\/v1\/me\/work-preferences$/, route => { requests++; return route.fulfill({ headers, json: { autoAcceptWork: false } }); });
    await page.goto('/account/?tenant=tenant-a'); await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: 'Automatically accept cleaning offers' })).toHaveCount(0); expect(requests).toBe(0);
  });
}

test('A01 account retries failed preference loading for verified manager and admin profiles', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let role = 'manager', failed = true;
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'tenant-a', employee: { id: 'staff', firstName: 'Test', lastName: 'Staff', role, isActive: true } } }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => {
    if (failed) { failed = false; return route.fulfill({ status: 503, headers, json: { detail: 'Preferences temporarily unavailable.' } }); }
    return route.fulfill({ headers, json: { autoAcceptWork: false } });
  });
  await page.goto('/account/?tenant=tenant-a'); await expect(page.locator('p[role="alert"]')).toContainText('temporarily unavailable');
  await expect(page.getByRole('checkbox')).toHaveCount(0);
  await page.getByRole('button', { name: 'Retry work preferences' }).click();
  await expect(page.getByRole('checkbox', { name: 'Automatically accept cleaning offers' })).not.toBeChecked();
  role = 'admin'; await page.reload();
  await expect(page.getByRole('checkbox', { name: 'Automatically accept cleaning offers' })).not.toBeChecked();
});

test('deactivating a cleaner who still has active shifts is refused with the reason and the next step', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const REASON = 'This cleaner is still assigned to 2 active shift(s). Reassign or remove them first, then deactivate the cleaner.';
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [{ id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true, phone: '', defaultHours: {}, skills: [], serviceAreas: [] }] }));
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/deactivate$/, route => route.fulfill({ status: 409, headers, json: { title: 'Shift conflict', detail: REASON } }));
  await page.goto('/admin/employees/'); await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toHaveText(REASON);
  await expect(page.getByText('could not be confirmed')).toHaveCount(0);
});
