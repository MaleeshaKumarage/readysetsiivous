import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const dayOf = (url: string) => new URL(url).searchParams.get('from')!.slice(0, 10);
const addDay = (day: string, count: number) => new Date(Date.parse(`${day}T12:00:00Z`) + count * 86_400_000).toISOString().slice(0, 10);

// Requests served by the mock, so the tests can check which week was asked for.
async function setup(page: Page, options: { scheduleStatus?: number; empty?: boolean } = {}) {
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
        { employeeId: 'cleaner', localDay: `${day}T00:00:00`, visits: [
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
      return route.fulfill({ headers, json: [{ employeeId: 'cleaner',
        earlier: { shiftId: 'shift', shiftName: 'Morning clean', startUtc: `${day}T06:00:00Z`, endUtc: `${day}T08:00:00Z` },
        later: { shiftId: 'other', shiftName: 'Evening deep clean', startUtc: `${day}T06:30:00Z`, endUtc: `${day}T08:30:00Z` } }] });
    }
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [
      { id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true },
      { id: 'second', firstName: 'Second', lastName: 'Cleaner', role: 'employee', isActive: true },
      { id: 'partner-owner', firstName: 'Active', lastName: 'Partner', role: 'partner', isActive: true },
    ] });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  await page.goto('/admin/shifts/employee-schedule/');
  await expect.poll(() => requests.length).toBeGreaterThan(0);
  return { requests, stopFailing: () => { failing = false; } };
}

test('admin sees each cleaner by day with visits and overlaps flagged', async ({ page }) => {
  const state = await setup(page);
  const first = state.requests[0];
  await expect(page.getByRole('heading', { name: 'Employee schedule' })).toBeVisible();
  const cell = page.getByLabel(`Test Cleaner, ${first}`);
  await expect(cell.getByText('Overlap')).toBeVisible();
  await expect(cell.getByText(/Morning clean 08:00/)).toBeVisible();
  await expect(cell.getByText(/Evening deep clean 08:30/)).toBeVisible();
  await expect(page.getByLabel(`Second Cleaner, ${addDay(first, 1)}`).getByText(/Morning clean 08:00/)).toBeVisible();
  await expect(page.getByText('Active Partner')).toHaveCount(0);
  await expect(page.getByLabel(`Second Cleaner, ${addDay(first, 1)}`).getByText('Overlap')).toHaveCount(0);
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
  await expect(page.getByLabel(`Test Cleaner, ${state.requests[0]}`).getByText(/Morning clean 08:00/)).toBeVisible();
});

test('previous and next week move the requested range by seven days', async ({ page }) => {
  const state = await setup(page);
  const start = state.requests[0];
  await page.getByRole('button', { name: 'Next week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(start, 7));
  await page.getByRole('button', { name: 'Previous week' }).click();
  await page.getByRole('button', { name: 'Previous week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(start, -7));
});

test('this week returns to the starting week after moving away', async ({ page }) => {
  const state = await setup(page);
  const start = state.requests[0];
  await page.getByRole('button', { name: 'Next week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(addDay(start, 7));
  await page.getByRole('button', { name: 'This week' }).click();
  await expect.poll(() => state.requests.at(-1)).toBe(start);
});

test('a date typed into Week starting jumps to the Monday of that week', async ({ page }) => {
  const state = await setup(page);
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
  await expect(page.getByRole('button', { name: 'Previous week' })).toHaveCount(0);
});

test('a failed overlap audit shows a retryable error instead of reporting no overlaps', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ status: 500, headers, json: { detail: 'boom' } }));
  await page.goto('/admin/shifts/employee-schedule/');
  await expect(page.getByText('Could not load the schedule. Check your connection, then retry.')).toBeVisible();
  await expect(page.getByText('Overlap', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
});

test('an empty staff list says there are no active cleaners yet', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/employees/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/employee-schedule/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts\/overlap-audit/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/tenant$/, route => route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } }));
  await page.goto('/admin/shifts/employee-schedule/');
  await expect(page.getByText('No active cleaners yet. Add employees, then their visits appear here.')).toBeVisible();
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
