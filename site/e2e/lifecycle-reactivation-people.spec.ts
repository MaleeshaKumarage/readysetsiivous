import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
test('inactive employee reactivation shows failure then restores same invited profile', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = { id: 'employee-a', email: 'old@example.invalid', firstName: 'Existing', lastName: 'Cleaner', role: 'employee', isActive: false, keycloakUserId: 'same-login' };
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, r => r.fulfill({ headers, json: [employee] }));
  let calls = 0;
  await page.route(/\/employees\/employee-a\/reactivate$/, r => {
    if (r.request().method() === 'OPTIONS') return r.fulfill({ headers, status: 200 });
    if (++calls === 1) { employee.isActive = true; return r.fulfill({ headers, status: 502, json: { detail: 'Login synchronization failed. Retry reactivation.' } }); }
    employee.isActive = true; return r.fulfill({ headers, status: 204 });
  });
  await page.goto('/admin/employees/'); await page.getByRole('tab', { name: /Inactive/ }).click();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Login synchronization failed');
  await page.getByRole('button', { name: 'Retry reactivation', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('reactivated');
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'old@example.invalid' })).toContainText('Invited');
  expect(calls).toBe(2);
});
for (const inactive of [false, true]) test(`duplicate ${inactive ? 'inactive' : 'active'} email retains create form and shows guidance`, async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, r => {
    if (r.request().method() === 'OPTIONS') return r.fulfill({ headers, status: 200 });
    if (r.request().method() === 'POST') return r.fulfill({ headers, status: 409, json: { detail: inactive ? 'An inactive employee already uses this email. Open the Inactive tab and Reactivate the existing employee.' : 'An employee already uses this email. Use the existing employee account.' } });
    return r.fulfill({ headers, json: [] });
  });
  await page.goto('/admin/employees/'); await page.getByRole('button', { name: 'Add employee' }).click();
  await page.getByLabel('Email').fill('Existing@example.invalid');
  await page.getByLabel('First name').fill('Keep'); await page.getByLabel('Last name').fill('Inputs');
  await page.getByRole('button', { name: 'Create employee' }).click();
  await expect(page.locator('p[role="alert"]')).toContainText(inactive ? 'Inactive tab' : 'existing employee');
  await expect(page.getByLabel('Email')).toHaveValue('Existing@example.invalid');
  await expect(page.getByRole('button', { name: 'Create employee' })).toBeEnabled();
});
test('signed agreement restores activity without changing completed status; cancelled remains blocked', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const signed = { id: 'signed-a', title: 'Signed preserved', code: 'A', status: 'Completed', isActive: false, signerCount: 1, signedCount: 1 };
  const cancelled = { id: 'cancelled-a', title: 'Cancelled preserved', code: 'B', status: 'Cancelled', isActive: false, signerCount: 1, signedCount: 0 };
  await page.route(/\/api\/v1\/admin\/agreements$/, r => r.fulfill({ headers, json: [signed, cancelled] }));
  await page.route(/\/agreements\/signed-a\/activate$/, r => { if (r.request().method() !== 'OPTIONS') signed.isActive = true; return r.fulfill({ headers, status: 204 }); });
  await page.goto('/admin/agreements/'); await page.getByRole('tab', { name: /Inactive/ }).click();
  const row = page.getByRole('row').filter({ hasText: 'Signed preserved' });
  await expect(row.getByRole('button', { name: 'PDF', exact: true })).toBeEnabled();
  await expect(page.getByRole('row').filter({ hasText: 'Cancelled preserved' }).getByRole('button', { name: 'Reactivate' })).toBeDisabled();
  await row.getByRole('button', { name: 'Reactivate' }).click(); await expect(row).toHaveCount(0);
  await page.getByRole('tab', { name: /^Active/ }).click(); await expect(row).toContainText('Completed');
});

test('pending identity sync offers retry after page reload on active profile', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = { id: 'pending-a', email: 'pending@example.invalid', firstName: 'Pending', lastName: 'Cleaner', role: 'employee', isActive: true, identitySyncPending: true, keycloakUserId: 'same-login' };
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, r => r.fulfill({ headers, json: [employee] }));
  await page.route(/\/employees\/pending-a\/reactivate$/, r => { if (r.request().method() !== 'OPTIONS') employee.identitySyncPending = false; return r.fulfill({ headers, status: 204 }); });
  await page.goto('/admin/employees/'); await page.reload();
  await page.getByRole('button', { name: 'Retry reactivation', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('reactivated');
  await expect(page.getByRole('button', { name: 'Retry reactivation', exact: true })).toHaveCount(0);
});
