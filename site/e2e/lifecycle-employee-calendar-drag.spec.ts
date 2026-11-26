import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Drag and drop on the desktop employee calendar. Times are Europe/Helsinki (UTC+2 in January).
// The grid starts at 08:00 and each hour is 72 px tall, so a y offset in a column maps to a time.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const MONDAY = '2030-01-14';
const HOUR_PX = 72;
// Pixel offset in a column for a local time, measured from the 08:00 grid start.
const yFor = (hour: number, minute = 0) => ((hour - 8) * 60 + minute) / 60 * HOUR_PX;

type Saved = { path: string; body: any };

async function setup(page: Page, options: { moveConflict?: boolean } = {}) {
  await setupAdminMocks(page);
  const saved: Saved[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (request.method() === 'PUT') {
      saved.push({ path, body: request.postDataJSON() });
      if (path.endsWith('/occurrence-exceptions')) {
        if (options.moveConflict) return route.fulfill({ status: 409, headers, json: { title: 'Conflict', detail: 'Neil Park already has Window clean at this time.' } });
        return route.fulfill({ headers, json: { exceptionId: 'moved-1', startUtc: request.postDataJSON().startUtc, endUtc: request.postDataJSON().endUtc } });
      }
      if (path.endsWith('/visit-assignees')) return route.fulfill({ headers, json: { employeeIds: request.postDataJSON().employeeIds, message: 'Saved', preview: false } });
    }
    if (path.endsWith('/employee-schedule')) return route.fulfill({ headers, json: [
      { employeeId: 'neil', localDay: `${MONDAY}T00:00:00`, visits: [
        // 09:00–10:00, an unchanged scheduled visit that can be moved and reassigned.
        { shiftId: 'deep', shiftName: 'Deep clean', startUtc: `${MONDAY}T07:00:00Z`, endUtc: `${MONDAY}T08:00:00Z`, workStatus: 'Scheduled',
          status: null, originalStartUtc: null, extraExceptionId: null, exceptionId: null, visitEmployeeId: null,
          assigneeIds: ['neil'], rosterVersion: 'roster-1', assignmentVersion: 'assign-1', isBranchMain: false, isPartnerBooking: false, moveLockReason: null },
        // 13:00–14:00, finished work: not draggable.
        { shiftId: 'done', shiftName: 'Office clean', startUtc: `${MONDAY}T11:00:00Z`, endUtc: `${MONDAY}T12:00:00Z`, workStatus: 'Completed',
          assigneeIds: ['neil'], rosterVersion: 'roster-2', assignmentVersion: null, moveLockReason: 'Started or completed work cannot be moved or reassigned.' },
      ] },
      { employeeId: 'aiden', localDay: `${MONDAY}T00:00:00`, visits: [
        // 10:00–11:00, a manual (extra) visit: moved by its own id.
        { shiftId: 'lawn', shiftName: 'Lawn service', startUtc: `${MONDAY}T08:00:00Z`, endUtc: `${MONDAY}T09:00:00Z`, workStatus: 'Scheduled',
          status: 4, originalStartUtc: null, extraExceptionId: 'extra-1', exceptionId: 'extra-1', visitEmployeeId: 'aiden',
          assigneeIds: ['aiden'], rosterVersion: 'roster-3', assignmentVersion: null, isBranchMain: false, isPartnerBooking: false, moveLockReason: null },
      ] },
    ] });
    if (path.endsWith('/overlap-audit')) return route.fulfill({ headers, json: [] });
    if (path.endsWith('/employees')) return route.fulfill({ headers, json: [
      { id: 'aiden', firstName: 'Aiden', lastName: 'Grey', role: 'employee', isActive: true, defaultHours: {} },
      { id: 'neil', firstName: 'Neil', lastName: 'Park', role: 'employee', isActive: true, defaultHours: {} },
    ] });
    if (path.endsWith('/tenant')) return route.fulfill({ headers, json: { companyName: 'QA', timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: [] });
  });
  await page.goto(`/admin/shifts/employee-schedule/?date=${MONDAY}`);
  await expect(page.getByRole('heading', { name: 'Employee schedule' })).toBeVisible();
  return saved;
}

const column = (page: Page, name: string) => page.getByRole('group', { name: `${name}, ${MONDAY}` });
const card = (page: Page, name: string, visit: RegExp) => column(page, name).getByRole('button', { name: visit });

/** Drags a card by its top edge, so the drop's y is the new start time. */
async function drag(page: Page, from: ReturnType<typeof card>, to: ReturnType<typeof column>, y: number) {
  await from.dragTo(to, { sourcePosition: { x: 20, y: 1 }, targetPosition: { x: 40, y: y + 1 } });
}

