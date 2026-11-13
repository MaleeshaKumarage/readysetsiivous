import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { shiftAction } from './shift-actions';

// A property has one main visit shift. Staff add visits to it by date or date range; each visit is numbered,
// tagged with where it came from, and offered to a cleaner. The API is mocked here; the rules themselves are
// covered by the backend database tests (BranchVisitServiceDatabaseTests, BranchMainShiftDatabaseTests).

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const MAIN = 'Cleaning - Testikatu 1, 00100 Helsinki |34m2|2H|1V';

const adminVisit = { startUtc: '2026-11-10T06:00:00.000Z', endUtc: '2026-11-10T08:00:00.000Z', exceptionId: 'visit-1', status: 4, reason: 'Added from the admin panel',
  assigneeIds: [], rosterVersion: 'v1', canAssign: true, visitNumber: 1, visitSource: 'AdminPanel', visitCleaningType: 'Normal', offeredEmployeeId: 'cleaner', offerRevision: 1, isPartnerBooking: false };
const partnerVisit = { startUtc: '2026-11-10T10:00:00.000Z', endUtc: '2026-11-14T14:00:00.000Z', exceptionId: 'visit-2', status: 4, reason: 'Accepted partner cleaning',
  employeeId: 'second', assigneeIds: ['second'], rosterVersion: 'v1', canAssign: true, visitNumber: 2, visitSource: 'PartnerPortal', visitCleaningType: 'Normal', offerRevision: 0, isPartnerBooking: true };

async function setup(page: Page, options: { addFailures?: number; cleanerConflict?: boolean; sameNameCleaners?: boolean } = {}) {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-11-03T10:00:00Z') });
  const state = { adds: [] as any[], cleanerWrites: [] as any[], cleaningTypeWrites: [] as any[], defaultWrites: [] as any[], extraWrites: 0,
    occurrences: [adminVisit, partnerVisit] as any[], addFailures: options.addFailures ?? 0, defaultEmployeeId: null as string | null };
  const shifts = () => [
    { id: 'main', name: MAIN, companyId: 'company', branchId: 'branch', isActive: true, isBranchMain: true, defaultEmployeeId: state.defaultEmployeeId, schedule: { type: 4 } },
    { id: 'weekly', name: 'Morning clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  ];
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/branch-visits') && method === 'POST') {
      const body = request.postDataJSON(); state.adds.push(body);
      if (state.addFailures > 0) { state.addFailures--; return route.fulfill({ status: 409, headers, json: { detail: 'This property has no cleaning price yet. Set the property price before adding a visit, so the visit can be invoiced.' } }); }
      const number = state.occurrences.length + 1;
      const startUtc = new Date(`${body.fromDate}T${body.startTime}:00+02:00`).toISOString();
      const endUtc = new Date(`${body.toDate}T${body.endTime}:00+02:00`).toISOString();
      const offered = body.employeeId ?? (body.useDefaultEmployee ? state.defaultEmployeeId : null);
      state.occurrences.push({ startUtc, endUtc, exceptionId: `visit-${number}`, status: 4, assigneeIds: [], rosterVersion: 'v1', canAssign: true,
        visitNumber: number, visitSource: 'AdminPanel', offeredEmployeeId: offered, offerRevision: offered ? 1 : 0, isPartnerBooking: false });
      return route.fulfill({ status: 200, headers, json: { id: `visit-${number}`, shiftId: 'main', branchId: body.branchId, number, source: 'AdminPanel', startUtc, endUtc,
        fromDate: body.fromDate, toDate: body.toDate, notes: body.notes, employeeId: null, offeredEmployeeId: offered, offerRevision: offered ? 1 : 0,
        status: offered ? 'Offered' : 'Unassigned', timeZoneId: 'Europe/Helsinki' } });
    }
    if (path.endsWith('/default-employee')) {
      const body = request.postDataJSON(); state.defaultWrites.push(body); state.defaultEmployeeId = body.employeeId;
      return route.fulfill({ status: 200, headers, json: { shiftId: 'main', defaultEmployeeId: body.employeeId } });
    }
    if (/\/visits\/[^/]+\/employee$/.test(path)) {
      const body = request.postDataJSON(); state.cleanerWrites.push({ path, ...body });
      if (options.cleanerConflict) return route.fulfill({ status: 409, headers, json: { detail: 'Work has started on this visit, so its cleaner cannot be changed.' } });
      state.occurrences = state.occurrences.map(item => item.exceptionId === 'visit-1' ? { ...item, offeredEmployeeId: body.employeeId, offerRevision: body.offerRevision + 2 } : item);
      return route.fulfill({ status: 200, headers, json: { id: 'visit-1', shiftId: 'main', number: 1, status: 'Offered', offeredEmployeeId: body.employeeId, offerRevision: body.offerRevision + 2 } });
    }
    const cleaningType = /\/visits\/([^/]+)\/cleaning-type$/.exec(path);
    if (cleaningType) {
      const body = request.postDataJSON(); state.cleaningTypeWrites.push({ visitId: cleaningType[1], ...body });
      state.occurrences = state.occurrences.map(item => item.exceptionId === cleaningType[1] ? { ...item, visitCleaningType: body.cleaningType } : item);
      return route.fulfill({ status: 200, headers, json: { id: cleaningType[1], shiftId: 'main', cleaningType: body.cleaningType } });
    }
    if (path.includes('/occurrence-exceptions')) { state.extraWrites++; return route.fulfill({ status: 409, headers, json: { detail: 'Add visits to this shift with Add visit.' } }); }
    if (path.endsWith('/occurrences')) return route.fulfill({ status: 200, headers, json: path.includes('/shifts/main/') ? state.occurrences : [] });
    const json = isShiftsRoute(path) ? shiftsResponse(shifts(), request.url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'Acme Hotels', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'Helsinki Office', isActive: true }, { id: 'closed', companyId: 'company', name: 'Closed Office', isActive: false }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Acme Hotels', isActive: true }], total: 1 }
      : path.endsWith('/employees') ? (options.sameNameCleaners
        ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', email: 'test.cleaner@example.com', role: 'employee', isActive: true }, { id: 'second', firstName: 'Test', lastName: 'Cleaner', email: 'second.cleaner@example.com', role: 'employee', isActive: true }]
        : [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }, { id: 'second', firstName: 'Second', lastName: 'Cleaner', role: 'employee', isActive: true }, { id: 'partner', firstName: 'Active', lastName: 'Partner', role: 'partner', isActive: true }])
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('button', { name: `More actions for ${MAIN}`, exact: true })).toBeVisible();
  return state;
}

