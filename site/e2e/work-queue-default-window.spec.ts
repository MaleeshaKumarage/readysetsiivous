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
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'emp-1', firstName: 'Ada', lastName: 'Cleaner', isActive: true }] }));
  return { queries, last: () => queries.at(-1) };
}

const windowNotice = (page: Page) => page.getByRole('status').filter({ hasText: 'for the next 14 days' });

test('the first view opens on unassigned work in the next 14 days', async ({ page }) => {
  const { last } = await setup(page);
  const { today, end } = windowDays();
  await page.goto('/admin/work-queue/');
  await expect(windowNotice(page).filter({ hasText: 'Showing unassigned work' })).toBeVisible();
  await expect.poll(() => last()?.get('from')).toBe(today);
  expect(last()?.get('to')).toBe(end);
  expect(last()?.get('unassigned')).toBe('true');
  expect(last()?.has('assignee')).toBe(false);
});

test('Show all work clears dates and the assignee, and a reload keeps them cleared', async ({ page }) => {
  const { last } = await setup(page);
  await page.goto('/admin/work-queue/');
  await expect(windowNotice(page)).toBeVisible();
  await page.getByRole('button', { name: 'Show all work' }).click();
  await expect(windowNotice(page)).toHaveCount(0);
  await expect.poll(() => last()?.has('from')).toBe(false);
  expect(last()?.has('unassigned')).toBe(false);
  expect(new URL(page.url()).searchParams.get('range')).toBe('all');
  expect(new URL(page.url()).searchParams.get('assignee')).toBe('all');
  await page.reload();
  await expect.poll(() => last()?.has('from')).toBe(false);
  expect(last()?.has('unassigned')).toBe(false);
  await expect(windowNotice(page)).toHaveCount(0);
});

test('dates in a link are used as chosen, with no default unassigned filter', async ({ page }) => {
  const { last } = await setup(page);
  await page.goto('/admin/work-queue/?view=needs-action&from=2026-11-01&to=2026-11-30');
  await expect.poll(() => last()?.get('from')).toBe('2026-11-01');
  expect(last()?.get('to')).toBe('2026-11-30');
  expect(last()?.has('unassigned')).toBe(false);
  await expect(windowNotice(page)).toHaveCount(0);
});

test('choosing Unassigned in the assignee list sends the unassigned filter, and a named cleaner sends the assignee', async ({ page }) => {
  const { last } = await setup(page);
  await page.goto('/admin/work-queue/?view=needs-action&range=all&assignee=all');
  await expect.poll(() => last()?.has('unassigned')).toBe(false);
  await page.getByRole('textbox', { name: 'Assignee', exact: true }).click();
  await page.getByRole('option', { name: 'Unassigned', exact: true }).click();
  await expect.poll(() => last()?.get('unassigned')).toBe('true');
  await page.getByRole('textbox', { name: 'Assignee', exact: true }).click();
  await page.getByRole('option', { name: 'Ada Cleaner', exact: true }).click();
  await expect.poll(() => last()?.get('assignee')).toBe('emp-1');
  expect(last()?.has('unassigned')).toBe(false);
});
