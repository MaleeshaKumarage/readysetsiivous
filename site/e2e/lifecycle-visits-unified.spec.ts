import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { shiftAction } from './shift-actions';

// One Visits panel for every shift: generated (schedule) visits and manually created visits share the
// same list and actions. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

const generated = { startUtc: '2026-11-02T06:00:00.000Z', endUtc: '2026-11-02T08:00:00.000Z', assigneeIds: [], rosterVersion: 'v1' };
const manual = { exceptionId: 'manual-1', status: 4, originalStartUtc: null, startUtc: '2026-11-05T06:00:00.000Z', endUtc: '2026-11-05T08:00:00.000Z', reason: 'Extra clean', assigneeIds: [], visitNotes: 'Bring ladder' };

const shifts = [
  { id: 'per-visit', name: 'Per visit clean', companyId: 'company', branchId: 'branch', isActive: true, pricingMode: 1, rateNet: 40, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  { id: 'monthly', name: 'Monthly clean', companyId: 'company', branchId: 'branch', isActive: true, pricingMode: 0, rateNet: 500, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  { id: 'main', name: 'Branch main', companyId: 'company', branchId: 'branch', isActive: true, isBranchMain: true, schedule: { type: 4 } },
];

async function setup(page: Page, options: { failCancel?: boolean } = {}) {
  await setupAdminMocks(page);
  let visits: any[] = [generated, manual];
  const writes: any[] = []; const cancels: any[] = []; const restores: string[] = [];
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const cancel = path.match(/\/visits\/([^/]+)\/cancel$/);
    if (cancel) {
      const body = request.postDataJSON(); cancels.push(body);
      if (options.failCancel) return route.fulfill({ status: 409, headers, json: { title: 'Work has started on this visit, so it cannot be cancelled.' } });
      visits = visits.map(v => v.exceptionId === cancel[1] ? { ...v, isCancelledManualVisit: true, canAssign: false, reason: body.reason } : v);
      return route.fulfill({ status: 200, headers, json: visits.find(v => v.exceptionId === cancel[1]) });
    }
    const restore = path.match(/\/visits\/([^/]+)\/restore$/);
    if (restore) {
      restores.push(restore[1]);
      visits = visits.map(v => v.exceptionId === restore[1] ? { ...manual } : v);
      return route.fulfill({ status: 200, headers, json: manual });
    }
    if (path.includes('/occurrence-exceptions')) {
      const body = request.postDataJSON(); writes.push(body);
      return route.fulfill({ status: 200, headers, json: { ...body, exceptionId: body.id ?? 'saved' } });
    }
    if (path.endsWith('/occurrences')) return route.fulfill({ status: 200, headers, json: visits });
    const json = isShiftsRoute(path) ? shiftsResponse(shifts, request.url())
      : path.endsWith('/employees') ? [{ id: 'employee', firstName: 'Test', lastName: 'Employee', role: 'employee', isActive: true }]
      : path.endsWith('/companies') ? { items: [], total: 0 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/');
  return { writes, cancels, restores };
}

function rowFor(page: Page, text: string) {
  return page.getByRole('dialog', { name: /^Visits/ }).getByRole('row').filter({ hasText: text });
}

test('the shift menu opens one Visits list with generated and manual visits', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  const list = page.getByRole('dialog', { name: 'Visits — Per visit clean', exact: true });
  await expect(list).toBeVisible();
  await expect(list.getByText('11/2/2026, 8:00:00 AM')).toBeVisible();
  await expect(list.getByText('11/5/2026, 8:00:00 AM')).toBeVisible();
});

test('only a manual visit can be deleted; a generated visit offers Change and Cancel', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  const generatedRow = rowFor(page, '11/2/2026, 8:00:00 AM'); const manualRow = rowFor(page, '11/5/2026, 8:00:00 AM');
  await expect(generatedRow.getByRole('button', { name: 'Change', exact: true })).toBeVisible();
  await expect(generatedRow.getByRole('button', { name: 'Cancel', exact: true })).toBeVisible();
  await expect(generatedRow.getByRole('button', { name: 'Delete visit' })).toHaveCount(0);
  await expect(manualRow.getByRole('button', { name: 'Delete visit' })).toBeVisible();
  await expect(manualRow.getByRole('button', { name: 'Cancel', exact: true })).toBeVisible();
});

test('a per-visit shift offers a visit price; a monthly-fee shift does not', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await expect(rowFor(page, '11/2/2026, 8:00:00 AM').getByRole('button', { name: 'Visit price' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: /^Visits/ })).toHaveCount(0);
  await shiftAction(page, 'Monthly clean', 'Visits');
  await expect(page.getByRole('dialog', { name: 'Visits — Monthly clean', exact: true }).getByText('11/2/2026, 8:00:00 AM')).toBeVisible();
  await expect(page.getByRole('dialog', { name: /^Visits/ }).getByRole('button', { name: 'Visit price' })).toHaveCount(0);
});