async function chooseProperty(page: Page) {
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await dialog.getByRole('textbox', { name: 'Company', exact: true }).click();
  await page.getByRole('option', { name: 'Acme Hotels', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Property', exact: true }).click();
  // An inactive property cannot take a visit, so it is not offered.
  await expect(page.getByRole('option', { name: 'Closed Office', exact: true })).toHaveCount(0);
  await page.getByRole('option', { name: 'Helsinki Office', exact: true }).click();
  return dialog;
}

test('admin adds a single-day visit by choosing only the property, date and time', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Add visit');
  // Opened from the property's row, so the company and property are fixed; they are not asked again.
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await expect(dialog.getByLabel('Property', { exact: true })).toHaveValue(MAIN);
  // Company details and prices are never asked for: they come from the property.
  await expect(dialog.getByLabel(/price|vat|business id|address/i)).toHaveCount(0);
  await dialog.getByLabel(/^Date/).fill('2026-11-12');
  await dialog.getByLabel(/^Start time/).fill('09:00');
  await dialog.getByLabel(/^End time/).fill('11:30');
  await dialog.getByLabel('Notes for the cleaner (optional)').fill('Ring the bell at the back door');
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(state.adds).toEqual([{ branchId: 'branch', fromDate: '2026-11-12', toDate: '2026-11-12', startTime: '09:00', endTime: '11:30',
    notes: 'Ring the bell at the back door', employeeId: null, useDefaultEmployee: true, cleaningType: 'Normal' }]);
});

test('two cleaners with the same name are told apart by email in the cleaner list', async ({ page }) => {
  const state = await setup(page, { sameNameCleaners: true });
  await shiftAction(page, MAIN, 'Add visit');
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await dialog.getByLabel(/^Date/).fill('2026-11-12');
  await dialog.getByRole('textbox', { name: 'Cleaner', exact: true }).click();
  await expect(page.getByRole('option', { name: 'Test Cleaner (test.cleaner@example.com)', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Test Cleaner (second.cleaner@example.com)', exact: true })).toBeVisible();
  await page.getByRole('option', { name: 'Test Cleaner (second.cleaner@example.com)', exact: true }).click();
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(state.adds[0]).toMatchObject({ fromDate: '2026-11-12', employeeId: 'second', useDefaultEmployee: false });
});

test('a date range is one visit, and a chosen cleaner is sent an offer', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Add visit');
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  // Opened from the shift: the property is fixed and no company has to be picked.
  await expect(dialog.getByLabel('Property', { exact: true })).toHaveValue(MAIN);
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveCount(0);
  await dialog.getByLabel(/^Date/).fill('2026-11-21');
  await dialog.getByLabel('Last day (optional)').fill('2026-11-25');
  await expect(dialog.getByRole('status')).toHaveText('One visit covering 5 days, from 2026-11-21 08:00 to 2026-11-25 10:00.');
  await dialog.getByRole('textbox', { name: 'Cleaner', exact: true }).click();
  // Partners are not cleaners and are never offered a visit.
  await expect(page.getByRole('option', { name: 'Active Partner', exact: true })).toHaveCount(0);
  await page.getByRole('option', { name: 'Second Cleaner', exact: true }).click();
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(state.adds).toHaveLength(1);
  expect(state.adds[0]).toMatchObject({ branchId: 'branch', fromDate: '2026-11-21', toDate: '2026-11-25', employeeId: 'second', useDefaultEmployee: false });
});

test('a visit can be left unassigned, and bad times are stopped before anything is sent', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Add visit');
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Choose the date of the visit.');
  await dialog.getByLabel(/^Date/).fill('2026-11-12');
  await dialog.getByLabel(/^Start time/).fill('12:00');
  await dialog.getByLabel(/^End time/).fill('10:00');
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('The end time must be after the start time.');
  expect(state.adds).toHaveLength(0);
  await dialog.getByLabel(/^End time/).fill('14:00');
  await dialog.getByRole('textbox', { name: 'Cleaner', exact: true }).click();
  await page.getByRole('option', { name: 'Leave unassigned', exact: true }).click();
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.adds).toEqual([expect.objectContaining({ employeeId: null, useDefaultEmployee: false })]);
});

test('a refused visit shows the reason, keeps what was typed, and a retry adds it once', async ({ page }) => {
  const state = await setup(page, { addFailures: 1 });
  await shiftAction(page, MAIN, 'Add visit');
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await dialog.getByLabel(/^Date/).fill('2026-11-12');
  await dialog.getByLabel('Notes for the cleaner (optional)').fill('Keys at reception');
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('This property has no cleaning price yet. Set the property price before adding a visit, so the visit can be invoiced.');
  await expect(dialog.getByLabel(/^Date/)).toHaveValue('2026-11-12');
  await expect(dialog.getByLabel('Notes for the cleaner (optional)')).toHaveValue('Keys at reception');
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.adds).toHaveLength(2);
  expect(state.occurrences.filter(item => item.visitNumber === 3)).toHaveLength(1);
});

