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

type QueueItem = { source: 'booking' | 'partner' | 'shift'; id: string; reference: string; customerName: string; scheduledLocal: string; status: string; stateLabel: string; assigneeName: string | null; href: string };
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
    const all = state.items[view] ?? [];
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

test('work queue shows needs-action booking with view counts and filters sent to the server', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Needs action (1)' })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('tab', { name: /Completed/ }).click();
  await expect.poll(() => state.queries.at(-1)?.get('view')).toBe('completed');
  await expect(page.getByText('Nothing in this view.')).toBeVisible();
  await page.getByRole('textbox', { name: 'Search' }).fill('Anna');
  await expect.poll(() => state.queries.at(-1)?.get('q')).toBe('Anna');
  await page.getByRole('textbox', { name: 'Source' }).click();
  await page.getByRole('option', { name: 'Partner requests' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('source')).toBe('partner');
  expect(page.url()).toContain('view=completed');
});

test('booking drawer confirms a new booking and refreshes the queue', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await page.getByRole('button', { name: 'Open booking BK-1001' }).click();
  const drawer = page.getByRole('dialog', { name: 'Booking BK-1001' });
  await expect(drawer.getByText('Anna Virtanen')).toBeVisible();
  await drawer.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect.poll(() => state.actions).toEqual(['confirm']);
  await expect(drawer.getByText('Confirmed')).toBeVisible();
  await expect(drawer.getByRole('button', { name: 'Confirm', exact: true })).toHaveCount(0);
});

test('queue failure shows a retry that recovers the list', async ({ page }) => {
  const state = await setup(page);
  state.listFails = true;
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('alert').filter({ hasText: 'could not be loaded' })).toBeVisible();
  state.listFails = false;
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
});

test('booking drawer cancels a new booking after the confirmation dialog', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await page.getByRole('button', { name: 'Open booking BK-1001' }).click();
  const drawer = page.getByRole('dialog', { name: 'Booking BK-1001' });
  // Dismissing the dialog sends nothing.
  page.once('dialog', dialog => { expect(dialog.message()).toBe('Cancel booking BK-1001?'); void dialog.dismiss(); });
  await drawer.getByRole('button', { name: 'Cancel booking', exact: true }).click();
  await expect(drawer.getByText('Status: New')).toBeVisible();
  expect(state.actions).toEqual([]);
  page.once('dialog', dialog => void dialog.accept());
  await drawer.getByRole('button', { name: 'Cancel booking', exact: true }).click();
  await expect.poll(() => state.actions).toEqual(['cancel']);
  await expect(drawer.getByText('Status: Cancelled')).toBeVisible();
  await expect(drawer.getByRole('button', { name: 'Cancel booking', exact: true })).toHaveCount(0);
});

test('booking drawer marks a confirmed booking completed', async ({ page }) => {
  const state = await setup(page);
  state.bookings['booking-1'] = booking({ status: 'Confirmed' });
  state.counts = { 'needs-action': 0, scheduled: 1, 'in-progress': 0, completed: 0, cancelled: 0 };
  state.items = { scheduled: [{ ...bookingItem, status: 'Confirmed', stateLabel: 'Confirmed' }] };
  await page.goto('/admin/work-queue/?view=scheduled');
  await page.getByRole('button', { name: 'Open booking BK-1001' }).click();
  const drawer = page.getByRole('dialog', { name: 'Booking BK-1001' });
  await expect(drawer.getByRole('button', { name: 'Confirm', exact: true })).toHaveCount(0);
  await drawer.getByRole('button', { name: 'Mark completed', exact: true }).click();
  await expect.poll(() => state.actions).toEqual(['complete']);
  await expect(drawer.getByText('Status: Completed')).toBeVisible();
  await expect(drawer.getByRole('button', { name: 'Mark completed', exact: true })).toHaveCount(0);
});

