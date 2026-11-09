import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { shiftAction } from './shift-actions';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const visitStart = '2026-11-02T06:00:00.000Z', visitEnd = '2026-11-02T08:00:00.000Z';
const clashStart = '2026-11-02T06:30:00.000Z', clashEnd = '2026-11-02T08:30:00.000Z';

// Mocks the morning shift (target) and an evening shift (the clashing visit).
// `clashHolders` lists who is on the clashing visit; the server refuses the target
// assignment until the clashing visit no longer holds the selected cleaner.
async function setup(page: Page, clashHolders: string[] = ['cleaner'], options: { extraClash?: boolean } = {}) {
  await setupAdminMocks(page);
  const writes: { path: string; employeeIds: string[]; preview: boolean; mode: number; body: Record<string, unknown> }[] = [];
  let clashCleared = false;
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = isShiftsRoute(path) ? shiftsResponse([
      { id: 'shift', name: 'Morning clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
      { id: 'other', name: 'Evening deep clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:30', dailyEnd: '10:30' } },
    ], route.request().url()) : path.endsWith('/employees') ? [
      { id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true },
      { id: 'second', firstName: 'Second', lastName: 'Cleaner', role: 'employee', isActive: true },
    ] : path.endsWith('/companies') ? { items: [], total: 0 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.route(/\/visit-assignees$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const body = route.request().postDataJSON() as { employeeIds: string[]; preview: boolean; mode: number };
    const path = new URL(route.request().url()).pathname;
    writes.push({ path, employeeIds: body.employeeIds, preview: body.preview, mode: body.mode, body: body as unknown as Record<string, unknown> });
    if (path.includes('/shifts/other/')) { clashCleared = true; return route.fulfill({ headers, json: { employeeIds: [], preview: false, message: 'Overlapping visit updated.' } }); }
    if (!clashCleared) return route.fulfill({ status: 409, headers, json: {
      title: 'Shift conflict',
      detail: 'Test Cleaner is already on Evening deep clean for an overlapping visit. Choose another cleaner or time, or remove the cleaner from the other visit first.',
      conflicts: [{ shiftId: 'other', shiftName: 'Evening deep clean', startUtc: clashStart, endUtc: clashEnd }],
    } });
    return route.fulfill({ headers, json: { employeeIds: body.employeeIds, preview: body.preview, message: 'Only this visit changes.' } });
  });
  await page.route(/\/occurrences(?:\?.*)?$/, route => {
    const path = new URL(route.request().url()).pathname;
    if (path.includes('/shifts/other/')) return route.fulfill({ headers, json: [{ startUtc: clashStart, endUtc: clashEnd, assigneeIds: clashCleared ? [] : clashHolders, overrideEmployeeIds: clashCleared ? [] : clashHolders, rosterVersion: 'other-v1', assignmentVersion: 'other-a1', ...(options.extraClash ? { status: 4, exceptionId: 'extra-1', originalStartUtc: null } : { originalStartUtc: clashStart }) }] });
    return route.fulfill({ headers, json: [{ startUtc: visitStart, endUtc: visitEnd, assigneeIds: [], rosterVersion: 'v1' }] });
  });
  await page.goto('/admin/shifts/');
  return { writes };
}

