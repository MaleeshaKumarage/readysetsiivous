import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Employee profile: the Details and Work preferences cards carry the gold edge, and the role reads "Employee".
// Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Authorization,Content-Type', 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS' };

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers, json: { realm: 'tenant-a', employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ status: 200, headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/notification-preferences(\/[^/]+)?$/, route => route.fulfill({ status: 200, headers, json: [] }));
}

test('the employee Details and Work preferences cards have the gold edge', async ({ page }) => {
  await setup(page);
  await page.goto('/account/profile/?tenant=tenant-a');
  for (const id of ['profile-details-heading', 'preferences-heading']) {
    const card = page.locator(`#${id}`).locator('xpath=..');
    const width = await card.evaluate(el => parseFloat(getComputedStyle(el).borderLeftWidth));
    expect(width).toBeGreaterThanOrEqual(4);
  }
});

test('the role reads Employee, not Employee', async ({ page }) => {
  await setup(page);
  await page.goto('/account/profile/?tenant=tenant-a');
  await expect(page.getByText('Roles: Employee', { exact: true })).toBeVisible();
  await expect(page.getByText('Employee', { exact: true })).toHaveCount(0);
});

test('the profile does not widen a phone', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await page.goto('/account/profile/?tenant=tenant-a');
  await expect(page.locator('#profile-details-heading')).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});
