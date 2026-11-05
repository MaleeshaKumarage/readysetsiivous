import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { shiftAction } from './shift-actions';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const visitStart = '2026-11-02T06:00:00.000Z', visitEnd = '2026-11-02T08:00:00.000Z';
const clashStart = '2026-11-02T06:30:00.000Z', clashEnd = '2026-11-02T08:30:00.000Z';

// Mocks the morning shift (target) and an evening shift (the clashing visit).
// `clashHolders` lists who is on the clashing visit; the server refuses the target
// assignment until the clashing visit no longer holds the selected cleaner.
async function setup(page: Page, clashHolders: string[] = ['cleaner']) {
  await setupAdminMocks(page);
  const writes: { path: string; employeeIds: string[]; preview: boolean; mode: number }[] = [];
  let clashCleared = false;
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = path.endsWith('/shifts') ? [
      { id: 'shift', name: 'Morning clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
      { id: 'other', name: 'Evening deep clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:30', dailyEnd: '10:30' } },
    ] : path.endsWith('/employees') ? [
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
    writes.push({ path, employeeIds: body.employeeIds, preview: body.preview, mode: body.mode });
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
    if (path.includes('/shifts/other/')) return route.fulfill({ headers, json: [{ startUtc: clashStart, endUtc: clashEnd, assigneeIds: clashCleared ? [] : clashHolders, overrideEmployeeIds: clashCleared ? [] : clashHolders, rosterVersion: 'other-v1' }] });
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
  expect(state.writes).toEqual([
    { path: '/api/v1/admin/shifts/shift/visit-assignees', employeeIds: ['cleaner'], preview: true, mode: 1 },
    { path: '/api/v1/admin/shifts/other/visit-assignees', employeeIds: [], preview: false, mode: 1 },
    { path: '/api/v1/admin/shifts/shift/visit-assignees', employeeIds: ['cleaner'], preview: true, mode: 1 },
    { path: '/api/v1/admin/shifts/shift/visit-assignees', employeeIds: ['cleaner'], preview: false, mode: 1 },
  ]);
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
