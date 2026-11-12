import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Admins and managers open notifications from a bell on the account page. The drawer lists events,
// each event opens the page it is about, and opening one marks it read.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const items = [
  { id: 'a1', kind: 'StaffPartnerAccepted', title: 'Cleaner accepted partner work', body: 'A cleaner accepted a partner cleaning.', link: '/admin/partner-requests/', createdUtc: '2026-11-12T09:00:00Z', read: false },
  { id: 'a2', kind: 'StaffVisitOfferDeclined', title: 'Cleaner declined a visit', body: 'A cleaner declined a visit offer. Assign another cleaner. Visit: Lakeside Villa, 14 Nov 08:00.', link: '/admin/shifts/', createdUtc: '2026-11-11T09:00:00Z', read: true },
];

async function mockInbox(page: Page) {
  const state = { read: new Set<string>() };
  await page.route(/\/api\/v1\/me\/inbox(\/[^/]+\/read)?$/, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const path = new URL(request.url()).pathname;
    if (request.method() === 'POST') { state.read.add(path.split('/')[5]); return route.fulfill({ headers, json: { read: true } }); }
    const current = items.map(item => ({ ...item, read: item.read || state.read.has(item.id) }));
    return route.fulfill({ headers, json: { unread: current.filter(item => !item.read).length, items: current } });
  });
  return state;
}

test('the bell shows the unread count and opens a drawer listing notifications', async ({ page }) => {
  await setupAdminMocks(page);
  await mockInbox(page);
  await page.goto('/account/');

  const bell = page.getByRole('button', { name: 'Notifications, 1 unread', exact: true });
  await expect(bell).toBeVisible();
  await bell.click();
  const drawer = page.getByRole('dialog', { name: 'Notifications', exact: true });
  await expect(drawer).toBeVisible();
  await expect(drawer.getByText('1 unread')).toBeVisible();
  await expect(drawer.getByText('A cleaner declined a visit offer.')).toBeVisible();
  await expect(drawer.getByRole('link', { name: /Cleaner accepted partner work/ })).toHaveAttribute('href', '/admin/partner-requests/');
  if (process.env.PORTAL_SHOT_DIR) await page.screenshot({ path: `${process.env.PORTAL_SHOT_DIR}/notification-drawer.png` });
});

test('opening a notification goes to the page it is about and marks it read', async ({ page }) => {
  await setupAdminMocks(page);
  const state = await mockInbox(page);
  await page.goto('/account/');
  await page.waitForLoadState('networkidle');

  await page.getByRole('button', { name: 'Notifications, 1 unread', exact: true }).click();
  await page.getByRole('dialog', { name: 'Notifications', exact: true }).getByRole('link', { name: /Cleaner accepted partner work/ }).click();
  await expect(page).toHaveURL(/\/admin\/partner-requests\/$/);
  await expect.poll(() => state.read.has('a1')).toBe(true);
});

test('Escape closes the drawer and returns focus to the bell', async ({ page }) => {
  await setupAdminMocks(page);
  await mockInbox(page);
  await page.goto('/account/');

  const bell = page.getByRole('button', { name: /^Notifications/ });
  await bell.click();
  await expect(page.getByRole('dialog', { name: 'Notifications', exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Notifications', exact: true })).toHaveCount(0);
  await expect(bell).toBeFocused();
});
