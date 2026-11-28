import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, setNavOpen } from './admin-helpers';

// Work times, review flags and photos from the shift calendar, recorded in the Scheduler's visit panel.
// The mock serves the calendar and builds the Scheduler's employee schedule from the same visits.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const DENIED = 'Your account does not have shift calendar access. Ask an owner to enable Shift calendar for your employee record, then reload.';
const EMPLOYEES: Record<string, string> = { alice: 'Alice Employee', bob: 'Bob Employee', carol: 'Carol Done', dan: 'Dan Pending', special: 'Assigned Employee', 'partner-cleaner': 'Partner Employee' };

async function setup(page: Page, allowed = true) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: allowed, employee: null } }));
  const state = { saved: null as any, fail: false, saveFailure: 0, specialStates: false, partialWork: false, hold: null as Promise<void> | null, settled: false, unusual: false,
    abortPut: false, abortGet: false, secondFlagged: false, override: null as null | ((date: string) => any[]), calendarCalls: 0 };

  const occurrencesFor = (date: string): any[] => {
    if (state.override) return state.override(date);
    const past = date < '2026-11-01';
    const specialVisit = (id: string, cancelled: boolean, ambiguous: boolean, assigned: boolean) => ({
      shiftId: id, shiftName: id, companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T11:00:00Z`, endUtc: `${date}T13:00:00Z`, exceptionId: id, cancelled, ambiguous, needsReview: false,
      assignees: assigned ? [{ employeeId: 'special', name: 'Assigned Employee', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: past, review: null }] : [],
    });
    // Server-computed review flags: past visits without completion are missing; a recorded visit is unusual until a reasoned correction.
    const aliceReview = !past ? null : state.saved && !state.partialWork ? (state.unusual ? { kind: 'unusual-duration', label: 'Actual duration differs from scheduled' } : null)
      : state.partialWork ? { kind: 'missing-completion', label: 'Started, not ended' } : { kind: 'missing-completion', label: 'No completion recorded' };
    const bobReview = past ? { kind: 'missing-completion', label: 'No completion recorded' } : null;
    const visits: any[] = date > '2026-11-01' ? [] : [{
      shiftId: 'shift', shiftName: `Cleaning ${date}`, companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T08:00:00Z`, endUtc: `${date}T10:00:00Z`, exceptionId: null, cancelled: false, ambiguous: false, needsReview: !!aliceReview || !!bobReview,
      assignees: [{ employeeId: 'alice', name: 'Alice Employee', status: state.partialWork ? 'Started' : state.saved ? 'Recorded' : 'Not started', actualStartUtc: state.saved ? `${date}T08:15:00Z` : null,
        actualEndUtc: state.saved && !state.partialWork ? `${date}T10:15:00Z` : null, hours: state.saved && !state.partialWork ? 2 : null, version: state.saved ? '11111111-1111-1111-1111-111111111111' : null, canRecord: past, review: aliceReview },
        { employeeId: 'bob', name: 'Bob Employee', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: past, review: bobReview }],
    }];
    if (state.settled && past) visits.push({ shiftId: 'settled', shiftName: 'Settled clean', companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T12:00:00Z`, endUtc: `${date}T14:00:00Z`, exceptionId: 'settled', cancelled: false, ambiguous: false, needsReview: false,
      assignees: [{ employeeId: 'carol', name: 'Carol Done', status: 'Recorded', actualStartUtc: `${date}T12:00:00Z`, actualEndUtc: `${date}T14:00:00Z`, hours: 2, version: '22222222-2222-2222-2222-222222222222', canRecord: true, review: null }] });
    if (state.secondFlagged && past) visits.push({ shiftId: 'second', shiftName: 'Second clean', companyName: 'Test company', branchName: 'Helsinki branch',
      startUtc: `${date}T13:00:00Z`, endUtc: `${date}T15:00:00Z`, exceptionId: 'second', cancelled: false, ambiguous: false, needsReview: true,
      assignees: [{ employeeId: 'dan', name: 'Dan Pending', status: 'Not started', actualStartUtc: null, actualEndUtc: null, hours: null, version: null, canRecord: true, review: { kind: 'missing-completion', label: 'No completion recorded' } }] });
    if (state.specialStates) visits.push(specialVisit('Cancelled clean', true, false, true), specialVisit('Overlapping clean', false, true, true), specialVisit('Unassigned clean', false, false, false));
    return visits;
  };
  const dayOf = (offset: number, monday: string) => new Date(Date.parse(`${monday}T12:00:00Z`) + offset * 86_400_000).toISOString().slice(0, 10);

  await page.route(/\/api\/v1\/admin\/(?!shifts\/calendar)/, route => {
    const request = route.request(); const url = new URL(request.url()); const path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts/employee-schedule')) {
      const monday = url.searchParams.get('from')!.slice(0, 10);
      const rows: any[] = [];
      for (let offset = 0; offset < 7; offset++) {
        const day = dayOf(offset, monday);
        for (const visit of occurrencesFor(day).filter(item => !item.cancelled)) for (const person of visit.assignees) {
          const workStatus = person.status === 'Recorded' ? 'Completed' : person.status === 'Started' ? 'InProgress' : day < '2026-11-01' ? 'NeedsAction' : 'Scheduled';
          let row = rows.find(item => item.employeeId === person.employeeId && item.localDay.startsWith(day));
          if (!row) rows.push(row = { employeeId: person.employeeId, localDay: `${day}T00:00:00`, visits: [] });
          row.visits.push({ shiftId: visit.shiftId, shiftName: visit.shiftName, startUtc: visit.startUtc, endUtc: visit.endUtc, companyName: visit.companyName,
            branchName: visit.branchName, workStatus, assigneeIds: [person.employeeId], rosterVersion: `roster-${visit.shiftId}`,
            moveLockReason: workStatus === 'Completed' || workStatus === 'InProgress' ? 'Started or completed work cannot be moved or reassigned.' : null });
        }
      }
      return route.fulfill({ headers, json: rows });
    }
    if (path.endsWith('/shifts/overlap-audit')) return route.fulfill({ headers, json: [] });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: Object.entries(EMPLOYEES).map(([id, name]) =>
      ({ id, firstName: name.split(' ')[0], lastName: name.split(' ')[1], role: 'employee', isActive: true, defaultHours: {} })) });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
    return route.fallback();
  });
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?|\/|$)/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (!allowed) return route.fulfill({ headers, status: 403, json: {} });
    if (route.request().method() === 'PUT') {
      if (state.saveFailure) return route.fulfill({ headers, status: state.saveFailure, json: { detail: state.saveFailure === 409 ? 'Work times changed. Reload the calendar before saving.' : state.saveFailure === 403 ? DENIED : 'Could not save times. Please retry.' } });
      state.saved = route.request().postDataJSON(); state.partialWork = false; state.unusual = false;
      // A lost response: the server has saved the work, but the browser never receives the reply.
      if (state.abortPut) { state.abortPut = false; return route.abort('connectionclosed'); }
      return route.fulfill({ headers, json: { id: 'work' } });
    }
    if (route.request().method() === 'POST') return route.fallback();
    state.calendarCalls++;
    // A connection that drops before any reply: the load failed, nothing changed.
    if (state.abortGet) return route.abort('connectionclosed');
    if (state.fail) return route.fulfill({ headers, status: 503, json: { detail: 'Calendar unavailable' } });
    const date = new URL(route.request().url()).searchParams.get('date') || '2026-10-30';
    if (state.hold && date === '2026-10-29') await state.hold;
    return route.fulfill({ headers, json: { date, today: '2026-11-01', timeZoneId: 'Europe/Helsinki', occurrences: occurrencesFor(date) } });
  });
  return state;
}

const open = async (page: Page, date = '2026-10-30') => {
  await page.goto(`/admin/shifts/scheduler/?date=${date}`);
  await expect(page.getByRole('heading', { name: 'Scheduler', exact: true })).toBeVisible();
};
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const visitOf = (page: Page, employee: string, visit: string | RegExp, date = '2026-10-30') =>
  page.getByRole('group', { name: `${EMPLOYEES[employee]}, ${date}` }).getByRole('button', { name: typeof visit === 'string' ? new RegExp(`^${escape(visit)},`) : visit });
const details = (page: Page) => page.getByRole('complementary', { name: 'Visit details' });
/** Opens a visit's panel. The panel covers part of the grid (all of it on phones), so an open panel is closed first. */
async function pick(page: Page, visit: ReturnType<typeof visitOf>) {
  if (await details(page).count()) await details(page).getByRole('button', { name: 'Close visit details' }).click();
  await visit.click();
}
const reason = (page: Page) => page.getByRole('dialog').getByRole('textbox', { name: 'Reason', exact: true });

test('a past visit saves actual tenant-local work times with a reason, then edits them', async ({ page }) => {
  const state = await setup(page); await open(page);
  await expect(visitOf(page, 'alice', 'Cleaning 2026-10-30')).toBeVisible();
  await expect(visitOf(page, 'bob', 'Cleaning 2026-10-30')).toBeVisible();
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await details(page).getByRole('button', { name: 'Record start and end' }).click();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:00');
  await expect(page.getByText('Work duration: 2.00 hours', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Enter both times and a reason');
  await page.getByLabel('Actual start').selectOption('2026-10-30T10:15');
  await page.getByLabel('Actual end').selectOption('2026-10-30T12:15');
  await reason(page).fill('Timesheet received');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('status')).toContainText('Work times saved');
  expect(state.saved).toMatchObject({ employeeId: 'alice', actualStartLocal: '2026-10-30T10:15', actualEndLocal: '2026-10-30T12:15', reason: 'Timesheet received', version: null });
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await details(page).getByRole('button', { name: 'Edit work times' }).click();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await expect(page.getByLabel('Actual end')).toHaveValue('2026-10-30T12:15');
  await reason(page).fill('Corrected timesheet');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(state.saved.reason).toBe('Corrected timesheet');
  expect(state.saved.version).toBe('11111111-1111-1111-1111-111111111111');
});

for (const status of [409, 500]) {
  test(`failed work save (${status}) retains the dialog and entered values`, async ({ page }) => {
    const state = await setup(page); state.saved = {}; state.saveFailure = status;
    await open(page);
    await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
    await details(page).getByRole('button', { name: 'Edit work times' }).click();
    await reason(page).fill('Correction to retain');
    await page.getByRole('button', { name: 'Save work times' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('alert')).toContainText(status === 409 ? 'Work times changed' : 'Could not save times');
    await expect(reason(page)).toHaveValue('Correction to retain');
    await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
    await page.getByRole('button', { name: 'Close work times' }).click();
    await expect(dialog).toHaveCount(0);
  });
}

test('cancelled and overlapping visits show their states; unassigned visits belong to the Work queue', async ({ page }) => {
  const state = await setup(page); state.specialStates = true;
  await open(page);
  await expect(visitOf(page, 'special', /^Cancelled clean, 13:00–15:00, Cancelled$/)).toBeVisible();
  await pick(page, visitOf(page, 'special', 'Overlapping clean'));
  await expect(details(page).getByText('Overlapping visits need review before work times can be recorded.')).toBeVisible();
  await expect(details(page).getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await expect(page.getByText('Unassigned clean')).toHaveCount(0);
});

test('revoked permission during save keeps the entry, shows the reason and turns work actions off', async ({ page }) => {
  const state = await setup(page); state.saveFailure = 403;
  await open(page);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await details(page).getByRole('button', { name: 'Record start and end' }).click();
  await reason(page).fill('Pending entry');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('does not have shift calendar access');
  await page.getByRole('button', { name: 'Close work times' }).click();
  await expect(details(page).getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await expect(details(page).getByText(DENIED)).toBeVisible();
});

test('today and future visits cannot record work yet; empty days and a failed calendar load recover', async ({ page }) => {
  const state = await setup(page); await open(page, '2026-11-01');
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-11-01', '2026-11-01'));
  await expect(details(page).getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await details(page).getByRole('button', { name: 'Close visit details' }).click();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText('No visits on this day.')).toBeVisible();
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(visitOf(page, 'alice', 'Cleaning 2026-11-01', '2026-11-01')).toBeVisible();
  state.fail = true; await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Calendar unavailable' })).toBeVisible();
  state.fail = false; await page.getByRole('alert').getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Calendar unavailable' })).toHaveCount(0);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-31', '2026-10-31'));
  await expect(details(page).getByRole('button', { name: 'Record start and end' })).toBeEnabled();
});

test('admins without calendar access reach the Scheduler from an old calendar link but cannot record work', async ({ page }) => {
  const state = await setup(page, false); await page.goto('/admin/shifts/calendar/?date=2026-10-30');
  await expect(page).toHaveURL(/\/admin\/shifts\/scheduler\/\?date=2026-10-30$/);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await expect(details(page).getByText('Shift calendar access is needed to record work times and add photos. Ask an owner to enable it, then reload.')).toBeVisible();
  expect(state.calendarCalls).toBe(0);
});

test('late calendar responses cannot replace a newly selected day', async ({ page }) => {
  const state = await setup(page); await open(page);
  await expect(visitOf(page, 'alice', 'Cleaning 2026-10-30')).toBeVisible();
  let release!: () => void; state.hold = new Promise<void>(resolve => { release = resolve; });
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByText(/^Day view, Wed 28 Oct\./)).toBeVisible();
  release();
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-28', '2026-10-28'));
  await expect(details(page).getByRole('definition').filter({ hasText: 'Wed 28 Oct, 10:00–12:00' })).toBeVisible();
  await expect(details(page).getByText('No completion recorded')).toBeVisible();
});

test('employee page grants and revokes calendar access for admins; the Scheduler stays in the menu', async ({ page }) => {
  await setup(page);
  let ownAccess = false;
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], canUseShiftCalendar: ownAccess, employee: null } }));
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  const people = [{ id: 'admin-one', firstName: 'Admin', lastName: 'One', email: 'admin@example.invalid', role: 'admin', isActive: true, shiftCalendarEnabled: false },
    { id: 'employee', firstName: 'Employee', lastName: 'One', email: 'employee@example.invalid', role: 'employee', isActive: true }];
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: people }));
  const updates: boolean[] = [];
  await page.route(/\/api\/v1\/admin\/employees\/admin-one\/shift-calendar-access$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    const { enabled } = route.request().postDataJSON(); updates.push(enabled);
    ownAccess = enabled;
    return route.fulfill({ headers, json: { enabled } });
  });
  await page.goto('/admin/employees/');
  const schedulerLink = page.locator('a[href="/admin/shifts/scheduler/"]');
  await setNavOpen(page, true); await expect(schedulerLink).toHaveCount(1); await setNavOpen(page, false);
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toHaveCount(1);
  await page.getByRole('button', { name: 'Enable shift calendar' }).click();
  await page.getByRole('button', { name: 'Disable shift calendar' }).click();
  await setNavOpen(page, true); await expect(schedulerLink).toHaveCount(1); await setNavOpen(page, false);
  await expect(page.getByRole('button', { name: 'Enable shift calendar' })).toBeVisible();
  expect(updates).toEqual([true, false]);
});

test('the Scheduler menu item opens the Scheduler', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await setup(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [], total: 0 } }));
  await page.goto('/admin/employees/');
  await page.getByRole('link', { name: 'Scheduler', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/shifts\/scheduler\/$/);
  await expect(page.getByRole('heading', { name: 'Scheduler', exact: true })).toBeVisible();
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
  await page.getByRole('link', { name: 'Scheduler', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Scheduler', exact: true })).toBeVisible();
  person.shiftCalendarEnabled = true;
  await page.getByRole('link', { name: 'Employees', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Disable shift calendar' })).toBeVisible();
  expect(reads).toBeGreaterThan(initialReads);
});

test('a started visit prefills its actual start and scheduled end before admin completion', async ({ page }) => {
  const state = await setup(page); state.saved = {}; state.partialWork = true;
  await open(page);
  await pick(page, visitOf(page, 'alice', /^Cleaning 2026-10-30, 10:00–12:00, In progress/));
  await expect(details(page).getByText('10:15 – Not ended')).toBeVisible();
  await details(page).getByRole('button', { name: 'Edit work times' }).click();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await expect(page.getByLabel('Actual end')).toHaveValue('2026-10-30T12:00');
  await expect(page.getByText(/Saving records this past visit as completed/)).toBeVisible();
  await reason(page).fill('Complete past work from timesheet');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await pick(page, visitOf(page, 'alice', /^Cleaning 2026-10-30, 10:00–12:00, Done/));
  await expect(details(page).getByText('10:15 – 12:15 · 2.00 hours')).toBeVisible();
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

test('a started-but-not-ended visit shows Missing completion with the Started, not ended label', async ({ page }) => {
  const state = await setup(page); state.saved = {}; state.partialWork = true;
  await open(page);
  await expect(visitOf(page, 'alice', 'Cleaning 2026-10-30').getByText('Needs review')).toBeVisible();
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByText('Missing completion', { exact: true })).toBeVisible();
  await expect(details(page).getByText('Started, not ended', { exact: true })).toBeVisible();
  await pick(page, visitOf(page, 'bob', 'Cleaning 2026-10-30'));
  await expect(details(page).getByText('No completion recorded', { exact: true })).toBeVisible();
});

test('a visit with no recorded work shows Missing completion with the No completion recorded label', async ({ page }) => {
  await setup(page);
  await open(page);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByText('Missing completion', { exact: true })).toBeVisible();
  await expect(details(page).getByText('No completion recorded', { exact: true })).toBeVisible();
  await expect(details(page).getByText('Started, not ended', { exact: true })).toHaveCount(0);
});

test('visits needing review carry a Needs review tag; a settled visit has none and shows its actual times', async ({ page }) => {
  const state = await setup(page); state.settled = true; state.secondFlagged = true;
  await open(page);
  for (const [employee, name] of [['alice', 'Cleaning 2026-10-30'], ['bob', 'Cleaning 2026-10-30'], ['dan', 'Second clean']]) {
    await expect(visitOf(page, employee, name).getByText('Needs review')).toBeVisible();
  }
  await expect(visitOf(page, 'carol', 'Settled clean').getByText('Needs review')).toHaveCount(0);
  await pick(page, visitOf(page, 'carol', 'Settled clean'));
  await expect(details(page).getByText('14:00 – 16:00 · 2.00 hours')).toBeVisible();
  await expect(details(page).getByRole('definition').filter({ hasText: 'Fri 30 Oct, 14:00–16:00' })).toBeVisible();
  // Today has no review flags.
  await details(page).getByRole('button', { name: 'Close visit details' }).click();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText(/^Day view, Sun 1 Nov\./)).toBeVisible();
  await expect(page.getByText('Needs review')).toHaveCount(0);
});

test('unusual duration is flagged and clears after a reasoned correction without moving times', async ({ page }) => {
  const state = await setup(page); state.saved = {}; state.unusual = true;
  await open(page);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByText('Unusual time', { exact: true })).toBeVisible();
  await expect(details(page).getByText('Actual duration differs from scheduled', { exact: true })).toBeVisible();
  await details(page).getByRole('button', { name: 'Edit work times' }).click();
  await expect(page.getByRole('dialog').getByText(/^Scheduled: /)).toBeVisible();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await reason(page).fill('Confirmed long visit');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(details(page).getByText('Unusual time', { exact: true })).toHaveCount(0);
  expect(state.saved).toMatchObject({ actualStartLocal: '2026-10-30T10:15', reason: 'Confirmed long visit' });
});

test('a dropped load connection shows a plain reload message and Retry recovers', async ({ page }) => {
  const state = await setup(page); state.abortGet = true;
  await open(page);
  const alert = page.getByRole('alert').filter({ hasText: 'The calendar could not be loaded' });
  await expect(alert).toContainText('Reload the page to try again.');
  await expect(page.getByText('The connection was lost before the server confirmed the request')).toHaveCount(0);
  state.abortGet = false;
  await alert.getByRole('button', { name: 'Retry' }).click();
  await expect(alert).toHaveCount(0);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByRole('button', { name: 'Record start and end' })).toBeEnabled();
});

test('a lost save response explains that the save may have happened; reloading shows the recorded work', async ({ page }) => {
  const state = await setup(page); state.abortPut = true;
  await open(page);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await details(page).getByRole('button', { name: 'Record start and end' }).click();
  await reason(page).fill('Timesheet received');
  await page.getByRole('button', { name: 'Save work times' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('alert')).toContainText('The connection was lost before the server confirmed the request');
  await expect(dialog.getByRole('alert')).toContainText('Reload the calendar');
  await expect(reason(page)).toHaveValue('Timesheet received');
  await page.getByRole('button', { name: 'Close work times' }).click();
  await expect(dialog).toHaveCount(0);
  await page.reload();
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByRole('button', { name: 'Edit work times' })).toBeVisible();
  expect(state.saved).toMatchObject({ reason: 'Timesheet received' });
});

test('a partner-accepted visit can be recorded from the keyboard and its saved time is shown', async ({ page }) => {
  const state = await setup(page);
  // Partner acceptance names the visit after the property; the recording contract is the same as any company visit.
  state.override = date => date !== '2026-10-30' ? [] : [{
    shiftId: 'partner-shift', shiftName: 'Cleaning — Lake apartment', companyName: 'Owner company', branchName: 'Lake apartment',
    startUtc: '2026-10-30T08:00:00Z', endUtc: '2026-10-30T10:00:00Z', exceptionId: 'partner-visit', cancelled: false, ambiguous: false, needsReview: !state.saved,
    assignees: [{ employeeId: 'partner-cleaner', name: 'Partner Employee', status: state.saved ? 'Recorded' : 'Not started', actualStartUtc: state.saved ? '2026-10-30T08:15:00Z' : null,
      actualEndUtc: state.saved ? '2026-10-30T10:15:00Z' : null, hours: state.saved ? 2 : null, version: state.saved ? '33333333-3333-3333-3333-333333333333' : null, canRecord: true,
      review: state.saved ? null : { kind: 'missing-completion', label: 'No completion recorded' } }],
  }];
  await open(page);
  const visit = visitOf(page, 'partner-cleaner', 'Cleaning — Lake apartment');
  // Keyboard: Enter opens the panel, the record button opens the dialog, Escape closes it and returns focus to the button.
  await visit.focus(); await page.keyboard.press('Enter');
  const record = details(page).getByRole('button', { name: 'Record start and end' });
  await expect(record).toBeEnabled();
  await record.focus(); await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog', { name: 'Record start and end', exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(record).toBeFocused();
  await page.keyboard.press('Enter');
  await page.getByLabel('Actual start').selectOption('2026-10-30T10:15');
  await page.getByLabel('Actual end').selectOption('2026-10-30T12:15');
  await reason(page).fill('Timesheet received');
  await page.getByRole('button', { name: 'Save work times' }).click();
  await expect(page.getByText('Work times saved.', { exact: true })).toBeVisible();
  expect(state.saved).toMatchObject({ employeeId: 'partner-cleaner', actualStartLocal: '2026-10-30T10:15', actualEndLocal: '2026-10-30T12:15' });
  await pick(page, visitOf(page, 'partner-cleaner', 'Cleaning — Lake apartment'));
  await details(page).getByRole('button', { name: 'Edit work times' }).click();
  await expect(page.getByLabel('Actual start')).toHaveValue('2026-10-30T10:15');
  await expect(page.getByLabel('Actual end')).toHaveValue('2026-10-30T12:15');
  // Escape inside the dialog closes the dialog only; the panel stays open until a second Escape.
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(details(page)).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(details(page)).toHaveCount(0);
});

test('the Scheduler shows the server-chosen completed duration with no review flag (selection rule: RecordedWorkTests and AdminShiftCalendarTests)', async ({ page }) => {
  const state = await setup(page);
  // The server picks the submitted 80-minute record; the Scheduler shows that duration and no review flag.
  state.override = date => date !== '2026-10-30' ? [] : [{
    shiftId: 'shift', shiftName: 'Cleaning 2026-10-30', companyName: 'Test company', branchName: 'Helsinki branch',
    startUtc: '2026-10-30T08:00:00Z', endUtc: '2026-10-30T10:00:00Z', exceptionId: null, cancelled: false, ambiguous: false, needsReview: false,
    assignees: [{ employeeId: 'alice', name: 'Alice Employee', status: 'Recorded', actualStartUtc: '2026-10-30T08:00:00Z', actualEndUtc: '2026-10-30T09:20:00Z',
      hours: 80 / 60, version: '11111111-1111-1111-1111-111111111111', canRecord: true, review: null }],
  }];
  await open(page);
  await pick(page, visitOf(page, 'alice', 'Cleaning 2026-10-30'));
  await expect(details(page).getByText('10:00 – 11:20 · 1.33 hours')).toBeVisible();
  await expect(details(page).getByText('Unusual time', { exact: true })).toHaveCount(0);
  await expect(details(page).getByText('Missing completion', { exact: true })).toHaveCount(0);
});

test('an admin attaches photos the employee sent outside the app to a recorded visit, with a note and a reason', async ({ page }) => {
  const state = await setup(page); state.settled = true; await open(page);
  const posted: any[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/calendar\/work-photos$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    posted.push(route.request().postDataJSON());
    return route.fulfill({ headers, json: { id: 'photos', version: '33333333-3333-3333-3333-333333333333', photoCount: 1 } });
  });
  await pick(page, visitOf(page, 'carol', 'Settled clean'));
  await details(page).getByRole('button', { name: 'Add photos' }).click();
  await page.getByLabel('Choose photos').setInputFiles({ name: 'kitchen.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgo=', 'base64') });
  await expect(page.getByAltText('Photo 1')).toBeVisible();
  await page.getByRole('button', { name: 'Save photos' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Add a separate note for each photo.');
  expect(posted).toHaveLength(0);
  await page.getByLabel('Note for photo 1').fill('Kitchen after cleaning');
  await reason(page).fill('Employee sent these by chat');
  await page.getByRole('button', { name: 'Save photos' }).click();
  await expect(page.getByRole('status')).toContainText('Photos added to the visit');
  expect(posted).toHaveLength(1);
  expect(posted[0]).toMatchObject({ employeeId: 'carol', reason: 'Employee sent these by chat', version: '22222222-2222-2222-2222-222222222222',
    photos: [{ note: 'Kitchen after cleaning' }] });
  expect(posted[0].photos[0].url).toMatch(/^data:image\/png;base64,/);
});