async function openAssignForMorningVisit(page: Page, reason: string) {
  await shiftAction(page, 'Morning clean', 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('button', { name: 'Assign', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Assign visit', exact: true });
  await editor.getByRole('textbox', { name: /^Employees/ }).click();
  await page.getByRole('option', { name: 'Test Cleaner', exact: true }).click();
  await page.keyboard.press('Escape');
  await editor.getByLabel('Assignment reason').fill(reason);
  return editor;
}

test('overlap refusal names the clashing visit, removes that cleaner from it, then assigns here', async ({ page }) => {
  const state = await setup(page);
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await expect(editor.getByText('Overlaps Evening deep clean')).toBeVisible();
  await expect(editor.getByText(/Choose another cleaner or time/)).toBeVisible();
  await editor.getByRole('button', { name: 'Remove from Evening deep clean and assign here' }).click();
  await expect(editor.getByText('Selected visit: Test Cleaner')).toBeVisible();
  await editor.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(editor).toHaveCount(0);
  expect(state.writes.map(({ path, employeeIds, preview, mode }) => ({ path, employeeIds, preview, mode }))).toEqual([
    { path: '/api/v1/admin/shifts/shift/visit-assignees', employeeIds: ['cleaner'], preview: true, mode: 1 },
    { path: '/api/v1/admin/shifts/other/visit-assignees', employeeIds: [], preview: false, mode: 1 },
    { path: '/api/v1/admin/shifts/shift/visit-assignees', employeeIds: ['cleaner'], preview: true, mode: 1 },
    { path: '/api/v1/admin/shifts/shift/visit-assignees', employeeIds: ['cleaner'], preview: false, mode: 1 },
  ]);
  // The removal request carries the clashing visit's identity and versions, so the server can reject a stale edit.
  expect(state.writes[1].body).toMatchObject({ originalStartUtc: clashStart, extraExceptionId: null, rosterVersion: 'other-v1', assignmentVersion: 'other-a1', fromThisDate: false, preview: false, employeeIds: [] });
  expect(String(state.writes[1].body.reason).length).toBeGreaterThan(0);
});

test('removing a cleaner from an extra (status 4) clashing visit sends its exception id and no original start', async ({ page }) => {
  const state = await setup(page, ['cleaner'], { extraClash: true });
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await editor.getByRole('button', { name: 'Remove from Evening deep clean and assign here' }).click();
  await expect(editor.getByText('Selected visit: Test Cleaner')).toBeVisible();
  expect(state.writes[1].path).toBe('/api/v1/admin/shifts/other/visit-assignees');
  expect(state.writes[1].body).toMatchObject({ originalStartUtc: null, extraExceptionId: 'extra-1', rosterVersion: 'other-v1', assignmentVersion: 'other-a1' });
});

test('overlap removal explains when the cleaner is no longer on the clashing visit', async ({ page }) => {
  await setup(page, []);
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await expect(editor.getByText('Overlaps Evening deep clean')).toBeVisible();
  await editor.getByRole('button', { name: 'Remove from Evening deep clean and assign here' }).click();
  await expect(editor.getByText('The selected cleaners are no longer on the overlapping visit. Preview again to continue.')).toBeVisible();
  await expect(editor.getByRole('button', { name: 'Save assignment', exact: true })).toBeDisabled();
});

test('a failed save shows the reason as a toast and keeps the dialog open to retry', async ({ page }) => {
  await setup(page, []);
  await page.route(/\/visit-assignees$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const body = route.request().postDataJSON() as { preview: boolean };
    if (body.preview) return route.fulfill({ headers, json: { employeeIds: ['cleaner'], preview: true, message: 'Only this visit changes.' } });
    return route.fulfill({ status: 500, headers, json: { detail: 'Assignments could not be saved. Your changes were kept; retry the save.' } });
  });
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await expect(editor.getByText('Selected visit: Test Cleaner')).toBeVisible();
  await editor.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(page.getByText('Assignment not saved')).toBeVisible();
  await expect(page.getByText('Assignments could not be saved. Your changes were kept; retry the save.').first()).toBeVisible();
  await expect(editor).toBeVisible();
});

test('overlap refusal on save shows the server message that names the clashing visit and keeps the editor open', async ({ page }) => {
  await setup(page, []);
  let saves = 0;
  // Preview succeeds; the save is refused with the server's own wording (VisitAssignmentSafety.OverlapMessage).
  await page.route(/\/visit-assignees$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const body = route.request().postDataJSON() as { preview: boolean };
    if (body.preview) return route.fulfill({ headers, json: { employeeIds: ['cleaner'], preview: true, message: 'Only this visit changes.' } });
    saves++;
    return route.fulfill({ status: 409, headers, json: {
      title: 'Shift conflict',
      detail: 'This cleaner already has a visit that overlaps the visit starting 2 Nov 2026 08:00. It clashes with "Evening deep clean", 2 Nov 2026 08:30 to 2 Nov 2026 10:30. No assignments were saved. Choose another cleaner or time, or remove the cleaner from the other visit first.',
      conflicts: [{ shiftId: 'other', shiftName: 'Evening deep clean', startUtc: clashStart, endUtc: clashEnd }],
    } });
  });
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await expect(editor.getByText('Selected visit: Test Cleaner')).toBeVisible();
  await editor.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect.poll(() => saves).toBe(1);
  await expect(page.getByText(/It clashes with "Evening deep clean", 2 Nov 2026 08:30 to 2 Nov 2026 10:30\./).first()).toBeVisible();
  await expect(editor).toBeVisible();
  await expect(editor.getByRole('button', { name: 'Save assignment', exact: true })).toBeVisible();
});

// The clashing shift is loaded by id, so it can be on another page of the shift list.
async function mockClashingShift(page: Page, status = 200) {
  await page.route(/\/api\/v1\/admin\/shifts\/other$/, route => {
    if (route.request().method() !== 'GET') return route.fallback();
    if (status !== 200) return route.fulfill({ status, headers, json: { detail: 'Shift not found.' } });
    return route.fulfill({ headers, json: { id: 'other', name: 'Evening deep clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:30', dailyEnd: '10:30' } } });
  });
}

