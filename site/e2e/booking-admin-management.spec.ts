import { test, expect } from '@playwright/test';
import { setupAdminMocks, setNavOpen } from './admin-helpers';

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
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 200, headers, json: [{ id: 'e-1', firstName: 'Ada', lastName: 'Cleaner', role: 'employee', isActive: true }] });
  });
  await page.route(/\/api\/v1\/admin\/bookings\/([^/]+)\/(confirm|cancel|complete|assign|unassign)$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const [, id, action] = route.request().url().match(/bookings\/([^/]+)\/(confirm|cancel|complete|assign|unassign)$/) ?? [];
    actions.push({ id, action });
    if (options.failAction === action) return route.fulfill({ status: 409, headers, json: { detail: 'Booking is cancelled' } });
    const booking = bookings.find(b => b.id === id);
    if (action === 'assign') { if (booking) booking.employeeName = 'Ada Cleaner'; return route.fulfill({ status: 204, headers }); }
    if (action === 'unassign') { if (booking) booking.employeeName = null; return route.fulfill({ status: 204, headers }); }
    const next = action === 'confirm' ? 'Confirmed' : action === 'cancel' ? 'Cancelled' : 'Completed';
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

test('admin cancels a confirmed booking once and the status and row update', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  const { actions } = await mockBookings(page, bookings);
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1002/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel booking', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('BK-1002 is now Cancelled.');
  await expect(page.getByRole('row', { name: /BK-1002/ })).toContainText('Cancelled');
  expect(actions).toEqual([{ id: 'b-confirmed', action: 'cancel' }]);
});

test('Bookings navigation link is listed under Operations and opens the bookings page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures());
  await page.goto('/admin/');
  await setNavOpen(page, true);
  const link = page.locator('#admin-navigation').getByRole('link', { name: 'Bookings', exact: true });
  await expect(link).toHaveAttribute('href', /\/admin\/bookings\/?$/);
  await link.click();
  await expect(page).toHaveURL(/\/admin\/bookings\/?$/);
  await expect(page.getByRole('heading', { name: 'Bookings', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();
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

test('empty list shows the no-bookings message and no rows', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, []);
  await page.goto('/admin/bookings/');
  await expect(page.getByText('No bookings.', { exact: true })).toBeVisible();
  await expect(page.getByRole('row', { name: /BK-/ })).toHaveCount(0);
});

test('filter that matches nothing shows the status-specific empty message', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures().filter(b => b.status !== 'Completed'));
  await page.goto('/admin/bookings/');
  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'Completed', exact: true }).click();
  await expect(page.getByText('No bookings with status Completed.', { exact: true })).toBeVisible();
  await expect(page.getByRole('row', { name: /BK-/ })).toHaveCount(0);
});

test('a failed list load shows a recoverable alert instead of a table', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/bookings(?:\?.*)?$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 500, headers, json: { detail: 'Database unavailable' } });
  });
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('alert').filter({ hasText: 'Bookings could not be loaded. Reload and retry.' })).toBeVisible();
  await expect(page.getByRole('row', { name: /BK-/ })).toHaveCount(0);
  await expect(page.locator('.mantine-Loader-root')).toHaveCount(0);
});

test('switching the status filter back to All statuses drops the filter and restores the full list', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const { queries } = await mockBookings(page, fixtures());
  await page.goto('/admin/bookings/');
  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'Confirmed', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'New', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'All statuses', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1002', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1004', exact: true })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Filter by status', exact: true })).toHaveValue('All statuses');
  expect(queries.some(q => q.includes('status=Confirmed'))).toBe(true);
  expect(queries.some(q => q.includes('status=New'))).toBe(true);
});

