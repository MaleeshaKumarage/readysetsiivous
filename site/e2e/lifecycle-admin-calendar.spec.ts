import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
async function setup(page: Page, allowed = true) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: allowed, employee: null } }));
  const state = { saved: null as any, fail: false, hold: null as Promise<void> | null };
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?|\/|$)/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (!allowed) return route.fulfill({ headers, status: 403, json: {} });
    if (route.request().method() === 'PUT') {
      state.saved = route.request().postDataJSON(); return route.fulfill({ headers, json: { id: 'work' } });
    }
    if (state.fail) return route.fulfill({ headers, status: 503, json: { detail: 'Calendar unavailable' } });
    const date = new URL(route.request().url()).searchParams.get('date') || '2026-10-30';
    if (state.hold && date === '2026-10-29') await state.hold;
    return route.fulfill({ headers, json: { date, today: '2026-11-01', timeZoneId: 'Europe/Helsinki', occurrences: date > '2026-11-01' ? [] : [{
      shiftId: 'shift', shiftName: `Cleaning ${date}`, companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T08:00:00Z`, endUtc: `${date}T10:00:00Z`, exceptionId: null, cancelled: false, ambiguous: false,
      assignees: [{ employeeId: 'alice', name: 'Alice Cleaner', status: state.saved ? 'Recorded' : 'Not started', actualStartUtc: state.saved ? `${date}T08:15:00Z` : null,
        actualEndUtc: state.saved ? `${date}T10:15:00Z` : null, hours: state.saved ? 2 : null, version: null, canRecord: date < '2026-11-01' },
        { employeeId: 'bob', name: 'Bob Cleaner', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: date < '2026-11-01' }],
    }] } });
  });
  return state;
}

test('daily cards show employees and save actual tenant-local work times with a reason', async ({ page }) => {
  const state = await setup(page); await page.goto('/admin/shifts/calendar/');
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
  await expect(page.getByText('Alice Cleaner', { exact: true })).toBeVisible();
  await expect(page.getByText('Bob Cleaner', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Record work for Alice Cleaner' }).click();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:00');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Enter both times and a reason');
  await page.getByLabel('Actual start').fill('2026-10-30T10:15');
  await page.getByLabel('Actual end').fill('2026-10-30T12:15');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Timesheet received');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('status')).toContainText('Work times saved');
  expect(state.saved).toMatchObject({ employeeId: 'alice', actualStartLocal: '2026-10-30T10:15', actualEndLocal: '2026-10-30T12:15', reason: 'Timesheet received', version: null });
  await expect(page.getByRole('button', { name: 'Correct times for Alice Cleaner' })).toBeVisible();
  await page.getByRole('button', { name: 'Correct times for Alice Cleaner' }).click();
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Corrected timesheet');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.saved.reason).toBe('Corrected timesheet');
});

test('today and future have no work actions; empty days and retry work', async ({ page }) => {
  const state = await setup(page); await page.goto('/admin/shifts/calendar/');
  await page.getByLabel('Calendar date').fill('2026-11-01');
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-11-01' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Record work for/ })).toHaveCount(0);
  await page.getByRole('button', { name: 'Next day' }).click();
  await expect(page.getByText('No shifts on this day.')).toBeVisible();
  await page.getByRole('button', { name: 'Previous day' }).click();
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-11-01' })).toBeVisible();
  await page.getByRole('button', { name: 'Today', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
  state.fail = true; await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Calendar unavailable' })).toBeVisible();
  state.fail = false; await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
});

test('disabled admins cannot open calendar through a direct URL', async ({ page }) => {
  await setup(page, false); await page.goto('/admin/shifts/calendar/');
  await expect(page.getByRole('alert').filter({ hasText: 'not enabled' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Shift calendar', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Record work/ })).toHaveCount(0);
});

test('late responses cannot replace a newly selected day', async ({ page }) => {
  const state = await setup(page); await page.goto('/admin/shifts/calendar/');
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
  let release!: () => void; state.hold = new Promise<void>(resolve => { release = resolve; });
  await page.getByLabel('Calendar date').fill('2026-10-29');
  await page.getByLabel('Calendar date').fill('2026-10-28');
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-28' })).toBeVisible();
  release(); await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-29' })).toHaveCount(0);
});

test('employee page grants and revokes calendar access for admins only', async ({ page }) => {
  await setup(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  const people = [{ id: 'admin-one', firstName: 'Admin', lastName: 'One', email: 'admin@example.invalid', role: 'admin', isActive: true, shiftCalendarEnabled: false },
    { id: 'cleaner', firstName: 'Cleaner', lastName: 'One', email: 'cleaner@example.invalid', role: 'employee', isActive: true }];
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: people }));
  const updates: boolean[] = [];
  await page.route(/\/api\/v1\/admin\/employees\/admin-one\/shift-calendar-access$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    const { enabled } = route.request().postDataJSON(); updates.push(enabled);
    return route.fulfill({ headers, json: { enabled } });
  });
  await page.goto('/admin/employees/');
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toHaveCount(1);
  await page.getByRole('button', { name: 'Enable shift calendar' }).click();
  await page.getByRole('button', { name: 'Disable shift calendar' }).click();
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toBeVisible();
  expect(updates).toEqual([true, false]);
});
