import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS' };

function booking(overrides: Record<string, unknown> = {}) {
  return {
    id: 'booking-1', bookingNumber: 'BK-1001', status: 'New', startLocalDate: '2026-11-10', startLocalTime: '09:00',
    customer: { name: 'Anna Virtanen', phone: '+358401234567', email: 'anna@example.test' },
    service: { nameFi: 'Kotisiivous', durationMinutes: 120, priceNet: 80 }, employeeName: null,
    ...overrides,
  };
}

type QueueItem = { source: 'booking' | 'partner' | 'shift'; id: string; reference: string; customerName: string; scheduledLocal: string; status: string; stateLabel: string; assigneeName: string | null; href: string; visit?: Record<string, unknown> };
const bookingItem: QueueItem = { source: 'booking', id: 'booking-1', reference: 'BK-1001', customerName: 'Anna Virtanen', scheduledLocal: '2026-11-10 09:00', status: 'New', stateLabel: 'New booking', assigneeName: null, href: '/admin/work-queue/?booking=booking-1' };

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  const state = {
    bookings: { 'booking-1': booking() } as Record<string, ReturnType<typeof booking>>,
    queries: [] as URLSearchParams[],
    actions: [] as string[],
    listFails: false,
    counts: { 'needs-action': 1, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 },
    // Items per view; the mock pages them with skip/take like the server.
    items: { 'needs-action': [bookingItem] } as Record<string, QueueItem[]>,
  };
  await page.route('**/api/v1/admin/work-queue**', route => {
    const url = new URL(route.request().url());
    state.queries.push(url.searchParams);
    if (state.listFails) return route.fulfill({ headers, status: 503, json: { detail: 'Queue unavailable' } });
    const view = url.searchParams.get('view') ?? 'needs-action';
    const all = (state.items[view] ?? []).filter(item => item.source === url.searchParams.get('source'));
    const skip = Number(url.searchParams.get('skip') ?? 0), take = Number(url.searchParams.get('take') ?? 25);
    return route.fulfill({ headers, json: { view, source: url.searchParams.get('source') ?? 'all', items: all.slice(skip, skip + take), total: all.length, counts: state.counts } });
  });
  await page.route('**/api/v1/admin/bookings/**', route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    const match = /\/api\/v1\/admin\/bookings\/([^/]+)(?:\/(confirm|cancel|complete))?$/.exec(path);
    if (!match) return route.fulfill({ headers, status: 404, json: {} });
    const [, id, action] = match;
    if (action) {
      state.actions.push(action);
      const next = action === 'confirm' ? 'Confirmed' : action === 'complete' ? 'Completed' : 'Cancelled';
      state.bookings[id] = booking({ ...state.bookings[id], status: next });
      return route.fulfill({ headers, status: 204 });
    }
    return route.fulfill({ headers, json: state.bookings[id] });
  });
  return state;
}

// The Work queue lists only unassigned work, merged from bookings, company visits and partner requests, and every
// item is handled in its side panel. Assigned work is in the Scheduler; finished bookings are on the Bookings page.
const toast = (page: Page, text: string | RegExp) => page.locator('.mantine-Notification-root').filter({ hasText: text });

test('the queue asks only for unassigned needs-action work, has no status tabs, and searches in place', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await expect(page.getByRole('tab')).toHaveCount(0);
  expect(state.queries.every(query => query.get('view') === 'needs-action' && query.get('unassigned') === 'true')).toBe(true);
  const before = state.queries.length;
  await page.getByRole('searchbox', { name: 'Search' }).fill('nobody');
  await expect(page.getByText('Nothing waiting matches “nobody”.')).toBeVisible();
  await page.getByRole('searchbox', { name: 'Search' }).fill('anna');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  expect(state.queries.length).toBe(before);
  expect(page.url()).toContain('q=anna');
});

test('the booking panel confirms a new booking and refreshes the queue', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await page.getByRole('button', { name: 'Assign cleaning BK-1001' }).click();
  const drawer = page.getByRole('dialog', { name: 'Booking BK-1001' });
  await expect(drawer.getByText('+358401234567 · anna@example.test')).toBeVisible();
  await drawer.getByRole('button', { name: 'Confirm booking', exact: true }).click();
  await expect.poll(() => state.actions).toEqual(['confirm']);
  await expect(toast(page, 'Booking BK-1001 is confirmed. Assign an employee next.')).toBeVisible();
  await expect(drawer).toHaveCount(0);
});

test('queue failure shows a retry that recovers the list', async ({ page }) => {
  const state = await setup(page);
  state.listFails = true;
  await page.route('**/api/v1/admin/partner/**', route => route.fulfill({ headers, status: 503, json: {} }));
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('alert').filter({ hasText: 'could not be loaded' })).toBeVisible();
  state.listFails = false;
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
});

test('the booking panel cancels a new booking only after its confirmation step', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await page.getByRole('button', { name: 'Assign cleaning BK-1001' }).click();
  const drawer = page.getByRole('dialog', { name: 'Booking BK-1001' });
  await drawer.getByRole('button', { name: 'Cancel booking', exact: true }).click();
  // Keeping it sends nothing.
  await drawer.getByRole('alertdialog').getByRole('button', { name: 'Keep' }).click();
  expect(state.actions).toEqual([]);
  await drawer.getByRole('button', { name: 'Cancel booking', exact: true }).click();
  await drawer.getByRole('alertdialog').getByRole('button', { name: 'Cancel booking', exact: true }).click();
  await expect.poll(() => state.actions).toEqual(['cancel']);
  await expect(toast(page, 'Booking BK-1001 is cancelled.')).toBeVisible();
});

test('a page past the end is clamped to the last page', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/work-queue/?page=9');
  await expect(page.getByText('Page 1 of 1', { exact: true })).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
});

test('a ?booking= deep link opens that booking panel on load', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/work-queue/?booking=booking-1');
  await expect(page.getByRole('dialog', { name: 'Booking BK-1001' })).toBeVisible();
});

test('an ended company visit nobody started is overdue, marked, and assigned in its panel instead of opening another page', async ({ page }) => {
  const state = await setup(page);
  state.items['needs-action'] = [{ source: 'shift', id: 'occ-1', reference: 'Weekly office', customerName: 'Acme Oy', scheduledLocal: '2026-11-02 08:00', status: 'needs-action',
    stateLabel: 'Overdue · not started', assigneeName: null, href: '/admin/shifts/scheduler/',
    visit: { shiftId: 'shift-1', startUtc: '2026-11-02T06:00:00Z', endUtc: '2026-11-02T08:00:00Z', originalStartUtc: null, exceptionId: null, extraExceptionId: null, status: null,
      visitEmployeeId: null, rosterVersion: 'r1', assignmentVersion: null, isBranchMain: false, branchName: 'Acme HQ', cleaningType: null, notes: null, offeredEmployeeId: null, offerRevision: 0 } }];
  await page.goto('/admin/work-queue/');
  const row = page.getByRole('listitem').filter({ hasText: 'Acme Oy' });
  await expect(row).toHaveClass(/alert/);
  await expect(row.locator('.chip')).toHaveText(['Admin', 'Cleaning', 'Normal', 'Overdue · not started']);
  await expect(row.getByRole('link')).toHaveCount(0);
  await row.getByRole('button', { name: 'Assign cleaning Acme HQ, Weekly office' }).click();
  await expect(page.getByRole('dialog', { name: 'Acme HQ, Weekly office' })).toBeVisible();
});