test('the visit list names each visit with its number, dates and source', async ({ page }) => {
  await setup(page);
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await expect(list.getByTestId('visit-name')).toHaveText(['#1 · Tue 10 Nov · Admin panel', '#2 · 10–14 Nov · Partner portal']);
  const first = list.getByRole('row').filter({ hasText: '#1 ·' });
  await expect(first).toContainText('Offer sent to Test Cleaner');
  await expect(first).toContainText('Waiting for the cleaner to accept');
  // A partner booking is changed through its request, so the visit list offers no actions for it.
  const booked = list.getByRole('row').filter({ hasText: '#2 ·' });
  await expect(booked).toContainText('Second Cleaner');
  await expect(booked).toContainText('Managed under Partner requests');
  // A partner booking's cleaner is managed through its request, but its cleaning type can be set by staff.
  await expect(booked.getByRole('button')).toHaveCount(1);
  await expect(booked.getByRole('button', { name: 'Change cleaning type', exact: true })).toBeVisible();
  // No price is shown in the visit list.
  await expect(list).not.toContainText('€');
});

test('a visit added from the open list appears there with the next number', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  // A main shift takes visits through Add visit only, so each one is numbered.
  await expect(list.getByRole('button', { name: 'Add extra visit', exact: true })).toHaveCount(0);
  await list.getByRole('button', { name: 'Add visit', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await dialog.getByLabel(/^Date/).fill('2026-11-17');
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(list.getByRole('status')).toHaveText(`Visit #3 added to ${MAIN}. It has no cleaner yet.`);
  await expect(list.getByTestId('visit-name')).toHaveText(['#1 · Tue 10 Nov · Admin panel', '#2 · 10–14 Nov · Partner portal', '#3 · Tue 17 Nov · Admin panel']);
  expect(state.extraWrites).toBe(0);
});

test('admin sets the default cleaner and changes the cleaner of one visit', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('textbox', { name: 'Default cleaner', exact: true }).click();
  await page.getByRole('option', { name: 'Second Cleaner', exact: true }).click();
  await expect(list.getByRole('textbox', { name: 'Default cleaner', exact: true })).toHaveValue('Second Cleaner');
  expect(state.defaultWrites).toEqual([{ employeeId: 'second' }]);

  await list.getByRole('row').filter({ hasText: '#1 ·' }).getByRole('button', { name: 'Change cleaner', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Change cleaner', exact: true });
  await expect(editor).toContainText('#1 · Tue 10 Nov · Admin panel');
  await expect(editor.getByRole('textbox', { name: 'Cleaner', exact: true })).toHaveValue('Test Cleaner');
  await editor.getByRole('textbox', { name: 'Cleaner', exact: true }).click();
  await page.getByRole('option', { name: 'Second Cleaner', exact: true }).click();
  await editor.getByRole('button', { name: 'Save cleaner', exact: true }).click();
  await expect(editor).toHaveCount(0);
  // The version the screen was showing is sent, so a stale screen cannot overwrite a newer choice.
  expect(state.cleanerWrites).toEqual([{ path: '/api/v1/admin/branch-visits/shifts/main/visits/visit-1/employee', employeeId: 'second', offerRevision: 1 }]);
  await expect(list.getByRole('row').filter({ hasText: '#1 ·' })).toContainText('Offer sent to Second Cleaner');
});

test('a cleaner change refused by the server shows why and leaves the visit as it was', async ({ page }) => {
  const state = await setup(page, { cleanerConflict: true });
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('row').filter({ hasText: '#1 ·' }).getByRole('button', { name: 'Change cleaner', exact: true }).click();
  const editor = page.getByRole('dialog', { name: 'Change cleaner', exact: true });
  await editor.getByRole('textbox', { name: 'Cleaner', exact: true }).click();
  await page.getByRole('option', { name: 'Second Cleaner', exact: true }).click();
  await editor.getByRole('button', { name: 'Save cleaner', exact: true }).click();
  await expect(editor.getByRole('alert')).toHaveText('Work has started on this visit, so its cleaner cannot be changed.');
  await expect(editor).toBeVisible();
  expect(state.cleanerWrites).toHaveLength(1);
  await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(list.getByRole('row').filter({ hasText: '#1 ·' })).toContainText('Offer sent to Test Cleaner');
});

test('a main shift has no whole-shift employees action; other shifts keep theirs', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: `More actions for ${MAIN}`, exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Add visit', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Assign employees', exact: true })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Schedule one-time visit', exact: true })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Pricing', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  // The first menu must be gone before the next one opens, or its closing layer swallows the click.
  await expect(page.getByRole('menu')).toHaveCount(0);
  await page.getByRole('button', { name: 'More actions for Morning clean', exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Assign employees', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Add visit', exact: true })).toHaveCount(0);
});

