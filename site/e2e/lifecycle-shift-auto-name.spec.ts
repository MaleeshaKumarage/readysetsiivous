import { test, expect } from '@playwright/test';
import { selectDailyScheduleIfEmpty, setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { autoShiftName } from '../src/lib/shiftName';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const branch = { id: 'branch', companyId: 'company', name: 'AAA1', street: 'Kauppakatu 1', city: 'Imatra', roomCount: 2, bedCount: 3, isActive: true };
const EXPECTED = 'Kauppakatu 1, Imatra | 2 H | 3 V | - Daily';

test('Add shift has no Name field and creates the shift with a name built from the property and schedule', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = []; let shifts: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && method === 'POST') {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      const saved = { id: 'shift', ...body }; shifts = [saved]; return route.fulfill({ headers, json: saved });
    }
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse(shifts, route.request().url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [branch] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });

  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Name', exact: true })).toHaveCount(0);
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0].name).toBe(EXPECTED);
  expect(writes[0].name).toBe(autoShiftName(branch, 'Daily'));
  await expect(page.getByRole('cell', { name: EXPECTED, exact: true })).toBeVisible();
});