test('Scheduled and In progress tabs request their view from the server', async ({ page }) => {
  const state = await setup(page);
  state.counts = { 'needs-action': 1, scheduled: 3, 'in-progress': 2, completed: 0, cancelled: 0 };
  await page.goto('/admin/work-queue/');
  await page.getByRole('tab', { name: 'Scheduled (3)' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('view')).toBe('scheduled');
  await expect(page.getByRole('tab', { name: 'Scheduled (3)' })).toHaveAttribute('aria-selected', 'true');
  expect(page.url()).toContain('view=scheduled');
  await page.getByRole('tab', { name: 'In progress (2)' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('view')).toBe('in-progress');
  await expect(page.getByRole('tab', { name: 'In progress (2)' })).toHaveAttribute('aria-selected', 'true');
  expect(page.url()).toContain('view=in-progress');
});

test('Next and Previous move between pages and re-fetch with the page offset', async ({ page }) => {
  const state = await setup(page);
  state.counts = { 'needs-action': 30, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 };
  state.items = { 'needs-action': Array.from({ length: 30 }, (_, index) => ({ ...bookingItem, id: `booking-${index + 1}`, reference: `BK-${2001 + index}`, customerName: `Customer ${index + 1}` })) };
  await page.goto('/admin/work-queue/');
  await expect(page.getByText('Showing 1–25 of 30')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Previous' })).toBeDisabled();
  await page.getByRole('button', { name: 'Next' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('skip')).toBe('25');
  await expect(page.getByText('Showing 26–30 of 30')).toBeVisible();
  await expect(page.getByText('Page 2 of 2')).toBeVisible();
  await expect(page.getByRole('listitem')).toHaveCount(5);
  expect(new URL(page.url()).searchParams.get('page')).toBe('2');
  await expect(page.getByRole('button', { name: 'Next' })).toBeDisabled();
  await page.getByRole('button', { name: 'Previous' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('skip')).toBe('0');
  await expect(page.getByText('Showing 1–25 of 30')).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('1');
});

test('a page past the end is clamped to the last page', async ({ page }) => {
  const state = await setup(page);
  state.items = { 'needs-action': Array.from({ length: 30 }, (_, index) => ({ ...bookingItem, id: `booking-${index + 1}`, reference: `BK-${2001 + index}`, customerName: `Customer ${index + 1}` })) };
  await page.goto('/admin/work-queue/?page=5');
  await expect(page.getByText('Page 2 of 2')).toBeVisible();
  expect(new URL(page.url()).searchParams.get('page')).toBe('2');
});

test('partner request items open the partner requests page', async ({ page }) => {
  const state = await setup(page);
  state.items = { 'needs-action': [{ source: 'partner', id: 'request-1', reference: 'Lake apartment', customerName: 'Owner company', scheduledLocal: '2026-11-12 10:00', status: 'Offered', stateLabel: 'Awaiting acceptance', assigneeName: null, href: '/admin/partner-requests/' }] };
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('listitem').filter({ hasText: 'Partner request' })).toBeVisible();
  await page.getByRole('link', { name: 'Open partner request for Owner company' }).click();
  await expect(page).toHaveURL(/\/admin\/partner-requests\/?$/);
});

test('a ?booking= deep link opens that booking drawer on load', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/work-queue/?booking=booking-1');
  const drawer = page.getByRole('dialog', { name: 'Booking BK-1001' });
  await expect(drawer).toBeVisible();
  await expect(drawer.getByText('Anna Virtanen')).toBeVisible();
  await expect(drawer.getByRole('button', { name: 'Confirm', exact: true })).toBeVisible();
});

test('Public bookings source sends source=booking and All sources sends source=all', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Source' }).click();
  await page.getByRole('option', { name: 'Public bookings' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('source')).toBe('booking');
  expect(new URL(page.url()).searchParams.get('source')).toBe('booking');
  await page.getByRole('textbox', { name: 'Source' }).click();
  await page.getByRole('option', { name: 'All sources' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('source')).toBe('all');
});

test('cancelled bookings have their own tab and never appear under Completed', async ({ page }) => {
  const state = await setup(page);
  const cancelled: QueueItem = { ...bookingItem, id: 'booking-9', reference: 'BK-9009', customerName: 'Cancelled Customer', status: 'Cancelled', stateLabel: 'Cancelled', href: '/admin/work-queue/?booking=booking-9' };
  state.counts = { ...state.counts, completed: 1, cancelled: 1 };
  state.items = { ...state.items, completed: [{ ...bookingItem, id: 'booking-7', reference: 'BK-7007', customerName: 'Done Customer', status: 'Completed', stateLabel: 'Completed' }], cancelled: [cancelled] };
  await page.goto('/admin/work-queue/');
  await page.getByRole('tab', { name: /^Completed/ }).click();
  await expect(page.getByText('Done Customer', { exact: true })).toBeVisible();
  await expect(page.getByText('Cancelled Customer', { exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Cancelled/ }).click();
  await expect(page.getByText('Cancelled Customer', { exact: true })).toBeVisible();
  await expect(page.getByText('Done Customer', { exact: true })).toHaveCount(0);
  expect(state.queries.at(-1)?.get('view')).toBe('cancelled');
});

test('company shifts show in the same queue with their own source filter, including future and completed visits', async ({ page }) => {
  const state = await setup(page);
  const shiftItem: QueueItem = { source: 'shift', id: 'occurrence-1', reference: 'Weekly office clean', customerName: 'Owner company', scheduledLocal: '2026-11-12 10:00', status: 'scheduled', stateLabel: 'Scheduled', assigneeName: null, href: '/admin/shifts/calendar/' };
  const doneShift: QueueItem = { ...shiftItem, id: 'occurrence-2', scheduledLocal: '2026-10-20 10:00', status: 'completed', stateLabel: 'Completed' };
  state.counts = { 'needs-action': 1, scheduled: 1, 'in-progress': 0, completed: 1, cancelled: 0 };
  state.items = { ...state.items, scheduled: [shiftItem], completed: [doneShift] };
  await page.goto('/admin/work-queue/');
  await page.getByRole('tab', { name: /^Scheduled/ }).click();
  await expect(page.getByText(/Weekly office clean/)).toBeVisible();
  await expect(page.getByText(/Company shift/).first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open company shift Weekly office clean', exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'Source' }).click();
  await page.getByRole('option', { name: 'Company shifts' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('source')).toBe('shift');
  await page.getByRole('tab', { name: /^Completed/ }).click();
  await expect(page.getByText(/Weekly office clean/)).toBeVisible();
});


test('a past assigned company visit nobody started reads Not started, and Open company shift opens the calendar', async ({ page }) => {
  const state = await setup(page);
  const missed: QueueItem = { source: 'shift', id: 'occurrence-missed', reference: 'Weekly office clean', customerName: 'Owner company', scheduledLocal: '2026-10-20 10:00', status: 'scheduled', stateLabel: 'Not started', assigneeName: 'Test Cleaner', href: '/admin/shifts/calendar/' };
  const upcoming: QueueItem = { ...missed, id: 'occurrence-upcoming', scheduledLocal: '2026-11-12 10:00', stateLabel: 'Scheduled' };
  state.counts = { 'needs-action': 1, scheduled: 2, 'in-progress': 0, completed: 0, cancelled: 0 };
  state.items = { ...state.items, scheduled: [missed, upcoming] };
  await page.goto('/admin/work-queue/');
  await page.getByRole('tab', { name: /^Scheduled/ }).click();
  const missedRow = page.getByRole('link', { name: 'Open company shift Weekly office clean', exact: true }).first();
  await expect(page.getByText('Not started', { exact: true })).toBeVisible();
  await expect(page.getByText('Scheduled', { exact: true })).toBeVisible();
  // Unstarted work stays in Scheduled with a red border; upcoming work keeps the plain row.
  await expect(page.getByRole('listitem').filter({ hasText: '2026-10-20 10:00' })).toHaveAttribute('data-state', 'not-started');
  await expect(page.getByRole('listitem').filter({ hasText: '2026-11-12 10:00' })).not.toHaveAttribute('data-state', 'not-started');
  await missedRow.focus(); await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/admin\/shifts\/calendar\/?$/);
});

test('date range and assignee filters are sent to the server and restored from the URL', async ({ page }) => {
  const state = await setup(page);
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'emp-1', firstName: 'Liisa', lastName: 'Korhonen', email: 'liisa@example.test', isActive: true }] }));
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();

  await page.getByLabel('From date', { exact: true }).fill('2026-11-01');
  await expect.poll(() => state.queries.at(-1)?.get('from')).toBe('2026-11-01');
  await page.getByLabel('To date', { exact: true }).fill('2026-11-30');
  await expect.poll(() => state.queries.at(-1)?.get('to')).toBe('2026-11-30');
  await expect(page.getByRole('tab', { name: 'Needs action (1)' })).toHaveAttribute('aria-selected', 'true');
  expect(new URL(page.url()).searchParams.get('from')).toBe('2026-11-01');
  expect(new URL(page.url()).searchParams.get('to')).toBe('2026-11-30');

  await page.getByRole('textbox', { name: 'Assignee', exact: true }).click();
  await page.getByRole('option', { name: 'Liisa Korhonen' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('assignee')).toBe('emp-1');
  await expect(page.getByText('Partner requests have no assignee, so they are hidden while this filter is on.')).toBeVisible();

  await page.reload();
  await expect.poll(() => state.queries.at(-1)?.get('assignee')).toBe('emp-1');
  await expect(page.getByRole('textbox', { name: 'Assignee', exact: true })).toHaveValue('Liisa Korhonen');
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('2026-11-01');
  await expect(page.getByLabel('To date', { exact: true })).toHaveValue('2026-11-30');
});

test('a reversed date range is dropped instead of sent to the server', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/?from=2026-12-05&to=2026-12-01');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  expect(state.queries.at(-1)?.has('from')).toBe(false);
  expect(state.queries.at(-1)?.has('to')).toBe(false);
});

test('clearing the assignee removes the filter, its URL param and the partner note', async ({ page }) => {
  const state = await setup(page);
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'emp-1', firstName: 'Liisa', lastName: 'Korhonen', email: 'liisa@example.test', isActive: true }] }));
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Assignee', exact: true }).click();
  await page.getByRole('option', { name: 'Liisa Korhonen' }).click();
  await expect.poll(() => state.queries.at(-1)?.get('assignee')).toBe('emp-1');
  await expect(page.getByText('Partner requests have no assignee, so they are hidden while this filter is on.')).toBeVisible();

  await page.locator('[aria-label="Clear assignee"]').click();
  await expect.poll(() => state.queries.at(-1)?.has('assignee')).toBe(false);
  // Clearing writes an explicit 'all', so a reload stays on Anyone instead of the unassigned default.
  expect(new URL(page.url()).searchParams.get('assignee')).toBe('all');
  await expect(page.getByText('Partner requests have no assignee, so they are hidden while this filter is on.')).toHaveCount(0);
});

