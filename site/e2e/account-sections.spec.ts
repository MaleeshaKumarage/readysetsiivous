import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

// The account picker shows one tile per section the signed-in login can open, like the Android TV account chooser.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Authorization,Content-Type', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS' };

async function signIn(page: Page, roles: string[], employee: { role: string } | null) {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(list => { (window as any).__MOCK_ROLES__ = list; }, roles);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers, json: {
    realm: 'tenant-a', employee: employee && { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: employee.role, isActive: true } } }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ status: 200, headers, json: { autoAcceptWork: false } }));
  await page.goto('/account/?tenant=tenant-a');
  await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();
}

test('partner account is offered only the partner portal', async ({ page }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/account/?tenant=readysetsiivous');
  await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();

  const picker = page.getByRole('navigation', { name: 'Choose a section' });
  await expect(picker.getByRole('link')).toHaveCount(1);
  await expect(picker.getByRole('link', { name: 'Open partner portal', exact: true })).toHaveAttribute('href', '/partner/');
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Manage tenant operations', exact: true })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Continue to administration', exact: true })).toHaveCount(0);
  if (process.env.PORTAL_SHOT_DIR) await page.screenshot({ path: `${process.env.PORTAL_SHOT_DIR}/portal-partner-only.png`, fullPage: true });
});

test('manager who is also an employee is offered My shifts and tenant operations', async ({ page }) => {
  await signIn(page, ['manager', 'employee'], { role: 'manager' });

  const picker = page.getByRole('navigation', { name: 'Choose a section' });
  await expect(picker.getByRole('link')).toHaveCount(2);
  await expect(picker.getByRole('link', { name: 'My shifts', exact: true })).toHaveAttribute('href', '/me/shifts/');
  await expect(picker.getByRole('link', { name: 'Manage tenant operations', exact: true })).toHaveAttribute('href', '/manager/');
  await expect(picker.getByRole('link', { name: 'Open partner portal', exact: true })).toHaveCount(0);
  await expect(picker.getByRole('link', { name: 'Continue to administration', exact: true })).toHaveCount(0);
});

test('administrator who is also an employee is offered My shifts and administration', async ({ page }, testInfo) => {
  await signIn(page, ['admin', 'employee'], { role: 'admin' });

  const picker = page.getByRole('navigation', { name: 'Choose a section' });
  await expect(picker.getByRole('link')).toHaveCount(2);
  await expect(picker.getByRole('link', { name: 'My shifts', exact: true })).toHaveAttribute('href', '/me/shifts/');
  await expect(picker.getByRole('link', { name: 'Continue to administration', exact: true })).toHaveAttribute('href', '/admin/');
  await expect(picker.getByRole('link', { name: 'Manage tenant operations', exact: true })).toHaveCount(0);
  if (process.env.PORTAL_SHOT_DIR) await page.screenshot({ path: `${process.env.PORTAL_SHOT_DIR}/portal-admin-employee.png`, fullPage: true });
});

test('employee without a management role is offered only My shifts', async ({ page }) => {
  await signIn(page, ['employee'], { role: 'employee' });

  const picker = page.getByRole('navigation', { name: 'Choose a section' });
  await expect(picker.getByRole('link')).toHaveCount(1);
  await expect(picker.getByRole('link', { name: 'My shifts', exact: true })).toHaveAttribute('href', '/me/shifts/');
});

test('administrator without an employee record is offered only administration', async ({ page }) => {
  await signIn(page, ['admin'], null);

  const picker = page.getByRole('navigation', { name: 'Choose a section' });
  await expect(picker.getByRole('link')).toHaveCount(1);
  await expect(picker.getByRole('link', { name: 'Continue to administration', exact: true })).toHaveAttribute('href', '/admin/');
});

test('signed-in employee sees notifications first, profile beside them, and sign-out without horizontal scroll', async ({ page, isMobile }) => {
  await signIn(page, ['employee'], { role: 'employee' });

  const notifications = page.getByRole('heading', { name: 'Your notifications', exact: true });
  const profile = page.getByRole('heading', { name: 'Profile', exact: true });
  const preferences = page.getByRole('heading', { name: 'Work preferences', exact: true });
  await expect(notifications).toBeVisible();
  await expect(profile).toBeVisible();
  await expect(preferences).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();

  const n = await notifications.boundingBox();
  const p = await profile.boundingBox();
  if (!n || !p) throw new Error('layout boxes missing');
  if (isMobile) expect(n.y).toBeLessThan(p.y); // phone: notifications first, then profile
  else expect(n.x).toBeLessThan(p.x); // wide screen: notifications in the main column, profile beside it

  // Portal tiles and the sign-out control are at least 44px tall (touch target).
  const tile = await page.getByRole('navigation', { name: 'Choose a section' }).getByRole('link', { name: 'My shifts', exact: true }).boundingBox();
  expect(tile?.height ?? 0).toBeGreaterThanOrEqual(44);
  const signOut = await page.getByRole('button', { name: 'Sign out', exact: true }).boundingBox();
  expect(signOut?.height ?? 0).toBeGreaterThanOrEqual(44);

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  if (process.env.PORTAL_SHOT_DIR) await page.screenshot({ path: `${process.env.PORTAL_SHOT_DIR}/account-employee-${isMobile ? 'mobile' : 'desktop'}.png`, fullPage: true });
});
