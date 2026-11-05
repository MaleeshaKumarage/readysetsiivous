import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS' };

// Today and the last day of the default 14-day window, in the tenant time zone the page uses.
function windowDays() {
  const today = new Date().toLocaleDateString('en-CA', { timeZone: 'Europe/Helsinki' });
  const end = new Date(Date.parse(`${today}T12:00:00Z`) + 13 * 86_400_000).toISOString().slice(0, 10);
  return { today, end };
}

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  const queries: URLSearchParams[] = [];
  await page.route('**/api/v1/admin/work-queue**', route => {
    const url = new URL(route.request().url());
    queries.push(url.searchParams);
    return route.fulfill({ headers, json: { view: 'needs-action', source: 'all', items: [], total: 0, counts: { 'needs-action': 0, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 } } });
  });
  return { queries, last: () => queries.at(-1) };
}

test('the first view opens on the next 14 days and says so', async ({ page }) => {
  const { last } = await setup(page);
  const { today, end } = windowDays();
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('status').filter({ hasText: 'Showing the next 14 days' })).toBeVisible();
  await expect.poll(() => last()?.get('from')).toBe(today);
  expect(last()?.get('to')).toBe(end);
});

test('Show all dates removes the bounds and a reload keeps them removed', async ({ page }) => {
  const { last } = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('status').filter({ hasText: 'Showing the next 14 days' })).toBeVisible();
  await page.getByRole('button', { name: 'Show all dates' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Showing the next 14 days' })).toHaveCount(0);
  await expect.poll(() => last()?.has('from')).toBe(false);
  expect(new URL(page.url()).searchParams.get('range')).toBe('all');
  await page.reload();
  await expect.poll(() => last()?.has('from')).toBe(false);
  await expect(page.getByRole('status').filter({ hasText: 'Showing the next 14 days' })).toHaveCount(0);
});

test('dates in the link are used as chosen and no default notice appears', async ({ page }) => {
  const { last } = await setup(page);
  await page.goto('/admin/work-queue/?view=needs-action&from=2026-11-01&to=2026-11-30');
  await expect.poll(() => last()?.get('from')).toBe('2026-11-01');
  expect(last()?.get('to')).toBe('2026-11-30');
  await expect(page.getByRole('status').filter({ hasText: 'Showing the next 14 days' })).toHaveCount(0);
});
