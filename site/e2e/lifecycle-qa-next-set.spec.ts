import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { openShiftWorkspace, shiftActionAt } from './shift-actions';
import type { MyShiftOccurrence } from '../src/lib/meApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('services list states that the price excludes VAT and shows the rate', async ({ page }) => {
  test.setTimeout(180000);
  await setupAdminMocks(page);
  const service = { id: '11111111-1111-4111-8111-111111111111', slug: 'home-cleaning', name: { values: { fi: 'Kotisiivous' } },
    description: { values: { fi: 'Description' } }, additionalInfo: null, currency: 'EUR', imageUrl: null,
    priceNet: 20, vatRatePercent: 25.5, durationMinutes: 75, isActive: true, isFeatured: false, sortOrder: 1 };
  await page.route(/\/api\/v1\/admin\/services/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 200, headers, json: [service] });
  });
  await page.goto('/admin/services/');
  await expect(page.getByRole('gridcell', { name: '20.00 € excl. VAT (25.5%)', exact: true })).toBeVisible({ timeout: 30000 });
});

test('shift list uses one time format for every schedule type and shows monthly times', async ({ page }) => {
  // The dev server compiles the admin shifts page and its client chunks on first use, which can exceed the default 30s under CI load.
  test.setTimeout(180000);
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = (path.endsWith('/shifts') || path.endsWith('/shifts/page')) ? [
      { id: 'daily', name: 'Daily shift', schedule: { type: 0, dailyStart: '08:00:00', dailyEnd: '17:00:00' }, isActive: true },
      { id: 'monthly', name: 'Monthly shift', schedule: { type: 5, monthlyDay: 15, monthlyStart: '13:00:00', monthlyEnd: '14:00:00' }, isActive: true },
    ] : path.endsWith('/assignment-counts') ? {} : path.endsWith('/companies') ? { items: [], total: 0 } : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json: path.endsWith('/shifts/page') ? shiftsResponse(json as any[], route.request().url()) : json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\/page)?(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 150000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByText('Daily 08:00–17:00', { exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Monthly day 15 13:00–14:00', { exact: true })).toBeVisible();
  await expect(page.getByRole('grid').getByText(/:\d{2}:\d{2}/)).toHaveCount(0);
});

test('assignee picker shows each employee email so similar names can be told apart', async ({ page }) => {
  test.setTimeout(180000);
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
    const json = (path.endsWith('/shifts') || path.endsWith('/shifts/page')) ? [{ id: 'one', name: 'Shift one', schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, isActive: true }]
      : path.endsWith('/employees') ? people : path.endsWith('/companies') ? { items: [], total: 0 } : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json: path.endsWith('/shifts/page') ? shiftsResponse(json as any[], route.request().url()) : json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\/page)?(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 150000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByRole('button', { name: /^More actions for / }).first()).toBeVisible({ timeout: 30000 });
  await shiftActionAt(page, 'Assign employees', 0);
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).click();
  await expect(page.getByRole('option', { name: 'Maleesha Kumarage (maleesha@hiava.fi)', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Maleesha MSK (maleesha.msk@gmail.com)', exact: true })).toBeVisible();
});

test('my shifts on an empty Today offers past and upcoming work instead of a dead end', async ({ page }) => {
  test.setTimeout(180000);
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
    // The range matches the view, as the real API does, so the client places each occurrence in its day.
    const range = view === 'past' ? { fromLocalDate: '2026-10-12', toLocalDate: '2026-10-18' }
      : view === 'future' ? { fromLocalDate: '2026-11-16', toLocalDate: '2026-11-22' } : { fromLocalDate: '2026-11-06', toLocalDate: '2026-11-06' };
    return route.fulfill({ status: 200, headers, json: { timeZoneId: 'Europe/Helsinki', ...range, occurrences } });
  });
  const calendarLoaded = page.waitForResponse(r => r.url().includes('/api/v1/me/shifts/calendar') && r.request().method() === 'GET', { timeout: 150000 });
  await page.goto('/me/shifts/');
  await calendarLoaded;
  await expect(page.getByText('No shifts today.', { exact: true })).toBeVisible({ timeout: 30000 });
  // Nothing is in progress on an empty Today, so both buttons are enabled.
  await expect(page.getByRole('button', { name: 'View past shifts', exact: true })).toBeEnabled();
  await expect(page.getByRole('button', { name: 'View upcoming shifts', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'View past shifts', exact: true }).click();
  await expect(page.getByRole('tab', { name: 'Past', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('button', { name: /Week of/ }).first().click();
  await expect(page.getByRole('button', { name: /16\/10\/2026 Friday.*1 shift/ })).toBeVisible({ timeout: 30000 });
  // Opening the week also opens its first day, so the occurrence is listed without clicking the day.
  await expect(page.getByRole('heading', { name: 'Past gym' })).toBeVisible({ timeout: 30000 });
  await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'View upcoming shifts', exact: true })).toBeVisible({ timeout: 30000 });
  await page.getByRole('button', { name: 'View upcoming shifts', exact: true }).click();
  await expect(page.getByRole('tab', { name: 'Future', exact: true })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('button', { name: /Week of/ }).first().click();
  await expect(page.getByRole('heading', { name: 'Upcoming clean' })).toBeVisible({ timeout: 30000 });
  expect(views).toContain('past');
  expect(views).toContain('future');
});

test('weekly shifts show HH:mm times with no seconds for both day branches', async ({ page }) => {
  test.setTimeout(180000);
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = (path.endsWith('/shifts') || path.endsWith('/shifts/page')) ? [
      { id: 'weekly-days', name: 'Weekly days', schedule: { type: 2, weeklyDays: [1, 3], weeklyStart: '08:00:00', weeklyEnd: '17:00:00' }, isActive: true },
      { id: 'weekly-one', name: 'Weekly one', schedule: { type: 2, weeklyDay: 5, weeklyStart: '09:00:00', weeklyEnd: '12:00:00' }, isActive: true },
    ] : path.endsWith('/assignment-counts') ? {} : path.endsWith('/companies') ? { items: [], total: 0 } : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json: path.endsWith('/shifts/page') ? shiftsResponse(json as any[], route.request().url()) : json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\/page)?(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 150000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByText('Monday, Wednesday 08:00–17:00', { exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Friday 09:00–12:00', { exact: true })).toBeVisible();
  await expect(page.getByRole('grid').getByText(/:\d{2}:\d{2}/)).toHaveCount(0);
});

test('a deactivated shift future visit is listed under Cancelled', async ({ page }) => {
  test.setTimeout(180000);
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

test('a service without a VAT rate shows the net price and no broken VAT suffix', async ({ page }) => {
  test.setTimeout(180000);
  await setupAdminMocks(page);
  const service = { id: '22222222-2222-4222-8222-222222222222', slug: 'no-vat-cleaning', name: { values: { fi: 'Ilman ALV:tä' } },
    description: { values: { fi: 'Description' } }, additionalInfo: null, currency: 'EUR', imageUrl: null,
    priceNet: 15, vatRatePercent: null, durationMinutes: 60, isActive: true, isFeatured: false, sortOrder: 2 };
  await page.route(/\/api\/v1\/admin\/services/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 200, headers, json: [service] });
  });
  await page.goto('/admin/services/');
  await expect(page.getByRole('gridcell', { name: '15.00 € excl. VAT', exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('undefined', { exact: false })).toHaveCount(0);
  await expect(page.getByText('(null%)', { exact: false })).toHaveCount(0);
});

test('one-time shifts show their date and HH:mm times with no seconds', async ({ page }) => {
  test.setTimeout(180000);
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = (path.endsWith('/shifts') || path.endsWith('/shifts/page')) ? [
      { id: 'one-time', name: 'One time', schedule: { type: 6, oneTimeDate: '2026-11-20', oneTimeStart: '09:30:00', oneTimeEnd: '11:00:00' }, isActive: true },
    ] : path.endsWith('/assignment-counts') ? {} : path.endsWith('/companies') ? { items: [], total: 0 } : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json: path.endsWith('/shifts/page') ? shiftsResponse(json as any[], route.request().url()) : json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\/page)?(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 150000 });
  await page.goto('/admin/shifts/');
  await listed;
  await expect(page.getByText('One-time 2026-11-20 09:30–11:00', { exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByRole('grid').getByText(/:\d{2}:\d{2}/)).toHaveCount(0);
});

test('workspace schedule shows HH:mm labels for daily, weekly, monthly and one-time shifts', async ({ page }) => {
  test.setTimeout(180000);
  await setupAdminMocks(page);
  const shifts = [
    { id: 'ws-daily', name: 'WS daily', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00:00', dailyEnd: '17:00:00' } },
    { id: 'ws-weekly', name: 'WS weekly', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 2, weeklyDays: [1, 3], weeklyStart: '08:00:00', weeklyEnd: '17:00:00' } },
    { id: 'ws-monthly', name: 'WS monthly', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 5, monthlyDay: 15, monthlyStart: '13:00:00', monthlyEnd: '14:00:00' } },
    { id: 'ws-one-time', name: 'WS one-time', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 6, oneTimeDate: '2026-11-20', oneTimeStart: '09:30:00', oneTimeEnd: '11:00:00' } },
  ];
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts/page')) return route.fulfill({ status: 200, headers, json: shiftsResponse(shifts, route.request().url()) });
    const json = path.endsWith('/shifts') ? shifts
      : path.endsWith('/shifts/assignment-counts') ? {}
      : path.endsWith('/pricing') ? { plans: [], overrides: [] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Company', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'Company', isActive: true }, branches: [{ id: 'branch', name: 'Branch', isActive: true }] }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  const listed = page.waitForResponse(r => /\/api\/v1\/admin\/shifts(\/page)?(\?|$)/.test(r.url()) && r.request().method() === 'GET', { timeout: 150000 });
  await page.goto('/admin/shifts/');
  await listed;
  for (const [name, label] of [
    ['WS daily', 'Daily 08:00–17:00'],
    ['WS weekly', 'Monday, Wednesday 08:00–17:00'],
    ['WS monthly', 'Monthly day 15 13:00–14:00'],
    ['WS one-time', 'One-time 2026-11-20 09:30–11:00'],
  ] as const) {
    await openShiftWorkspace(page, name);
    const dialog = page.getByRole('dialog', { name: `Manage — ${name}`, exact: true });
    await expect(dialog.getByText(label, { exact: true })).toBeVisible({ timeout: 30000 });
    await expect(dialog.getByText(/:\d{2}:\d{2}/)).toHaveCount(0);
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
  }
});
