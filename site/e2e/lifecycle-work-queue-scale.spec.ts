import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS' };

// 500 needs-action rows, the size of a busy tenant. The server pages them; the screen must never ask for more than one page.
const TOTAL = 500;
const items = Array.from({ length: TOTAL }, (_, index) => ({
  source: 'shift', id: `visit-${index}`, reference: `Weekly ${index}`, customerName: `Customer ${String(index).padStart(3, '0')}`,
  scheduledLocal: '2026-11-13 16:00', status: 'Scheduled', stateLabel: 'Needs employee', assigneeName: null, href: '/admin/shifts/calendar/',
}));

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  const requests: URLSearchParams[] = [];
  await page.route('**/api/v1/admin/work-queue**', route => {
    const url = new URL(route.request().url());
    requests.push(url.searchParams);
    const skip = Number(url.searchParams.get('skip') ?? 0), take = Number(url.searchParams.get('take') ?? 25);
    const search = (url.searchParams.get('q') ?? '').toLowerCase();
    const source = url.searchParams.get('source');
    const matching = source === 'booking' ? [] : search ? items.filter(item => item.customerName.toLowerCase().includes(search)) : items;
    return route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: matching.slice(skip, skip + take), total: matching.length,
      counts: { 'needs-action': TOTAL, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 } } });
  });
  return requests;
}

// The queue merges visits, bookings and partner work into one list sorted by date, so it reads every unassigned visit,
// at most 100 a request and never more than 2,000, and shows 25 a page.
test('a 500-row queue reads unassigned visits 100 at a time and shows one page of 25', async ({ page }) => {
  const requests = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(page.getByText('Showing 1–25 of 500')).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Customer 000' })).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Customer 499' })).toHaveCount(0);
  await expect(page.getByText('Page 1 of 20', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText('Showing 26–50 of 500')).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Customer 025' })).toBeVisible();
  const shiftReads = requests.filter(params => params.get('source') === 'shift');
  expect(shiftReads.every(params => Number(params.get('take')) <= 100 && params.get('view') === 'needs-action' && params.get('unassigned') === 'true')).toBe(true);
  expect(shiftReads.map(params => params.get('skip'))).toEqual(['0', '100', '200', '300', '400']);
});

test('searching a 500-row queue narrows the list and resets to page 1', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/work-queue/');
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText('Showing 26–50 of 500')).toBeVisible();
  await page.getByRole('searchbox', { name: /search/i }).fill('Customer 49');
  await expect(page.getByText('Showing 1–10 of 10')).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Customer 490' })).toBeVisible();
  await expect(page.getByText('Page 1 of 1', { exact: true })).toBeVisible();
});
