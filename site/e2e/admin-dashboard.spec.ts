import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The Dashboard shows what needs attention before the menu. A failed read offers a retry instead of a zero.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('the dashboard shows the attention counts and links each one to its list', async ({ page }, testInfo) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/work-queue/, route => route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: [], total: 0, counts: { 'needs-action': 3, scheduled: 5, 'in-progress': 0, completed: 0, cancelled: 0 } } }));
  await page.route(/\/api\/v1\/me\/inbox$/, route => route.fulfill({ headers, json: { unread: 2, items: [] } }));
  await page.goto('/admin/');
  await expect(page.getByRole('heading', { name: 'What needs attention' })).toBeVisible();
  await expect(page.getByLabel('Visits needing action: 3')).toBeVisible();
  await expect(page.getByLabel('Scheduled visits: 5')).toBeVisible();
  await expect(page.getByLabel('Unread notifications: 2')).toBeVisible();
  await expect(page.getByRole('link', { name: /Unread notifications/ })).toHaveAttribute('href', '/account/');
  await expect(page.getByRole('link', { name: /Partner actions/ })).toHaveAttribute('href', '/admin/partner-requests/');
  const shot = process.env.DASHBOARD_SHOT_DIR;
  if (shot) await page.screenshot({ path: `${shot}/dashboard-after-${testInfo.project.name}.png`, fullPage: testInfo.project.name !== 'chromium-desktop' });
});

test('a failed status read shows a retry instead of zero counts', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/work-queue/, route => route.fulfill({ status: 500, headers, json: { detail: 'down' } }));
  await page.route(/\/api\/v1\/me\/inbox$/, route => route.fulfill({ headers, json: { unread: 0, items: [] } }));
  await page.goto('/admin/');
  await expect(page.getByRole('button', { name: 'Retry status' })).toBeVisible();
  await expect(page.getByLabel('Visits needing action: unavailable')).toBeVisible();
  await expect(page.getByLabel('Visits needing action: 0')).toHaveCount(0);
});
