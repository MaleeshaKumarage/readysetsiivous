import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { shiftActionAt } from './shift-actions';
import type { MyShiftOccurrence } from '../src/lib/meApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('services list states that the price excludes VAT and shows the rate', async ({ page }) => {
  await setupAdminMocks(page);
  const service = { id: '11111111-1111-4111-8111-111111111111', slug: 'home-cleaning', name: { values: { fi: 'Kotisiivous' } },
    description: { values: { fi: 'Description' } }, additionalInfo: null, currency: 'EUR', imageUrl: null,
    priceNet: 20, vatRatePercent: 25.5, durationMinutes: 75, isActive: true, isFeatured: false, sortOrder: 1 };
  await page.route(/\/api\/v1\/admin\/services/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 200, headers, json: [service] });
  });
  await page.goto('/admin/services/');
  await expect(page.getByRole('cell', { name: '20.00 € excl. VAT (25.5%)', exact: true })).toBeVisible();
});

test('shift list uses one time format for every schedule type and shows monthly times', async ({ page }) => {
  // The dev server compiles the admin shifts page and its client chunks on first use, which can exceed the default 30s under CI load.
  test.setTimeout(120000);
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = path.endsWith('/shifts') ? [
      { id: 'daily', name: 'Daily shift', schedule: { type: 0, dailyStart: '08:00:00', dailyEnd: '17:00:00' }, isActive: true },
      { id: 'monthly', name: 'Monthly shift', schedule: { type: 5, monthlyDay: 15, monthlyStart: '13:00:00', monthlyEnd: '14:00:00' }, isActive: true },
    ] : path.endsWith('/assignment-counts') ? {} : path.endsWith('/companies') ? { items: [], total: 0 } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 90000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByText('Daily 08:00–17:00', { exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Monthly day 15 13:00–14:00', { exact: true })).toBeVisible();
  await expect(page.getByRole('table').getByText(/:\d{2}:\d{2}/)).toHaveCount(0);
});

test('assignee picker shows each employee email so similar names can be told apart', async ({ page }) => {
  test.setTimeout(120000);
  await setupAdminMocks(page);
  const people = [
    { id: 'maleesha-1', firstName: 'Maleesha', lastName: 'Kumarage', email: 'maleesha@hiava.fi', isActive: true },
    { id: 'maleesha-2', firstName: 'Maleesha', lastName: 'MSK', email: 'maleesha.msk@gmail.com', isActive: true },
  ];
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/assignments') && route.request().method() === 'GET') return route.fulfill({ status: 200, headers, json: [] });
    if (path.endsWith('/assignment-counts')) return route.fulfill({ status: 200, headers, json: {} });
    const json = path.endsWith('/shifts') ? [{ id: 'one', name: 'Shift one', schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, isActive: true }]
      : path.endsWith('/employees') ? people : path.endsWith('/companies') ? { items: [], total: 0 } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 90000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByRole('button', { name: /^More actions for / }).first()).toBeVisible({ timeout: 30000 });
  await shiftActionAt(page, 'Employees', 0);
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).click();
  await expect(page.getByRole('option', { name: 'Maleesha Kumarage (maleesha@hiava.fi)', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Maleesha MSK (maleesha.msk@gmail.com)', exact: true })).toBeVisible();
});

test('my shifts on an empty Today offers past and upcoming work instead of a dead end', async ({ page }) => {
  await setupAdminMocks(page);
  const shift = (id: string, shiftName: string, startUtc: string): MyShiftOccurrence => ({ shiftId: id, shiftName, startUtc,
    endUtc: new Date(Date.parse(startUtc) + 7200000).toISOString(), timeZoneId: 'Europe/Helsinki', form: null });
  const views: string[] = [];
  await page.route(/\/api\/v1\/me\/shifts\/calendar\?/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const view = new URL(route.request().url()).searchParams.get('view')!;
    views.push(view);
    const occurrences = view === 'past' ? [shift('gym', 'Past gym', '2026-10-16T12:00:00Z')]
      : view === 'future' ? [shift('later', 'Upcoming clean', '2026-11-20T08:00:00Z')] : [];
    return route.fulfill({ status: 200, headers, json: { timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-06', toLocalDate: '2026-11-06', occurrences } });
  });
  await page.goto('/me/shifts/');
  await expect(page.getByText('No shifts today.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'View past shifts', exact: true }).click();
  await expect(page.getByRole('tab', { name: 'Past', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'View upcoming shifts', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'View upcoming shifts', exact: true }).click();
  await expect(page.getByRole('tab', { name: 'Future', exact: true })).toHaveAttribute('aria-selected', 'true');
  expect(views).toContain('past');
  expect(views).toContain('future');
});

test('weekly shifts show HH:mm times with no seconds for both day branches', async ({ page }) => {
  test.setTimeout(120000);
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = path.endsWith('/shifts') ? [
      { id: 'weekly-days', name: 'Weekly days', schedule: { type: 2, weeklyDays: [1, 3], weeklyStart: '08:00:00', weeklyEnd: '17:00:00' }, isActive: true },
      { id: 'weekly-one', name: 'Weekly one', schedule: { type: 2, weeklyDay: 5, weeklyStart: '09:00:00', weeklyEnd: '12:00:00' }, isActive: true },
    ] : path.endsWith('/assignment-counts') ? {} : path.endsWith('/companies') ? { items: [], total: 0 } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 90000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByText('Monday, Wednesday 08:00–17:00', { exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Friday 09:00–12:00', { exact: true })).toBeVisible();
  await expect(page.getByRole('table').getByText(/:\d{2}:\d{2}/)).toHaveCount(0);
});

test('a deactivated shift future visit is listed under Cancelled', async ({ page }) => {
  test.setTimeout(120000);
  await setupAdminMocks(page, { authenticated: true });
  const cancelledVisit = { source: 'shift', id: '11111111-1111-4111-8111-111111111111', reference: 'Evening deep clean', customerName: 'AAA',
    scheduledLocal: '2026-11-20 08:00', status: 'Cancelled', stateLabel: 'Cancelled', assigneeName: null, href: '/admin/shifts/' };
  await page.route('**/api/v1/admin/work-queue**', route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const view = new URL(route.request().url()).searchParams.get('view') ?? 'needs-action';
    const items = view === 'cancelled' ? [cancelledVisit] : [];
    return route.fulfill({ status: 200, headers, json: { view, source: 'all', items, total: items.length,
      counts: { 'needs-action': 0, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 1 } } });
  });
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ status: 200, headers, json: [] }));
  await page.goto('/admin/work-queue/');
  await expect(page.getByRole('tab', { name: /^Needs action/ })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Evening deep clean', { exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Cancelled/ }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Evening deep clean' })).toBeVisible();
  await expect(page.getByRole('listitem').filter({ hasText: 'Evening deep clean' })).toContainText('Cancelled');
});
