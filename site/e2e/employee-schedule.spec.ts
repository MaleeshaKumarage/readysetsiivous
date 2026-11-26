import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const dayOf = (url: string) => new URL(url).searchParams.get('from')!.slice(0, 10);
const addDay = (day: string, count: number) => new Date(Date.parse(`${day}T12:00:00Z`) + count * 86_400_000).toISOString().slice(0, 10);
const localIso = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
const mondayOf = (date: Date) => { const copy = new Date(date); copy.setDate(copy.getDate() - ((copy.getDay() + 6) % 7)); return localIso(copy); };
// A fixed Monday, so the tests do not depend on today's date.
const MONDAY = '2030-01-14';

// Requests served by the mock, so the tests can check which week was asked for.
async function setup(page: Page, options: { scheduleStatus?: number; empty?: boolean; query?: string } = {}) {
  await setupAdminMocks(page);
  const requests: string[] = [];
  // Failing stays on until a test turns it off, so dev-mode double effects cannot consume the failure.
  let failing = (options.scheduleStatus ?? 0) > 0;
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/employee-schedule')) {
      requests.push(dayOf(request.url()));
      if (failing) return route.fulfill({ status: 500, headers, json: { detail: 'boom' } });
      const day = dayOf(request.url());
      if (options.empty) return route.fulfill({ headers, json: [] });
      return route.fulfill({ headers, json: [
        { employeeId: 'employee', localDay: `${day}T00:00:00`, visits: [
          { shiftId: 'shift', shiftName: 'Morning clean', startUtc: `${day}T06:00:00Z`, endUtc: `${day}T08:00:00Z` },
          { shiftId: 'other', shiftName: 'Evening deep clean', startUtc: `${day}T06:30:00Z`, endUtc: `${day}T08:30:00Z` },
        ] },
        { employeeId: 'second', localDay: `${addDay(day, 1)}T00:00:00`, visits: [
          { shiftId: 'shift', shiftName: 'Morning clean', startUtc: `${addDay(day, 1)}T06:00:00Z`, endUtc: `${addDay(day, 1)}T08:00:00Z` },
        ] },
      ] });
    }
    if (path.endsWith('/overlap-audit')) {
      const day = dayOf(request.url());
      return route.fulfill({ headers, json: [{ employeeId: 'employee',
        earlier: { shiftId: 'shift', shiftName: 'Morning clean', startUtc: `${day}T06:00:00Z`, endUtc: `${day}T08:00:00Z` },
        later: { shiftId: 'other', shiftName: 'Evening deep clean', startUtc: `${day}T06:30:00Z`, endUtc: `${day}T08:30:00Z` } }] });
    }
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [
      { id: 'employee', firstName: 'Test', lastName: 'Employee', role: 'employee', isActive: true, defaultHours: {} },
      { id: 'second', firstName: 'Second', lastName: 'Employee', role: 'employee', isActive: true, defaultHours: {} },
      { id: 'partner-owner', firstName: 'Active', lastName: 'Partner', role: 'partner', isActive: true, defaultHours: {} },
    ] });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  await page.goto(`/admin/shifts/employee-schedule/${options.query ?? `?date=${MONDAY}`}`);
  await expect.poll(() => requests.length).toBeGreaterThan(0);
  return { requests, stopFailing: () => { failing = false; } };
}

test('admin sees each employee on the chosen day with visits and overlaps flagged', async ({ page }) => {
  await setup(page);
  await expect(page.getByRole('heading', { name: 'Employee schedule' })).toBeVisible();
  const column = page.getByRole('group', { name: `Test Employee, ${MONDAY}` });
  await expect(column.getByRole('button', { name: /^Morning clean, 08:00–10:00, Scheduled, overlaps another visit$/ })).toBeVisible();
  await expect(column.getByRole('button', { name: /^Evening deep clean, 08:30–10:30, Scheduled, overlaps another visit$/ })).toBeVisible();
  await expect(column.getByText('Overlap', { exact: true })).toHaveCount(2);
  // The second employee has no visit on Monday; partners never get a column.
  await expect(page.getByRole('group', { name: `Second Employee, ${MONDAY}` }).getByRole('button')).toHaveCount(0);
  await expect(page.getByRole('group', { name: /Active Partner/ })).toHaveCount(0);
  await page.getByRole('button', { name: 'Next day' }).click();
  const tuesday = page.getByRole('group', { name: `Second Employee, ${addDay(MONDAY, 1)}` });
  await expect(tuesday.getByRole('button', { name: /^Morning clean, 08:00–10:00, Scheduled$/ })).toBeVisible();
  await expect(tuesday.getByText('Overlap', { exact: true })).toHaveCount(0);
});

test('a week with no visits says so and points to other weeks', async ({ page }) => {
  await setup(page, { empty: true });
  await expect(page.getByText('No visits this week. Choose another week or check the shift schedules.')).toBeVisible();
});

test('a failed load explains the problem and can be retried', async ({ page }) => {
  const state = await setup(page, { scheduleStatus: 1 });
  await expect(page.getByText('Could not load the schedule. Check your connection, then retry.')).toBeVisible();
  state.stopFailing();
  await page.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByRole('group', { name: `Test Employee, ${MONDAY}` }).getByRole('button', { name: /^Morning clean/ })).toBeVisible();
});

