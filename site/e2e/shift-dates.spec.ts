import { selectDailyScheduleIfEmpty } from './admin-helpers';
import { test, expect } from '@playwright/test';
import { shiftAction } from './shift-actions';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { autoShiftName } from '../src/lib/shiftName';
const DATED_NAME = autoShiftName(undefined, 'Daily');

test('admin waits for valid scheduling settings before defaulting a new shift date', async ({ page }) => {
  await setupAdminMocks(page); let zone = 'Invalid/Zone'; let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
  await page.route(/\/api\/v1\/admin\//, route => route.fulfill({ headers, json: new URL(route.request().url()).pathname.endsWith('/companies') ? { items: [], total: 0 } : isShiftsRoute(new URL(route.request().url()).pathname) ? shiftsResponse([], route.request().url()) : [] }));
  await page.route('**/api/v1/admin/tenant', async route => { await gate; return route.fulfill({ headers, json: { timeZoneId: zone } }); });
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('button', { name: 'Add shift', exact: true })).toBeDisabled();
  release();
  await expect(page.getByText('Could not load scheduling time zone.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add shift', exact: true })).toBeDisabled();
  zone = 'Europe/Helsinki'; await page.reload();
  await expect(page.getByRole('button', { name: 'Add shift', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Add shift', exact: true }).getByRole('textbox', { name: 'Start date', exact: true })).not.toHaveValue('');
});

test('admin requires a start date, supports an open end date, and edits the range', async ({ page }) => {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-10-17T22:00:00Z') });
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
  const writes: any[] = []; let shifts: any[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && method === 'POST' || path.includes('/shifts/') && method === 'PUT') {
      const body = route.request().postDataJSON(); writes.push(body); const saved = { id: method === 'POST' ? 'shift' : path.split('/').at(-1), ...body };
      shifts = [saved]; return route.fulfill({ headers, json: saved });
    }
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse(shifts, route.request().url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue('2026-10-18');
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('');
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(dialog.getByText('Shift start date is required.')).toBeVisible(); expect(writes).toHaveLength(0);
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await dialog.getByLabel('End date (optional)', { exact: true }).fill('2026-10-31');
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(dialog.getByText('End date must be on or after the start date.')).toBeVisible(); expect(writes).toHaveLength(0);
  await dialog.getByLabel('End date (optional)', { exact: true }).fill('');
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('cell', { name: DATED_NAME, exact: true })).toBeVisible();
  expect(writes[0].validFrom).toBe('2026-11-01'); expect(writes[0].validUntil).toBeUndefined();
  await shiftAction(page, DATED_NAME, 'Edit');
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(edit.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue('2026-11-01');
  await edit.getByRole('textbox', { name: 'Start date', exact: true }).fill('');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit.getByText('Shift start date is required.')).toBeVisible();
  expect(writes).toHaveLength(1);
  await edit.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await edit.getByLabel('End date (optional)', { exact: true }).fill('2026-11-30');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0); expect(writes[1].validUntil).toBe('2026-11-30');
  await shiftAction(page, DATED_NAME, 'Edit');
  await expect(edit.getByLabel('End date (optional)', { exact: true })).toHaveValue('2026-11-30');
  await edit.getByLabel('End date (optional)', { exact: true }).fill('');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0);
  expect(writes[2].validFrom).toBe('2026-11-01'); expect(writes[2].validUntil).toBeUndefined();
  shifts = [{ ...shifts[0], id: 'legacy', name: 'Legacy cleaning', validFrom: null, validUntil: null }];
  await page.reload();
  await shiftAction(page, 'Legacy cleaning', 'Edit');
  await expect(edit.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue('');
  await expect(edit.getByRole('textbox', { name: 'Start date', exact: true })).not.toHaveAttribute('required');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0);
  expect(writes[3].validFrom).toBeUndefined(); expect(writes[3].validUntil).toBeUndefined();
});

