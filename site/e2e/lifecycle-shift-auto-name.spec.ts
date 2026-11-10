import { test, expect, type Page } from '@playwright/test';
import { selectDailyScheduleIfEmpty, setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const fullProperty = { id: 'branch', companyId: 'company', name: 'AAA1', street: 'Kauppakatu 1', city: 'Imatra', areaSquareMeters: 45, roomCount: 2, bedCount: 3, isActive: true };
// A property with no address and no area: the name must not start with a separator.
const bareProperty = { id: 'branch', companyId: 'company', name: 'AAA1', roomCount: 2, bedCount: 3, isActive: true };

async function createShift(page: Page, property: Record<string, unknown>) {
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
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [property] }
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
  return writes[0].name;
}

test('Add shift has no Name field and names the shift from a full property and its schedule', async ({ page }) => {
  const name = await createShift(page, fullProperty);
  expect(name).toBe('Kauppakatu 1, Imatra | 45m2 | 2 H | 3 V | - Daily');
  await expect(page.getByRole('cell', { name: 'Kauppakatu 1, Imatra | 45m2 | 2 H | 3 V | - Daily', exact: true })).toBeVisible();
});

test('a property with no address or area still gets a clean shift name without a leading separator', async ({ page }) => {
  const name = await createShift(page, bareProperty);
  expect(name).toBe('2 H | 3 V | - Daily');
});
