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
  return page.getByRole('dialog', { name: /^Visits of/ }).getByRole('row').filter({ hasText: text });
}

/** Opens the visit row's Actions menu and clicks one item (the design keeps visit actions in the menu). */
async function visitMenu(page: Page, rowText: string, label: string) {
  const target = rowFor(page, rowText).getByRole('button', { name: /^Actions for visit/ });
  await target.click();
  await page.locator(`[id="${await target.getAttribute('aria-controls')}"]`).getByRole('menuitem', { name: label, exact: true }).click();
}

test('the shift menu opens one Visits list with generated and manual visits', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  const list = page.getByRole('dialog', { name: 'Visits of Per visit clean', exact: true });
  await expect(list).toBeVisible();
  await expect(rowFor(page, 'Nov 2')).toBeVisible();
  await expect(rowFor(page, 'Nov 5')).toBeVisible();
});

test('only a manual visit can be deleted; a generated visit offers Change time and Cancel visit', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, 'Nov 2').getByRole('button', { name: /^Actions for visit/ }).click();
  await expect(page.getByRole('menuitem', { name: 'Change time', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Cancel visit', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Delete visit', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await rowFor(page, 'Nov 5').getByRole('button', { name: /^Actions for visit/ }).click();
  await expect(page.getByRole('menuitem', { name: 'Delete visit', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Cancel visit', exact: true })).toBeVisible();
});

test('a per-visit shift offers a visit price; a monthly-fee shift does not', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await rowFor(page, 'Nov 2').getByRole('button', { name: /^Actions for visit/ }).click();
  await expect(page.getByRole('menuitem', { name: 'Visit price', exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await page.getByRole('dialog', { name: 'Visits of Per visit clean', exact: true }).getByRole('button', { name: 'Close visits', exact: true }).click();
  await shiftAction(page, 'Monthly clean', 'Visits');
  await rowFor(page, 'Nov 2').getByRole('button', { name: /^Actions for visit/ }).click();
  await expect(page.getByRole('menuitem', { name: 'Visit price', exact: true })).toHaveCount(0);
});

test('editing a visit prefills its notes and saves changed notes', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await visitMenu(page, 'Nov 5', 'Change time');
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
  await visitMenu(page, 'Nov 5', 'Change time');
  let editor = page.getByRole('dialog', { name: 'Change visit', exact: true });
  await editor.getByLabel('Notes for the employee (optional)').fill('');
  await editor.getByRole('button', { name: 'Save visit' }).click();
  await expect(editor).toBeHidden();
  expect(state.writes[0].notes).toBe('');
  await visitMenu(page, 'Nov 2', 'Change time');
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
  await visitMenu(page, 'Nov 5', 'Cancel visit');
  const dialog = page.getByRole('dialog', { name: 'Cancel visit', exact: true });
  await dialog.getByRole('button', { name: 'Cancel visit', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Enter a reason.');
  expect(state.cancels).toHaveLength(0);
  await dialog.getByLabel('Cancelled by').click();
  await page.getByRole('option', { name: 'Customer cancelled' }).click();
  await dialog.getByLabel('Reason').fill('Office closed');
  await dialog.getByRole('button', { name: 'Cancel visit', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(state.cancels).toEqual([{ status: 2, reason: 'Office closed' }]);
  await expect(page.getByRole('status')).toHaveText('Visit cancelled. You can restore it from this list.');
  await rowFor(page, 'Nov 5').getByRole('button', { name: /^Actions for visit/ }).click();
  await expect(page.getByRole('menuitem', { name: 'Delete visit', exact: true })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Change time', exact: true })).toBeDisabled();
  await page.getByRole('menuitem', { name: 'Restore visit', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Visit restored.');
  expect(state.restores).toEqual(['manual-1']);
  await rowFor(page, 'Nov 5').getByRole('button', { name: /^Actions for visit/ }).click();
  await expect(page.getByRole('menuitem', { name: 'Delete visit', exact: true })).toBeVisible();
});

test('Keep visit closes the cancel dialog without sending anything', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, 'Per visit clean', 'Visits');
  await visitMenu(page, 'Nov 5', 'Cancel visit');
  const dialog = page.getByRole('dialog', { name: 'Cancel visit', exact: true });
  await dialog.getByLabel('Reason').fill('Changed my mind');
  await dialog.getByRole('button', { name: 'Keep visit' }).click();
  await expect(dialog).toBeHidden();
  expect(state.cancels).toHaveLength(0);
});

test('a refused cancel shows the reason and keeps the dialog open', async ({ page }) => {
  await setup(page, { failCancel: true });
  await shiftAction(page, 'Per visit clean', 'Visits');
  await visitMenu(page, 'Nov 5', 'Cancel visit');
  const dialog = page.getByRole('dialog', { name: 'Cancel visit', exact: true });
  await dialog.getByLabel('Reason').fill('Office closed');
  await dialog.getByRole('button', { name: 'Cancel visit', exact: true }).click();
  await expect(dialog.getByRole('alert')).toBeVisible();
  await expect(dialog.getByLabel('Reason')).toHaveValue('Office closed');
});

test('a branch main shift visit is edited without a replacement employee field', async ({ page }) => {
  await setup(page);
  await shiftAction(page, 'Branch main', 'Visits');
  await visitMenu(page, 'Nov 5', 'Change time');
  const editor = page.getByRole('dialog', { name: 'Change visit', exact: true });
  await expect(editor.getByLabel('Notes for the employee (optional)')).toBeVisible();
  await expect(editor.getByLabel('Replacement employee (optional)')).toHaveCount(0);
});