test('a date that is not yyyy-MM-dd is ignored while a valid end date is still sent', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/?from=garbage&to=2026-11-30');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  expect(state.queries.at(-1)?.has('from')).toBe(false);
  expect(state.queries.at(-1)?.get('to')).toBe('2026-11-30');
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('To date', { exact: true })).toHaveValue('2026-11-30');
});

test('clearing both date fields removes the bounds from the request and the URL', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/?from=2026-11-01&to=2026-11-30');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await page.getByLabel('From date', { exact: true }).fill('');
  await page.getByLabel('To date', { exact: true }).fill('');
  await expect.poll(() => state.queries.at(-1)?.has('from')).toBe(false);
  expect(state.queries.at(-1)?.has('to')).toBe(false);
  expect(new URL(page.url()).searchParams.has('from')).toBe(false);
  expect(new URL(page.url()).searchParams.has('to')).toBe(false);
});

test('an impossible calendar date in the URL is ignored while a valid end date is still sent', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/?from=2026-13-45&to=2026-11-30');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  expect(state.queries.at(-1)?.has('from')).toBe(false);
  expect(state.queries.at(-1)?.get('to')).toBe('2026-11-30');
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('');
});


test('typing a From date after the To date clears both bounds, and From is capped by To', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/?to=2026-11-30');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await expect(page.getByLabel('From date', { exact: true })).toHaveAttribute('max', '2026-11-30');
  await page.getByLabel('From date', { exact: true }).fill('2026-12-05');
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('To date', { exact: true })).toHaveValue('');
  await expect.poll(() => state.queries.at(-1)?.has('to')).toBe(false);
  expect(state.queries.at(-1)?.has('from')).toBe(false);
  expect(new URL(page.url()).searchParams.has('to')).toBe(false);
});

