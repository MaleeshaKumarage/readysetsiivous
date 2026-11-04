import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
async function setup(page: Page, allowed = true) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: allowed, employee: null } }));
  const state = { saved: null as any, fail: false, saveFailure: 0, specialStates: false, partialWork: false, hold: null as Promise<void> | null, settled: false, unusual: false, abortPut: false, abortGet: false, secondFlagged: false };
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?|\/|$)/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (!allowed) return route.fulfill({ headers, status: 403, json: {} });
    if (route.request().method() === 'PUT') {
      if (state.saveFailure) return route.fulfill({ headers, status: state.saveFailure, json: { detail: state.saveFailure === 409 ? 'Work times changed. Reload the calendar before saving.' : 'Could not save times. Please retry.' } });
      state.saved = route.request().postDataJSON(); state.partialWork = false; state.unusual = false;
      // A lost response: the server has saved the work, but the browser never receives the reply.
      if (state.abortPut) { state.abortPut = false; return route.abort('connectionclosed'); }
      return route.fulfill({ headers, json: { id: 'work' } });
    }
    // A connection that drops before any reply: the load failed, nothing changed.
    if (state.abortGet) return route.abort('connectionclosed');
    if (state.fail) return route.fulfill({ headers, status: 503, json: { detail: 'Calendar unavailable' } });
    const date = new URL(route.request().url()).searchParams.get('date') || '2026-10-30';
    if (state.hold && date === '2026-10-29') await state.hold;
    const past = date < '2026-11-01';
    const specialVisit = (id: string, cancelled: boolean, ambiguous: boolean, assigned: boolean) => ({
      shiftId: id, shiftName: id, companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T11:00:00Z`, endUtc: `${date}T13:00:00Z`, exceptionId: id, cancelled, ambiguous, needsReview: false,
      assignees: assigned ? [{ employeeId: 'special', name: 'Assigned Cleaner', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: false, review: null }] : [],
    });
    // Server-computed review flags: past visits without completion are missing; a recorded visit is unusual until a reasoned correction.
    const aliceReview = !past ? null : state.saved && !state.partialWork ? (state.unusual ? { kind: 'unusual-duration', label: 'Actual duration differs from scheduled' } : null)
      : state.partialWork ? { kind: 'missing-completion', label: 'Started, not ended' } : { kind: 'missing-completion', label: 'No completion recorded' };
    const bobReview = past ? { kind: 'missing-completion', label: 'No completion recorded' } : null;
    const visits: any[] = date > '2026-11-01' ? [] : [{
      shiftId: 'shift', shiftName: `Cleaning ${date}`, companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T08:00:00Z`, endUtc: `${date}T10:00:00Z`, exceptionId: null, cancelled: false, ambiguous: false, needsReview: !!aliceReview || !!bobReview,
      assignees: [{ employeeId: 'alice', name: 'Alice Cleaner', status: state.partialWork ? 'Started' : state.saved ? 'Recorded' : 'Not started', actualStartUtc: state.saved ? `${date}T08:15:00Z` : null,
        actualEndUtc: state.saved && !state.partialWork ? `${date}T10:15:00Z` : null, hours: state.saved && !state.partialWork ? 2 : null, version: state.saved ? '11111111-1111-1111-1111-111111111111' : null, canRecord: past, review: aliceReview },
        { employeeId: 'bob', name: 'Bob Cleaner', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: past, review: bobReview }],
    }];
    if (state.settled && past) visits.push({ shiftId: 'settled', shiftName: 'Settled clean', companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T12:00:00Z`, endUtc: `${date}T14:00:00Z`, exceptionId: 'settled', cancelled: false, ambiguous: false, needsReview: false,
      assignees: [{ employeeId: 'carol', name: 'Carol Done', status: 'Recorded', actualStartUtc: `${date}T12:00:00Z`, actualEndUtc: `${date}T14:00:00Z`, hours: 2, version: '22222222-2222-2222-2222-222222222222', canRecord: true, review: null }] });
    if (state.secondFlagged && past) visits.push({ shiftId: 'second', shiftName: 'Second clean', companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T13:00:00Z`, endUtc: `${date}T15:00:00Z`, exceptionId: 'second', cancelled: false, ambiguous: false, needsReview: true,
      assignees: [{ employeeId: 'dan', name: 'Dan Pending', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: true, review: { kind: 'missing-completion', label: 'No completion recorded' } }] });
    if (state.specialStates) visits.push(specialVisit('Cancelled clean', true, false, true), specialVisit('Overlapping clean', false, true, true), specialVisit('Unassigned clean', false, false, false));
    return route.fulfill({ headers, json: { date, today: '2026-11-01', timeZoneId: 'Europe/Helsinki', occurrences: visits } });
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
  await expect(page.getByText('Work duration: 2.00 hours', { exact: true })).toBeVisible();
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
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await expect(page.getByLabel('Actual end')).toHaveValue('2026-10-30T12:15');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Corrected timesheet');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.saved.reason).toBe('Corrected timesheet');
  expect(state.saved.version).toBe('11111111-1111-1111-1111-111111111111');
});

