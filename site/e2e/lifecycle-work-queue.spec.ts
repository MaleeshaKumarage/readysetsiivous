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

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  const state = {
    bookings: { 'booking-1': booking() },
    queries: [] as URLSearchParams[],
    actions: [] as string[],
    listFails: false,
    counts: { 'needs-action': 1, scheduled: 0, 'in-progress': 0, completed: 0 },
  };
  await page.route('**/api/v1/admin/work-queue**', route => {
    const url = new URL(route.request().url());
    state.queries.push(url.searchParams);
    if (state.listFails) return route.fulfill({ headers, status: 503, json: { detail: 'Queue unavailable' } });
    const view = url.searchParams.get('view');
    const items = view === 'needs-action'
      ? [{ source: 'booking', id: 'booking-1', reference: 'BK-1001', customerName: 'Anna Virtanen', scheduledLocal: '2026-11-10 09:00', status: 'New', stateLabel: 'New booking', assigneeName: null, href: '/admin/work-queue/?booking=booking-1' }]
      : [];
    return route.fulfill({ headers, json: { view, source: url.searchParams.get('source') ?? 'all', items, total: items.length, counts: state.counts } });
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
      state.bookings[id as 'booking-1'] = booking({ status: next });
      return route.fulfill({ headers, status: 204 });
    }
    return route.fulfill({ headers, json: state.bookings[id as 'booking-1'] });
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