test('editing a visit prefills its notes and saves changed notes', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Change', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Change visit', exact: true });
  const notes = editor.getByLabel('Notes for the employee (optional)');
  await expect(notes).toHaveValue('Bring ladder');
  await notes.fill('Bring ladder and the blue mop');
  await editor.getByRole('button', { name: 'Save visit' }).click();
  await expect(editor).toBeHidden();
  expect(state.writes[0]).toMatchObject({ id: 'manual-1', status: 4, notes: 'Bring ladder and the blue mop' });
});

test('clearing the notes sends an empty value; a visit with no notes sends none', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Change', exact: true }).click();
  let editor = page.getByRole('dialog', { name: 'Change visit', exact: true });
  await editor.getByLabel('Notes for the employee (optional)').fill('');
  await editor.getByRole('button', { name: 'Save visit' }).click();
  await expect(editor).toBeHidden();
  expect(state.writes[0].notes).toBe('');
  await rowFor(page, '11/2/2026, 8:00:00 AM').getByRole('button', { name: 'Change', exact: true }).click();
  editor = page.getByRole('dialog', { name: 'Change visit', exact: true });
  await expect(editor.getByLabel('Notes for the employee (optional)')).toHaveValue('');
  await editor.getByLabel('Reason').fill('Moved by customer');
  await editor.getByRole('button', { name: 'Save visit' }).click();
  await expect(editor).toBeHidden();
  expect(state.writes[1]).not.toHaveProperty('notes');
});

test('cancelling a manual visit asks who cancelled and why, then it can be restored', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Cancel', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Cancel visit', exact: true });
  await dialog.getByRole('button', { name: 'Cancel visit' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Enter a reason.');
  expect(state.cancels).toHaveLength(0);
  await dialog.getByLabel('Cancelled by').click();
  await page.getByRole('option', { name: 'Customer cancelled' }).click();
  await dialog.getByLabel('Reason').fill('Office closed');
  await dialog.getByRole('button', { name: 'Cancel visit' }).click();
  await expect(dialog).toBeHidden();
  expect(state.cancels).toEqual([{ status: 2, reason: 'Office closed' }]);
  const list = page.getByRole('dialog', { name: /^Visits/ });
  await expect(list.getByText('Visit cancelled. You can restore it from this list.')).toBeVisible();
  const cancelled = rowFor(page, '11/5/2026, 8:00:00 AM');
  await expect(cancelled.getByRole('button', { name: 'Delete visit' })).toHaveCount(0);
  await expect(cancelled.getByRole('button', { name: 'Change', exact: true })).toHaveCount(0);
  await cancelled.getByRole('button', { name: 'Restore' }).click();
  await expect(list.getByText('Visit restored.')).toBeVisible();
  expect(state.restores).toEqual(['manual-1']);
  await expect(rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Delete visit' })).toBeVisible();
});

test('Keep visit closes the cancel dialog without sending anything', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Cancel', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Cancel visit', exact: true });
  await dialog.getByLabel('Reason').fill('Changed my mind');
  await dialog.getByRole('button', { name: 'Keep visit' }).click();
  await expect(dialog).toBeHidden();
  expect(state.cancels).toHaveLength(0);
});

test('a refused cancel shows the reason and keeps the dialog open', async ({ page }) => {
  await setup(page, { failCancel: true });
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Cancel', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Cancel visit', exact: true });
  await dialog.getByLabel('Reason').fill('Office closed');
  await dialog.getByRole('button', { name: 'Cancel visit' }).click();
  await expect(dialog.getByRole('alert')).toBeVisible();
  await expect(dialog.getByLabel('Reason')).toHaveValue('Office closed');
});

test('a branch main shift visit is edited without a replacement employee field', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Branch main', 'Visits');
  await rowFor(page, '11/5/2026, 8:00:00 AM').getByRole('button', { name: 'Change', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Change visit', exact: true });
  await expect(editor.getByLabel('Notes for the employee (optional)')).toBeVisible();
  await expect(editor.getByLabel('Replacement employee (optional)')).toHaveCount(0);
});