test('a slow earlier filter response cannot overwrite the rows of a newer filter', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures());
  let releaseConfirmed!: () => void;
  const confirmedGate = new Promise<void>(resolve => { releaseConfirmed = resolve; });
  await page.route(/\/api\/v1\/admin\/bookings(?:\?.*)?$/, async (route: any) => {
    const url = new URL(route.request().url());
    if (route.request().method() !== 'GET' || url.searchParams.get('status') !== 'Confirmed') return route.fallback();
    await confirmedGate;
    return route.fulfill({ status: 200, headers, json: fixtures().filter(b => b.status === 'Confirmed') });
  });
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();

  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'Confirmed', exact: true }).click();
  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'New', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();

  releaseConfirmed();
  await page.waitForResponse(response => response.url().includes('status=Confirmed'));
  await expect(page.getByRole('cell', { name: 'BK-1002', exact: true })).toHaveCount(0);
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();
});

test('a failed filter change shows only the alert, never stale rows or the empty message', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures());
  await page.route(/\/api\/v1\/admin\/bookings\?status=Completed$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 500, headers, json: { detail: 'Database unavailable' } });
  });
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toBeVisible();

  await page.getByRole('textbox', { name: 'Filter by status', exact: true }).click();
  await page.getByRole('option', { name: 'Completed', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Bookings could not be loaded. Reload and retry.' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toHaveCount(0);
  await expect(page.getByText('No bookings with status Completed.', { exact: true })).toHaveCount(0);
  await expect(page.getByText('No bookings.', { exact: true })).toHaveCount(0);
});

test('a network failure on load clears the spinner and shows the alert', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/bookings(?:\?.*)?$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.abort('failed');
  });
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('alert').filter({ hasText: 'Bookings could not be loaded. Reload and retry.' })).toBeVisible();
  await expect(page.locator('.mantine-Loader-root')).toHaveCount(0);
  await expect(page.getByRole('row', { name: /BK-/ })).toHaveCount(0);
});

test('admin assigns an open booking to an employee and the row shows the assignee', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  const { actions } = await mockBookings(page, bookings);
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1001/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Unassign', exact: true })).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Assign to', exact: true }).click();
  await page.getByRole('option', { name: 'Ada Cleaner', exact: true }).click();
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('BK-1001 assigned to Ada Cleaner.');
  await expect(page.getByRole('row', { name: /BK-1001/ })).toContainText('Ada Cleaner');
  expect(actions).toEqual([{ id: 'b-new', action: 'assign' }]);
});

test('admin unassigns a confirmed booking and the row shows it as unassigned', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  const { actions } = await mockBookings(page, bookings);
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1002/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await page.getByRole('button', { name: 'Unassign', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('BK-1002 is unassigned.');
  await expect(page.getByRole('row', { name: /BK-1002/ })).toContainText('Unassigned');
  expect(actions).toEqual([{ id: 'b-confirmed', action: 'unassign' }]);
});

test('terminal bookings offer no assignment controls', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures());
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1003/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Assign to', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Unassign', exact: true })).toHaveCount(0);
});

test('failed assignment keeps the booking unassigned and shows a recoverable error', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const bookings = fixtures();
  await mockBookings(page, bookings, { failAction: 'assign' });
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1001/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await page.getByRole('textbox', { name: 'Assign to', exact: true }).click();
  await page.getByRole('option', { name: 'Ada Cleaner', exact: true }).click();
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not assign the booking' })).toBeVisible();
  expect(bookings.find(b => b.id === 'b-new').employeeName).toBeNull();
});

test('a failed employee list shows an alert in the assign control instead of an empty picker', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockBookings(page, fixtures());
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 500, headers, json: { detail: 'Employee service unavailable' } });
  });
  await page.goto('/admin/bookings/');
  await page.getByRole('row', { name: /BK-1001/ }).getByRole('button', { name: 'Details', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Employees could not be loaded, so bookings cannot be assigned.' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Assign', exact: true })).toBeDisabled();
});

test('manager is blocked from the bookings administration page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['manager']; });
  await mockBookings(page, fixtures());
  await page.goto('/admin/bookings/');
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'BK-1001', exact: true })).toHaveCount(0);
});
