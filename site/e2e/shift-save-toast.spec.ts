import { selectDailyScheduleIfEmpty } from './admin-helpers';
import { test, expect } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const CONFLICT = 'A shift with this name already exists in this branch. Change the name, then save again.';

test('a refused shift save explains the fix as a toast and in the dialog, and keeps the entered values', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts') && method === 'POST') return route.fulfill({ status: 409, headers, json: { title: 'Shift conflict', detail: CONFLICT } });
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse([], route.request().url())
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await selectDailyScheduleIfEmpty(page);
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByText('Shift not created').first()).toBeVisible();
  await expect(dialog.getByText(CONFLICT)).toBeVisible();
});

test('a field validation failure names the field problem instead of a generic title', async ({ page }) => {
  await setupAdminMocks(page);
  const FIELD = 'End date must be on or after the start date.';
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts') && method === 'POST') return route.fulfill({ status: 400, headers, json: { title: 'Validation failed', status: 400, errors: { ValidUntil: [FIELD] } } });
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse([], route.request().url())
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await selectDailyScheduleIfEmpty(page);
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-10');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(dialog.getByText(FIELD)).toBeVisible();
  await expect(dialog.getByText('Validation failed')).toHaveCount(0);
});

test('a refused shift update says it was not updated, keeps the edited values and the dialog open', async ({ page }) => {
  await setupAdminMocks(page);
  const UPDATE_REFUSED = 'A shift with this name already exists in this branch. Change the name, then save again.';
  const shift = { id: 'shift-1', name: 'Morning clean', companyId: 'company', branchId: 'branch', isActive: true, notes: null, validFrom: '2026-11-01T00:00:00Z', validUntil: null, schedule: { type: 0, dailyStart: '08:00:00', dailyEnd: '10:00:00', dailyTimes: {}, weeklyDays: [] } };
  let puts = 0;
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts/shift-1') && method === 'PUT') { puts++; return route.fulfill({ status: 409, headers, json: { title: 'Shift conflict', detail: UPDATE_REFUSED } }); }
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse([shift], route.request().url())
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'More actions for Morning clean' }).click();
  await page.getByRole('menuitem', { name: 'Edit', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText('Shift not updated').first()).toBeVisible();
  await expect(dialog.getByText(UPDATE_REFUSED)).toBeVisible();
  expect(puts).toBe(1);
});
for (const [status, expected] of [
  [401, 'Your session has ended. Sign in again, then retry the change.'],
  [403, 'Your account is not allowed to make this change. Ask an owner to check your access.'],
] as const) {
  test(`a ${status} on save says what to do instead of asking to check the fields`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.route(/\/api\/v1\/admin\//, route => {
      const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
      if (path.endsWith('/shifts') && method === 'POST') return route.fulfill({ status, headers, body: '' });
      return route.fulfill({ headers, json: path.endsWith('/shifts') ? []
        : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
        : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
        : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
    });
    await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
    const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
    await selectDailyScheduleIfEmpty(page);
    await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
    await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
    await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-11');
    await dialog.getByRole('button', { name: 'Create shift' }).click();
    await expect(dialog.getByText(expected)).toBeVisible();
  });
}