test.describe('desktop', () => {
  test.skip(({ isMobile }) => isMobile, 'Drag and drop is off on phones.');
  // Tall enough that the whole 08:00–17:00 grid is on screen, so drops in the afternoon are reachable.
  test.use({ viewport: { width: 1280, height: 1400 } });

  test('dragging a visit down its column moves it, snapped to 15 minutes, and Undo moves it back', async ({ page }) => {
    const saved = await setup(page);
    // Dropped at about 11:20, which snaps to 11:15 (09:15 UTC); the one-hour length is kept.
    await drag(page, card(page, 'Neil Park', /^Deep clean/), column(page, 'Neil Park'), yFor(11, 20));
    await expect(page.getByRole('status')).toContainText('Moved Deep clean to Mon 14 Jan 11:15.');
    const move = saved.find(item => item.path.endsWith('/shifts/deep/occurrence-exceptions'))!;
    expect(move.body).toMatchObject({ startUtc: '2030-01-14T09:15:00.000Z', endUtc: '2030-01-14T10:15:00.000Z', status: 1,
      originalStartUtc: '2030-01-14T07:00:00Z', reason: 'Moved in the employee calendar', employeeId: null });
    expect(saved.some(item => item.path.endsWith('/visit-assignees'))).toBe(false);

    await page.getByRole('status').getByRole('button', { name: 'Undo' }).click();
    await expect(page.getByRole('status')).toContainText('Change undone.');
    const undo = saved.filter(item => item.path.endsWith('/shifts/deep/occurrence-exceptions'))[1];
    expect(undo.body).toMatchObject({ id: 'moved-1', startUtc: '2030-01-14T07:00:00Z', endUtc: '2030-01-14T08:00:00Z', reason: 'Undo in the employee calendar' });
  });

  test('a manual visit is moved by its own id', async ({ page }) => {
    const saved = await setup(page);
    await drag(page, card(page, 'Aiden Grey', /^Lawn service/), column(page, 'Aiden Grey'), yFor(14));
    await expect(page.getByRole('status')).toContainText('Moved Lawn service to Mon 14 Jan 14:00.');
    const move = saved.find(item => item.path.endsWith('/shifts/lawn/occurrence-exceptions'))!;
    expect(move.body).toMatchObject({ id: 'extra-1', status: 4, originalStartUtc: null, employeeId: 'aiden',
      startUtc: '2030-01-14T12:00:00.000Z', endUtc: '2030-01-14T13:00:00.000Z' });
  });

  test('dragging a visit to another employee at the same time reassigns it', async ({ page }) => {
    const saved = await setup(page);
    await drag(page, card(page, 'Neil Park', /^Deep clean/), column(page, 'Aiden Grey'), yFor(9));
    await expect(page.getByRole('status')).toContainText('Reassigned Deep clean from Neil Park to Aiden Grey.');
    const assign = saved.find(item => item.path.endsWith('/shifts/deep/visit-assignees'))!;
    expect(assign.body).toMatchObject({ mode: 1, employeeIds: ['aiden'], reason: 'Reassigned in the employee calendar',
      rosterVersion: 'roster-1', assignmentVersion: 'assign-1', originalStartUtc: '2030-01-14T07:00:00Z', extraExceptionId: null, preview: false });
    // Same time, so the visit is not moved.
    expect(saved.some(item => item.path.endsWith('/occurrence-exceptions'))).toBe(false);
  });

  test('a refused move shows the server reason and offers no undo', async ({ page }) => {
    await setup(page, { moveConflict: true });
    await drag(page, card(page, 'Neil Park', /^Deep clean/), column(page, 'Neil Park'), yFor(12));
    await expect(page.getByRole('status')).toContainText('Neil Park already has Window clean at this time.');
    await expect(page.getByRole('status').getByRole('button', { name: 'Undo' })).toHaveCount(0);
  });

  test('finished visits cannot be dragged and say why on hover', async ({ page }) => {
    await setup(page);
    const finished = card(page, 'Neil Park', /^Office clean/);
    await expect(finished).not.toHaveAttribute('draggable', 'true');
    await expect(finished).toHaveAttribute('title', 'Started or completed work cannot be moved or reassigned.');
    await expect(card(page, 'Neil Park', /^Deep clean/)).toHaveAttribute('draggable', 'true');
    await expect(page.getByText('Drag a visit to change its time or employee.')).toBeVisible();
  });
});

test('on phones no visit can be dragged', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'Checks the phone layout.');
  await setup(page);
  await expect(card(page, 'Neil Park', /^Deep clean/)).toBeVisible();
  await expect(page.locator('[draggable="true"]')).toHaveCount(0);
  await expect(page.getByText(/^Drag a visit/)).toHaveCount(0);
});