test('previous day from a Monday loads the week before', async ({ page }) => {
  const state = await setup(page);
  expect(state.requests[0]).toBe(MONDAY);
  await page.getByRole('button', { name: 'Previous day' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(MONDAY, -7));
  await expect(page.getByLabel('Date', { exact: true })).toHaveValue(addDay(MONDAY, -1));
});

test('previous and next week move the requested range by seven days', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await page.getByRole('button', { name: 'Next week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(MONDAY, 7));
  await page.getByRole('button', { name: 'Previous week' }).click();
  await page.getByRole('button', { name: 'Previous week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(MONDAY, -7));
});

test('this week returns to the current week after moving away', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  await page.getByRole('button', { name: 'Next week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(MONDAY, 7));
  await page.getByRole('button', { name: 'This week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(mondayOf(new Date()));
});

test('a date typed into Week starting jumps to the Monday of that week', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Week', exact: true }).click();
  // 2026-10-14 is a Wednesday, so the requested week starts on Monday 2026-10-12.
  await page.getByLabel('Week starting').fill('2026-10-14');
  await expect.poll(() => state.requests.at(-1)).toBe('2026-10-12');
  await expect(page.getByLabel('Week starting')).toHaveValue('2026-10-12');
});

test('a failed time-zone load blocks the schedule instead of showing times in the browser zone', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/tenant$/, route => route.fulfill({ status: 500, headers, json: { detail: 'boom' } }));
  await page.route(/\/api\/v1\/admin\/shifts\/employee-schedule/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ headers, json: [] }));
  await page.goto('/admin/shifts/employee-schedule/');
  await expect(page.getByText('Time zone unavailable')).toBeVisible();
  await expect(page.getByText('Could not load the workplace time zone')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Previous day' })).toHaveCount(0);
});

test('a failed overlap audit shows a retryable error instead of reporting no overlaps', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ status: 500, headers, json: { detail: 'boom' } }));
  await page.goto('/admin/shifts/employee-schedule/');
  await expect(page.getByText('Could not load the schedule. Check your connection, then retry.')).toBeVisible();
  await expect(page.getByText('Overlap', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
});

test('an empty staff list says there are no active employees yet', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/employees/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/employee-schedule/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/tenant$/, route => route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } }));
  await page.goto('/admin/shifts/employee-schedule/');
  await expect(page.getByText('No active employees yet. Add employees, then their visits appear here.')).toBeVisible();
});

test('the Employee schedule navigation item opens the page', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/employee-schedule/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/employees/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/tenant$/, route => route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } }));
  await page.goto('/admin/');
  // On phones the navigation sits in a drawer behind the toggle; open it when the toggle is visible.
  const toggle = page.getByRole('button', { name: 'Toggle navigation' });
  await toggle.click({ timeout: 3000 }).catch(() => undefined);
  await page.getByRole('link', { name: 'Employee schedule', exact: true }).first().click();
  await expect(page).toHaveURL(/\/admin\/shifts\/employee-schedule\/?$/);
  await expect(page.getByRole('heading', { name: 'Employee schedule' })).toBeVisible();
});

test('the Reload button in the time-zone error recovers the schedule once the zone loads', async ({ page }) => {
  await setupAdminMocks(page);
  let tenantCalls = 0; let zoneHealthy = false;
  await page.route(/\/api\/v1\/admin\/tenant$/, route => {
    tenantCalls++;
    if (!zoneHealthy) return route.fulfill({ status: 500, headers, json: { detail: 'boom' } });
    return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
  });
  await page.route(/\/api\/v1\/admin\/shifts\/employee-schedule/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/employees/, route => route.fulfill({ headers, json: [] }));
  await page.goto('/admin/shifts/employee-schedule/');
  await expect(page.getByText('Time zone unavailable')).toBeVisible();
  zoneHealthy = true;
  await page.getByRole('button', { name: 'Reload', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Previous day' })).toBeVisible();
  await expect(page.getByText('Time zone unavailable')).toHaveCount(0);
  expect(tenantCalls).toBeGreaterThanOrEqual(2);
});

test('conflict deep link opens the selected employee and week, with a way back to all employees', async ({ page }) => {
  const state = await setup(page, { query: '?employee=employee&date=2030-01-15' });
  await expect.poll(() => state.requests.at(-1)).toBe(MONDAY);
  // Week view: one column per day for the linked employee only.
  await expect(page.getByRole('group', { name: `Test Employee, ${MONDAY}` })).toBeVisible();
  // Monday to Friday by default; the weekend appears only when Show weekends is ticked.
  await expect(page.getByRole('group', { name: `Test Employee, ${addDay(MONDAY, 4)}` })).toBeVisible();
  await expect(page.getByRole('group', { name: `Test Employee, ${addDay(MONDAY, 6)}` })).toHaveCount(0);
  await expect(page.getByRole('group', { name: /^Second Employee/ })).toHaveCount(0);
  await page.getByRole('button', { name: 'Show all employees', exact: true }).click();
  await expect(page.getByRole('group', { name: `Second Employee, 2030-01-15` })).toBeVisible();
  await expect(page.getByRole('group', { name: `Test Employee, 2030-01-15` })).toBeVisible();
});