test('date and assignee filters survive a view, source or page change', async ({ page }) => {
  const state = await setup(page);
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'emp-1', firstName: 'Liisa', lastName: 'Korhonen', email: 'liisa@example.test', isActive: true }] }));
  await page.goto('/admin/work-queue/?from=2026-11-01&to=2026-11-30&assignee=emp-1');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await page.getByRole('tab', { name: /^Scheduled/ }).click();
  await expect.poll(() => state.queries.at(-1)?.get('from')).toBe('2026-11-01');
  expect(state.queries.at(-1)?.get('to')).toBe('2026-11-30');
  expect(state.queries.at(-1)?.get('assignee')).toBe('emp-1');
  expect(new URL(page.url()).searchParams.get('from')).toBe('2026-11-01');
  expect(new URL(page.url()).searchParams.get('assignee')).toBe('emp-1');
  await page.getByRole('textbox', { name: 'Source', exact: true }).click();
  await page.getByRole('option', { name: 'Public bookings', exact: true }).click();
  await expect.poll(() => state.queries.at(-1)?.get('source')).toBe('booking');
  expect(state.queries.at(-1)?.get('from')).toBe('2026-11-01');
  expect(state.queries.at(-1)?.get('assignee')).toBe('emp-1');
  expect(new URL(page.url()).searchParams.get('to')).toBe('2026-11-30');
});