test('a new shift no longer offers One-time: single visits are added with Add visit', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByRole('textbox', { name: 'Schedule', exact: true }).click();
  await expect(page.getByRole('option', { name: 'Daily', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Monthly', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'One-time', exact: true })).toHaveCount(0);
});

test('the details of a main shift explain visits, and its row menu has no whole-shift assignment or separate price', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: `More actions for ${MAIN}`, exact: true }).click();
  await page.getByRole('menuitem', { name: 'View details', exact: true }).click();
  const workspace = page.getByRole('dialog', { name: `Manage — ${MAIN}`, exact: true });
  await expect(workspace.getByRole('status')).toHaveText('Next step: Admin: add a visit. It is offered to the default cleaner, or to the cleaner you choose.');
  await expect(workspace).toContainText('Every booking and every added visit for this property is a numbered visit on this shift.');
  await expect(workspace).toContainText('Each visit uses the property price in force on its date.');
  await page.keyboard.press('Escape');
  await expect(workspace).toHaveCount(0);
  // A whole-shift assignment or a shift-level price would bypass the visit's own offer and the property price.
  await page.getByRole('button', { name: `More actions for ${MAIN}`, exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Assign employees', exact: true })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Pricing', exact: true })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Schedule one-time visit', exact: true })).toHaveCount(0);
  await page.getByRole('menuitem', { name: 'Add visit', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await expect(dialog.getByLabel('Property', { exact: true })).toHaveValue(MAIN);
  await dialog.getByLabel(/^Date/).fill('2026-11-19');
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(state.adds).toEqual([expect.objectContaining({ branchId: 'branch', fromDate: '2026-11-19', toDate: '2026-11-19' })]);
});

test('an ordinary shift keeps its assignment and pricing actions in its row menu', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'More actions for Morning clean', exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Assign employees', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Pricing', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Add visit', exact: true })).toHaveCount(0);
});