test('overlap refusal links to the clashing shift, whose visit list opens with the overlapping visit to move', async ({ page }) => {
  await setup(page, ['cleaner']);
  await mockClashingShift(page);
  // A second, non-clashing visit proves the marker is only on the overlapping row.
  await page.route(/\/shifts\/other\/occurrences/, route => route.fulfill({ headers, json: [
    { startUtc: clashStart, endUtc: clashEnd, assigneeIds: ['cleaner'], overrideEmployeeIds: ['cleaner'], rosterVersion: 'other-v1', assignmentVersion: 'other-a1', originalStartUtc: clashStart },
    { startUtc: '2026-11-05T06:00:00.000Z', endUtc: '2026-11-05T08:00:00.000Z', assigneeIds: [], rosterVersion: 'other-v2' },
  ] }));
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await expect(editor.getByText('Overlaps Evening deep clean')).toBeVisible();
  const occurrenceRequest = page.waitForRequest(request => request.url().includes('/shifts/other/occurrences'));
  await editor.getByRole('button', { name: 'View Evening deep clean visits', exact: true }).click();
  // The assignment dialog closes and the clashing shift's visits open in its place.
  await expect(editor).toHaveCount(0);
  const list = page.getByRole('dialog', { name: 'Occurrences — Evening deep clean', exact: true });
  await expect(list).toBeVisible();
  await expect(list.getByText('Showing Evening deep clean. The overlapping visit is marked below.')).toBeVisible();
  // The requested range always includes the overlapping visit, even when it falls outside the default 31-day window.
  const range = new URL((await occurrenceRequest).url()).searchParams;
  expect(Date.parse(range.get('from') ?? '')).toBeLessThanOrEqual(Date.parse(clashStart));
  expect(Date.parse(range.get('to') ?? '')).toBeGreaterThanOrEqual(Date.parse(clashEnd));
  // That visit is marked and keeps its own Assign action, so the admin can change it there.
  // Only the overlapping row is marked, and its own Assign action is the one that is reachable there.
  await expect(list.getByText('Overlapping visit', { exact: true })).toHaveCount(1);
  const clashRow = list.getByRole('row').filter({ hasText: 'Overlapping visit' });
  await expect(clashRow.getByRole('button', { name: 'Assign', exact: true })).toBeVisible();
});

test('the clashing-shift link is also offered when the save itself is refused for the overlap', async ({ page }) => {
  await setup(page, []);
  await mockClashingShift(page);
  await page.route(/\/visit-assignees$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const body = route.request().postDataJSON() as { preview: boolean };
    if (body.preview) return route.fulfill({ headers, json: { employeeIds: ['cleaner'], preview: true, message: 'Only this visit changes.' } });
    return route.fulfill({ status: 409, headers, json: {
      title: 'Shift conflict',
      detail: 'This cleaner already has a visit that overlaps the visit starting 2 Nov 2026 08:00. It clashes with "Evening deep clean", 2 Nov 2026 08:30 to 2 Nov 2026 10:30. No assignments were saved. Choose another cleaner or time, or remove the cleaner from the other visit first.',
      conflicts: [{ shiftId: 'other', shiftName: 'Evening deep clean', startUtc: clashStart, endUtc: clashEnd }],
    } });
  });
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await expect(editor.getByText('Selected visit: Test Cleaner')).toBeVisible();
  await editor.getByRole('button', { name: 'Save assignment', exact: true }).click();
  await expect(editor.getByRole('button', { name: 'View Evening deep clean visits', exact: true })).toBeVisible();
  await editor.getByRole('button', { name: 'View Evening deep clean visits', exact: true }).click();
  await expect(editor).toHaveCount(0);
  await expect(page.getByRole('dialog', { name: 'Occurrences — Evening deep clean', exact: true })).toBeVisible();
});

test('when the clashing shift cannot be loaded, the assignment stays open with the cleaner and reason kept', async ({ page }) => {
  await setup(page, ['cleaner']);
  await mockClashingShift(page, 404);
  const editor = await openAssignForMorningVisit(page, 'Cover the morning visit');
  await editor.getByRole('button', { name: 'Preview assignment', exact: true }).click();
  await editor.getByRole('button', { name: 'View Evening deep clean visits', exact: true }).click();
  await expect(page.getByText('Could not open shift').first()).toBeVisible();
  // The refusal stays on screen, so the admin can still remove the cleaner from that visit or retry.
  await expect(editor).toBeVisible();
  await expect(editor.getByText('Overlaps Evening deep clean')).toBeVisible();
  await expect(editor.getByRole('button', { name: 'Remove from Evening deep clean and assign here' })).toBeVisible();
  await expect(editor.getByLabel('Assignment reason')).toHaveValue('Cover the morning visit');
  await expect(page.getByRole('dialog', { name: /Occurrences — Evening deep clean/ })).toHaveCount(0);
});