for (const status of [409, 500]) {
  test(`failed work save (${status}) retains the dialog and entered values`, async ({ page }) => {
    const state = await setup(page); state.saved = {}; state.saveFailure = status;
    await page.goto('/admin/shifts/calendar/');
    await page.getByRole('button', { name: 'Correct times for Alice Cleaner' }).click();
    await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Correction to retain');
    await page.getByRole('button', { name: 'Save work times' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('alert')).toContainText(status === 409 ? 'Work times changed' : 'Could not save times');
    await expect(page.getByRole('textbox', { name: 'Reason', exact: true })).toHaveValue('Correction to retain');
    await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
    await page.getByRole('button', { name: 'Close work times' }).click();
    await expect(dialog).toHaveCount(0);
  });
}

test('cancelled, overlapping, and unassigned visits show their distinct states', async ({ page }) => {
  const state = await setup(page); state.specialStates = true;
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('Cancelled', { exact: true })).toBeVisible();
  await expect(page.getByText('Overlapping visits require review before work times can be recorded.', { exact: true })).toBeVisible();
  await expect(page.getByText('Unassigned', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Record work for Assigned Cleaner' })).toHaveCount(0);
});

test('revoked permission during save closes work entry and hides calendar data', async ({ page }) => {
  const state = await setup(page); state.saveFailure = 403;
  await page.goto('/admin/shifts/calendar/');
  await page.getByRole('button', { name: 'Record work for Alice Cleaner' }).click();
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Pending entry');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('alert').filter({ hasText: 'not enabled' })).toBeVisible();
  await expect(page.getByLabel('Calendar date')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toHaveCount(0);
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
  let ownAccess = false;
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: ownAccess, employee: null } }));
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  const people = [{ id: 'admin-one', firstName: 'Admin', lastName: 'One', email: 'admin@example.invalid', role: 'admin', isActive: true, shiftCalendarEnabled: false },
    { id: 'cleaner', firstName: 'Cleaner', lastName: 'One', email: 'cleaner@example.invalid', role: 'employee', isActive: true }];
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: people }));
  const updates: boolean[] = [];
  await page.route(/\/api\/v1\/admin\/employees\/admin-one\/shift-calendar-access$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    const { enabled } = route.request().postDataJSON(); updates.push(enabled);
    ownAccess = enabled;
    return route.fulfill({ headers, json: { enabled } });
  });
  await page.goto('/admin/employees/');
  const calendarLink = page.locator('a[href="/admin/shifts/calendar/"]');
  await expect(calendarLink).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toHaveCount(1);
  await page.getByRole('button', { name: 'Enable shift calendar' }).click();
  await expect(calendarLink).toHaveCount(1);
  await page.getByRole('button', { name: 'Disable shift calendar' }).click();
  await expect(calendarLink).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toBeVisible();
  expect(updates).toEqual([true, false]);
});

test('enabled calendar navigation opens the daily calendar', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await setup(page);
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  await page.goto('/admin/employees/');
  await page.getByRole('link', { name: 'Shift calendar', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/shifts\/calendar\/$/);
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
});

test('returning to Employees reloads grants changed by another admin', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await setup(page);
  const person = { id: 'admin-one', firstName: 'Admin', lastName: 'One', email: 'admin@example.invalid', role: 'admin', isActive: true, shiftCalendarEnabled: false };
  let reads = 0;
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => { reads++; return route.fulfill({ headers, json: [person] }); });
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  await page.goto('/admin/employees/');
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toBeVisible();
  const initialReads = reads;
  await page.getByRole('link', { name: 'Shift calendar', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
  person.shiftCalendarEnabled = true;
  await page.getByRole('link', { name: 'Employees', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Disable shift calendar' })).toBeVisible();
  expect(reads).toBeGreaterThan(initialReads);
});

test('a started visit prefills its actual start and scheduled end before admin completion', async ({ page }) => {
  const state = await setup(page); state.saved = {}; state.partialWork = true;
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('Started', { exact: true })).toBeVisible();
  await expect(page.getByText(/Actual:.*Not ended/)).toBeVisible();
  await page.getByRole('button', { name: 'Correct times for Alice Cleaner' }).click();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await expect(page.getByLabel('Actual end')).toHaveValue('2026-10-30T12:00');
  await expect(page.getByText(/Saving records this past visit as completed/)).toBeVisible();
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Complete past work from timesheet');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText('Recorded · 2.00 hours', { exact: true })).toBeVisible();
});

test('failed permission changes retain the toggle and show a retryable error', async ({ page }) => {
  await setup(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: [{ id: 'admin-one', firstName: 'Admin', lastName: 'One', email: 'admin@example.invalid', role: 'admin', isActive: true, shiftCalendarEnabled: false }] }));
  let failing = true;
  await page.route(/\/api\/v1\/admin\/employees\/admin-one\/shift-calendar-access$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, status: failing ? 500 : 200, json: failing ? {} : { enabled: true } });
  });
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Enable shift calendar' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not change calendar access' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Disable shift calendar' })).toHaveCount(0);
  failing = false;
  await page.getByRole('button', { name: 'Enable shift calendar' }).click();
  await expect(page.getByRole('button', { name: 'Disable shift calendar' })).toBeVisible();
});