test('date and assignee filters carry through the Next page control', async ({ page }) => {
  const state = await setup(page);
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'emp-1', firstName: 'Liisa', lastName: 'Korhonen', email: 'liisa@example.test', isActive: true }] }));
  state.items['needs-action'] = Array.from({ length: 26 }, (_, i) => ({ ...bookingItem, id: `booking-${i + 1}`, reference: `BK-${2000 + i}`, customerName: `Customer ${i + 1}`, href: `/admin/work-queue/?booking=booking-${i + 1}` }));
  state.counts['needs-action'] = 26;
  await page.goto('/admin/work-queue/?from=2026-11-01&to=2026-11-30&assignee=emp-1');
  await expect(page.getByText(/Showing 1–25 of 26/)).toBeVisible();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Customer 26' })).toBeVisible();
  await expect.poll(() => state.queries.at(-1)?.get('skip')).toBe('25');
  expect(state.queries.at(-1)?.get('from')).toBe('2026-11-01');
  expect(state.queries.at(-1)?.get('to')).toBe('2026-11-30');
  expect(state.queries.at(-1)?.get('assignee')).toBe('emp-1');
  expect(new URL(page.url()).searchParams.get('from')).toBe('2026-11-01');
  expect(new URL(page.url()).searchParams.get('to')).toBe('2026-11-30');
  expect(new URL(page.url()).searchParams.get('assignee')).toBe('emp-1');
});

test('typing a To date before From clears both bounds, and To is capped from below by From', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/admin/work-queue/?from=2026-11-10');
  await expect(page.getByRole('listitem').filter({ hasText: 'Anna Virtanen' })).toBeVisible();
  await expect(page.getByLabel('To date', { exact: true })).toHaveAttribute('min', '2026-11-10');
  await page.getByLabel('To date', { exact: true }).fill('2026-11-01');
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('To date', { exact: true })).toHaveValue('');
  await expect.poll(() => state.queries.at(-1)?.has('from')).toBe(false);
  expect(state.queries.at(-1)?.has('to')).toBe(false);
  expect(new URL(page.url()).searchParams.has('from')).toBe(false);
  expect(new URL(page.url()).searchParams.has('to')).toBe(false);
});
