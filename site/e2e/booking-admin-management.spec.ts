import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function fixtures(): any[] {
  const base = (id: string, bookingNumber: string, status: string, employeeName: string | null = null) => ({
    id, bookingNumber, status, employeeName,
    customer: { name: `Customer ${bookingNumber}`, phone: '+358 40 000 0000', email: `${id}@example.invalid` },
    service: { nameFi: 'Kotisiivous', durationMinutes: 120, priceNet: 50 },
    startLocalDate: '2026-11-10', startLocalTime: '09:00',
  });
  return [base('b-new', 'BK-1001', 'New'), base('b-confirmed', 'BK-1002', 'Confirmed', 'Test Cleaner'), base('b-completed', 'BK-1003', 'Completed', 'Test Cleaner'), base('b-cancelled', 'BK-1004', 'Cancelled')];
}

async function mockBookings(page: any, bookings: any[], options: { failAction?: string } = {}) {
  const actions: { id: string; action: string }[] = [];
  const queries: string[] = [];
  await page.route(/\/api\/v1\/admin\/bookings(?:\?.*)?$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    const status = url.searchParams.get('status');
    queries.push(url.search);
    const rows = status ? bookings.filter(b => b.status === status) : bookings;
    return route.fulfill({ status: 200, headers, json: rows });
  });
  await page.route(/\/api\/v1\/admin\/bookings\/([^/]+)\/(confirm|cancel|complete)$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const [, id, action] = route.request().url().match(/bookings\/([^/]+)\/(confirm|cancel|complete)$/) ?? [];
    actions.push({ id, action });
    if (options.failAction === action) return route.fulfill({ status: 409, headers, json: { detail: 'Booking is cancelled' } });
    const next = action === 'confirm' ? 'Confirmed' : action === 'cancel' ? 'Cancelled' : 'Completed';
    const booking = bookings.find(b => b.id === id);
    if (booking) booking.status = next;
    return route.fulfill({ status: 204, headers });
  });
  return { actions, queries };
}

test('admin lists bookings and filters by status on the server', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const { queries } = await mockBookings(page, fixtures());
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1004', exact: true })).toBeVisible();

  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'Confirmed', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'BK-1002', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toHaveCount(0);
  expect(queries.some(q => q.includes('status=Confirmed'))).toBe(true);
});

test('new booking offers confirm and cancel, and confirming updates the row', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  const { actions } = await mockBookings(page, bookings);
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1001/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Confirm', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancel booking', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Mark completed', exact: true })).toHaveCount(0);

  await page.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('BK-1001 is now Confirmed.');
  await expect(page.getByRole('row', { name: /BK-1001/ })).toContainText('Confirmed');
  expect(actions).toEqual([{ id: 'b-new', action: 'confirm' }]);
});

test('admin cancels a new booking once and the status and row update', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  const { actions } = await mockBookings(page, bookings);
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1001/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel booking', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('BK-1001 is now Cancelled.');
  await expect(page.getByRole('row', { name: /BK-1001/ })).toContainText('Cancelled');
  expect(actions).toEqual([{ id: 'b-new', action: 'cancel' }]);
});

test('confirmed booking can be completed and then offers no further actions', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  const { actions } = await mockBookings(page, bookings);
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1002/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Confirm', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Mark completed', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('BK-1002 is now Completed.');
  expect(actions).toEqual([{ id: 'b-confirmed', action: 'complete' }]);

  await page.getByRole('row', { name: /BK-1002/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await expect(page.getByText('No further changes are available.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancel booking', exact: true })).toHaveCount(0);
});

test('completed and cancelled bookings expose no change actions', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures());
  await page.goto('/admin/bookings/');
  for (const number of ['BK-1003', 'BK-1004']) {
    await page.getByRole('row', { name: new RegExp(number) }).getByRole('button', { name: 'Details', exact: true }).click();
    await expect(page.getByText('No further changes are available.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Cancel booking', exact: true })).toHaveCount(0);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
  }
});

test('failed action keeps the booking status and shows a recoverable error', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  await mockBookings(page, bookings, { failAction: 'confirm' });
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1001/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not confirm' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirm', exact: true })).toBeVisible();
  expect(bookings.find(b => b.id === 'b-new').status).toBe('New');
});

test('manager is blocked from the bookings administration page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['manager']; });
  await mockBookings(page, fixtures());
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toHaveCount(0);
});
