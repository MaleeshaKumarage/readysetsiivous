import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Admins and managers see events they should act on in their inbox, and opening one marks it read.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const items = [
  { id: 'a1', kind: 'StaffPartnerAccepted', title: 'Cleaner accepted partner work', body: 'A cleaner accepted a partner cleaning.', link: '/admin/partner-requests/', createdUtc: '2026-11-12T09:00:00Z', read: false },
  { id: 'a2', kind: 'StaffVisitOfferDeclined', title: 'Cleaner declined a visit', body: 'A cleaner declined a visit offer.', link: '/admin/shifts/', createdUtc: '2026-11-11T09:00:00Z', read: true },
];

test('the inbox lists events with the unread count and marks an event read when it is opened', async ({ page }) => {
  await setupAdminMocks(page);
  const state = { read: new Set<string>() };
  await page.route(/\/api\/v1\/me\/inbox(\/[^/]+\/read)?$/, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const path = new URL(request.url()).pathname;
    if (request.method() === 'POST') { state.read.add(path.split('/')[5]); return route.fulfill({ headers, json: { read: true } }); }
    const current = items.map(item => ({ ...item, read: item.read || state.read.has(item.id) }));
    return route.fulfill({ headers, json: { unread: current.filter(item => !item.read).length, items: current } });
  });
  await page.goto('/account/');
  await expect(page.getByRole('heading', { name: /Inbox/ })).toBeVisible();
  await expect(page.getByTestId('inbox-unread')).toContainText('1 unread');
  await expect(page.getByText('A cleaner declined a visit offer.')).toBeVisible();
  await page.getByRole('link', { name: 'Cleaner accepted partner work', exact: true }).click();
  await expect(page.getByTestId('inbox-unread')).toHaveCount(0);
});