test('a new visit is Normal unless Deep is chosen, and the choice is sent with it', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Add visit');
  const dialog = page.getByRole('dialog', { name: 'Add visit', exact: true });
  await dialog.getByLabel(/^Date/).fill('2026-11-21');
  await expect(dialog.getByRole('textbox', { name: 'Cleaning type', exact: true })).toHaveValue('Normal');
  await dialog.getByRole('textbox', { name: 'Cleaning type', exact: true }).click();
  await page.getByRole('option', { name: 'Deep', exact: true }).click();
  await expect(dialog.getByText('Deep cleaning adds €15.00 excl. VAT to this visit.', { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Add visit', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(state.adds).toHaveLength(1);
  expect(state.adds[0]).toMatchObject({ fromDate: '2026-11-21', cleaningType: 'Deep' });
});

test('a visit that is already cleaned can be changed from Normal to Deep', async ({ page }) => {
  const state = await setup(page);
  // A visit whose cleaning is already done (the clock is set to 3 November 2026).
  state.occurrences = [{ ...adminVisit, exceptionId: 'visit-done', startUtc: '2026-10-20T06:00:00.000Z', endUtc: '2026-10-20T08:00:00.000Z', visitNumber: 3, visitCleaningType: 'Normal' }];
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  const row = list.getByRole('row').filter({ hasText: '#3 ·' });
  await expect(row).toContainText('Normal cleaning');

  await row.getByRole('button', { name: 'Change cleaner', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Change cleaner', exact: true });
  await modal.getByRole('textbox', { name: 'Cleaning type', exact: true }).click();
  await page.getByRole('option', { name: 'Deep', exact: true }).click();
  await expect(modal.getByText('Deep cleaning adds €15.00 excl. VAT to this visit.', { exact: true })).toBeVisible();
  await modal.getByRole('button', { name: 'Save cleaning type', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(state.cleaningTypeWrites).toEqual([expect.objectContaining({ visitId: 'visit-done', cleaningType: 'Deep' })]);
  await expect(list.getByRole('row').filter({ hasText: '#3 ·' })).toContainText('Deep cleaning');
});

test('staff set Deep or Normal on a partner visit from the visit list, and the booking follows', async ({ page }) => {
  const state = await setup(page);
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  const booked = list.getByRole('row').filter({ hasText: '#2 ·' });
  await expect(booked).toContainText('Normal cleaning');
  await booked.getByRole('button', { name: 'Change cleaning type', exact: true }).click();

  const modal = page.getByRole('dialog', { name: 'Cleaning type', exact: true });
  // The booking's cleaner is not changed from here: only the cleaning type is offered.
  await expect(modal.getByRole('textbox', { name: 'Cleaner', exact: true })).toHaveCount(0);
  await modal.getByRole('textbox', { name: 'Cleaning type', exact: true }).click();
  await page.getByRole('option', { name: 'Deep', exact: true }).click();
  await modal.getByRole('button', { name: 'Save cleaning type', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(state.cleaningTypeWrites).toEqual([expect.objectContaining({ visitId: 'visit-2', cleaningType: 'Deep' })]);
  expect(state.cleanerWrites).toHaveLength(0);
  await expect(list.getByRole('row').filter({ hasText: '#2 ·' })).toContainText('Deep cleaning');
});

test('a cleaning type picked and cancelled on one visit is not carried to the next visit', async ({ page }) => {
  const state = await setup(page);
  // Two Normal visits, so the same modal opens for each in turn.
  state.occurrences = [adminVisit, { ...adminVisit, exceptionId: 'visit-3', visitNumber: 3, startUtc: '2026-11-12T06:00:00.000Z', endUtc: '2026-11-12T08:00:00.000Z' }];
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  const modal = page.getByRole('dialog', { name: 'Change cleaner', exact: true });

  await list.getByRole('row').filter({ hasText: '#1 ·' }).getByRole('button', { name: 'Change cleaner', exact: true }).click();
  await modal.getByRole('textbox', { name: 'Cleaning type', exact: true }).click();
  await page.getByRole('option', { name: 'Deep', exact: true }).click();
  await modal.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(modal).toHaveCount(0);

  await list.getByRole('row').filter({ hasText: '#3 ·' }).getByRole('button', { name: 'Change cleaner', exact: true }).click();
  await expect(modal.getByRole('textbox', { name: 'Cleaning type', exact: true })).toHaveValue('Normal');
  await modal.getByRole('button', { name: 'Save cleaning type', exact: true }).click();
  await expect(modal).toHaveCount(0);
  expect(state.cleaningTypeWrites).toEqual([expect.objectContaining({ visitId: 'visit-3', cleaningType: 'Normal' })]);
});


test('a visit that has already ended with a waiting offer says the cleaner can still accept it', async ({ page }) => {
  const state = await setup(page);
  state.occurrences = [{ ...adminVisit, startUtc: '2026-11-02T06:00:00.000Z', endUtc: '2026-11-02T08:00:00.000Z' }];
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await expect(list.getByTestId('past-offer-note')).toContainText('The cleaner can still accept the offer. To change the cleaner, delete this visit and add it again.');
});

test('a visit row shows what its work is doing: Done once the cleaner has finished', async ({ page }) => {
  const state = await setup(page);
  state.occurrences = [{ ...adminVisit, workStatus: 'Completed' }, { ...partnerVisit, workStatus: 'Scheduled' }];
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await expect(list.getByRole('row').filter({ hasText: '#1 ·' }).getByTestId('occurrence-status')).toHaveText('Done');
  await expect(list.getByRole('row').filter({ hasText: '#2 ·' }).getByTestId('occurrence-status')).toHaveText('Scheduled');
});

test('admin changes the cleaning type of a finished visit it added', async ({ page }) => {
  const state = await setup(page);
  state.occurrences = [{ ...adminVisit, startUtc: '2026-11-02T06:00:00.000Z', endUtc: '2026-11-02T08:00:00.000Z', offeredEmployeeId: null, assigneeIds: ['cleaner'], employeeId: 'cleaner' }];
  await shiftAction(page, MAIN, 'Occurrences');
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('button', { name: 'Change cleaning type' }).click();
  const editor = page.getByRole('dialog', { name: /Change|Cleaner|Occurrence/ }).last();
  await editor.getByRole('textbox', { name: 'Cleaning type', exact: true }).click();
  await page.getByRole('option', { name: 'Deep', exact: true }).click();
  await editor.getByRole('button', { name: 'Save cleaning type', exact: true }).click();
  await expect(page.getByRole('dialog').filter({ hasText: 'Save cleaning type' })).toHaveCount(0);
  expect(state.cleaningTypeWrites).toEqual([expect.objectContaining({ cleaningType: 'Deep' })]);
});