test('on-call shifts keep scheduling dated visits and remain available for new shifts', async ({ page }) => {
  await setupAdminMocks(page);
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
  const shiftWrites: any[] = [], visitWrites: any[] = [], occurrenceQueries: URLSearchParams[] = [];
  // Existing on-call shifts remain editable and can still receive dated visits; new shifts cannot be on-call.
  let shifts: any[] = [{ id: 'oncall', companyId: 'company', branchId: 'branch', name: 'One-time cleaning', isActive: true, notes: null, qualityCycleTemplateId: null, validFrom: '2026-11-01T00:00:00', validUntil: null, schedule: { type: 4 } }];
  let visits: any[] = [], delayNextLoad = false;
  let releaseOldLoad!: () => void;
  const oldLoad = new Promise<void>(resolve => { releaseOldLoad = resolve; });
  let releaseSave!: () => void;
  const pendingSave = new Promise<void>(resolve => { releaseSave = resolve; });
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && request.method() === 'POST') {
      const body = request.postDataJSON(); shiftWrites.push(body); shifts = [{ id: 'oncall', ...body }];
      return route.fulfill({ headers, json: shifts[0] });
    }
    if (path.endsWith('/occurrence-exceptions')) {
      const body = request.postDataJSON(); visitWrites.push(body);
      visits = [{ ...body, exceptionId: 'visit', assigneeIds: body.employeeId ? [body.employeeId] : [] }];
      await pendingSave;
      return route.fulfill({ headers, json: visits[0] });
    }
    if (path.endsWith('/occurrences')) {
      occurrenceQueries.push(new URL(request.url()).searchParams);
      if (delayNextLoad) {
        delayNextLoad = false;
        await oldLoad;
        return route.fulfill({ headers, json: [] });
      }
      return route.fulfill({ headers, json: visits });
    }
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse(shifts, route.request().url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'Property', isActive: true }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/employees') ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true }]
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const create = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await create.getByLabel('Schedule', { exact: true }).click();
  await expect(page.getByRole('option', { name: 'One-time', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'On-call / flexible', exact: true })).toBeVisible();
  await page.getByRole('option', { name: 'On-call / flexible', exact: true }).click();
  await expect(create.getByLabel('Schedule', { exact: true })).toHaveValue('On-call / flexible');
  await create.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(shiftWrites).toHaveLength(0);
  await expect(page.getByRole('cell', { name: 'One-time cleaning', exact: true })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'One-time cleaning' })).toContainText('On-call');
  delayNextLoad = true;
  await shiftAction(page, 'One-time cleaning', 'Schedule one-time visit');
  await expect.poll(() => occurrenceQueries.length).toBe(1);
  const editor = page.getByRole('dialog', { name: 'Change occurrence', exact: true });
  await expect(editor).toBeVisible();
  await expect(editor.getByRole('textbox', { name: /^Start \(Europe\/Helsinki\)/ })).toHaveValue('');
  expect(visitWrites).toHaveLength(0);
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await editor.getByRole('textbox', { name: /^Start \(Europe\/Helsinki\)/ }).fill('2030-01-10T09:00');
  await editor.getByRole('textbox', { name: /^End \(Europe\/Helsinki\)/ }).fill('2030-01-10T11:00');
  await editor.getByLabel('Replacement employee (optional)', { exact: true }).click();
  await page.getByRole('option', { name: 'Test Cleaner', exact: true }).click();
  await editor.getByRole('textbox', { name: /^Reason/ }).fill('Agreed one-time cleaning');
  await editor.getByRole('button', { name: 'Save occurrence', exact: true }).click();
  await expect.poll(() => visitWrites.length).toBe(1);
  await expect(editor.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled();
  await expect(editor.locator('.mantine-Modal-close')).toBeDisabled();
  await expect(page.getByRole('dialog', { name: /Occurrences/, includeHidden: true }).locator('.mantine-Modal-close')).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(editor).toBeVisible();
  releaseSave();
  await expect(editor).toBeHidden();
  expect(visitWrites).toEqual([{ originalStartUtc: null, startUtc: '2030-01-10T07:00:00.000Z', endUtc: '2030-01-10T09:00:00.000Z', status: 4, reason: 'Agreed one-time cleaning', employeeId: 'cleaner' }]);
  await expect(list.getByText('One-time visit scheduled.', { exact: false })).toBeVisible();
  await expect(list.getByRole('cell', { name: 'Test Cleaner', exact: false })).toBeVisible();
  await expect(list.getByLabel('From', { exact: true })).toHaveValue('2030-01-10');
  expect(occurrenceQueries.at(-1)?.get('from')).toBe('2030-01-09T22:00:00.000Z');
  const staleResponse = page.waitForResponse(response => response.url().includes('/occurrences?') && !response.url().includes('2030-01-09'));
  releaseOldLoad(); await staleResponse;
  await page.waitForLoadState('networkidle');
  await expect(list.getByRole('cell', { name: 'Test Cleaner', exact: false })).toBeVisible();
  await expect(list.getByText('No occurrences in this period.')).toHaveCount(0);
  expect(visitWrites).toHaveLength(1);
});