test('started-but-not-ended visit shows Missing completion with the Started, not ended label', async ({ page }) => {
  const state = await setup(page); state.saved = {}; state.partialWork = true;
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('Missing completion', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Started, not ended', { exact: true })).toBeVisible();
  await expect(page.getByText('No completion recorded', { exact: true })).toHaveCount(1);
});

test('visit with no recorded work shows Missing completion with the No completion recorded label', async ({ page }) => {
  await setup(page);
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('Missing completion', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('No completion recorded', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Started, not ended', { exact: true })).toHaveCount(0);
});

test('visits needing review are counted and filterable; scheduled and actual times are labelled apart', async ({ page }) => {
  const state = await setup(page); state.settled = true;
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('1 visit needs review.', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Settled clean' })).toBeVisible();
  await expect(page.getByText(/^Scheduled: /).first()).toBeVisible();
  await expect(page.getByText('Missing completion', { exact: true }).first()).toBeVisible();
  await page.getByText('Show only visits that need review', { exact: true }).click();
  await expect(page.getByRole('checkbox', { name: 'Show only visits that need review' })).toBeChecked();
  await expect(page.getByRole('heading', { name: 'Settled clean' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toBeVisible();
  await page.getByText('Show only visits that need review', { exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Settled clean' })).toBeVisible();
});

test('unusual duration is flagged, explained in the correction, and clears after a reasoned correction without moving times', async ({ page }) => {
  const state = await setup(page); state.saved = {}; state.unusual = true;
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('Unusual time', { exact: true })).toBeVisible();
  await expect(page.getByText('Actual duration differs from scheduled', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Correct times for Alice Cleaner' }).click();
  await expect(page.getByText(/Review: Actual duration differs from scheduled/)).toBeVisible();
  await expect(page.getByRole('dialog').getByText(/^Scheduled: /)).toBeVisible();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Confirmed long visit');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText('Unusual time', { exact: true })).toHaveCount(0);
  expect(state.saved).toMatchObject({ actualStartLocal: '2026-10-30T10:15', reason: 'Confirmed long visit' });
});

test('a dropped load connection shows a plain reload message and Retry recovers', async ({ page }) => {
  const state = await setup(page); state.abortGet = true;
  await page.goto('/admin/shifts/calendar/');
  const alert = page.getByRole('alert').filter({ hasText: 'The calendar could not be loaded' });
  await expect(alert).toContainText('Reload the page to try again.');
  await expect(page.getByText('The connection was lost before the server confirmed the request')).toHaveCount(0);
  state.abortGet = false;
  await alert.getByRole('button', { name: 'Retry' }).click();
  await expect(page.getByRole('button', { name: 'Record work for Alice Cleaner' })).toBeVisible();
});

test('a lost save response explains that the save may have happened; reloading shows the recorded work', async ({ page }) => {
  const state = await setup(page); state.abortPut = true;
  await page.goto('/admin/shifts/calendar/');
  await page.getByRole('button', { name: 'Record work for Alice Cleaner' }).click();
  await page.getByRole('textbox', { name: 'Reason', exact: true }).fill('Timesheet received');
  await page.getByRole('button', { name: 'Save work times' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('alert')).toContainText('The connection was lost before the server confirmed the request');
  await expect(dialog.getByRole('alert')).toContainText('Reload the calendar');
  await expect(page.getByRole('textbox', { name: 'Reason', exact: true })).toHaveValue('Timesheet received');
  await page.getByRole('button', { name: 'Close work times' }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Correct times for Alice Cleaner' })).toBeVisible();
  expect(state.saved).toMatchObject({ reason: 'Timesheet received' });
});

test('review summary counts two flagged visits and filter-empty copy only appears when no visit needs review', async ({ page }) => {
  const state = await setup(page); state.secondFlagged = true;
  await page.goto('/admin/shifts/calendar/');
  await expect(page.getByText('2 visits need review.', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Second clean' })).toBeVisible();
  await expect(page.getByText('Turn off the review filter to see every visit.', { exact: true })).toHaveCount(0);
  // Today has no review flags: the summary says so, and the filter then needs an explicit empty message.
  await page.getByLabel('Calendar date').fill('2026-11-01');
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-10-30' })).toHaveCount(0);
  await expect(page.getByText('No visits need review on this day.', { exact: true })).toBeVisible();
  await page.getByText('Show only visits that need review', { exact: true }).click();
  await expect(page.getByText('Turn off the review filter to see every visit.', { exact: true })).toBeVisible();
  await expect(page.getByText('No shifts on this day.', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Cleaning 2026-11-01' })).toHaveCount(0);
});
