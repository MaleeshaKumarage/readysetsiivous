import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS' };

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  const queries: URLSearchParams[] = [];
  await page.route('**/api/v1/admin/work-queue**', route => {
    const url = new URL(route.request().url());
    queries.push(url.searchParams);
    return route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: [], total: 0, counts: { 'needs-action': 0, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 } } });
  });
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'emp-1', firstName: 'Ada', lastName: 'Employee', isActive: true }] }));
  return { queries, last: () => queries.at(-1) };
}

// The queue no longer has a date window or an assignee filter: it always shows all unassigned work (design: "always should show all").
test('the queue always asks for all unassigned work, with no date window and no assignee', async ({ page }) => {
  const { queries } = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('status').filter({ hasText: 'Nothing is waiting.' })).toBeVisible();
  await expect.poll(() => queries.length).toBeGreaterThanOrEqual(2);
  for (const query of queries) {
    expect(query.get('view')).toBe('needs-action');
    expect(query.get('unassigned')).toBe('true');
    expect(query.has('from')).toBe(false);
    expect(query.has('to')).toBe(false);
    expect(query.has('assignee')).toBe(false);
  }
  expect(queries.map(query => query.get('source')).sort()).toEqual(['booking', 'shift']);
  await expect(page.getByLabel('From date')).toHaveCount(0);
  await expect(page.getByLabel('Assignee')).toHaveCount(0);
  await expect(page.getByText('Show all work')).toHaveCount(0);
});

test('old links with dates, an assignee or a view still open the whole queue', async ({ page }) => {
  const { queries } = await setup(page);
  await page.goto('/admin/work-queue/?view=scheduled&from=2026-01-01&to=2026-01-31&assignee=emp-1&range=all');
  await expect(page.getByRole('status').filter({ hasText: 'Nothing is waiting.' })).toBeVisible();
  await expect.poll(() => queries.length).toBeGreaterThanOrEqual(2);
  expect(queries.every(query => query.get('view') === 'needs-action' && !query.has('from') && !query.has('assignee'))).toBe(true);
  // The unused parameters are dropped from the address.
  await expect(page).toHaveURL(/\/admin\/work-queue\/$/);
});
