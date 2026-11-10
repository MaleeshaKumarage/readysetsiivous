import { test, expect, type Page } from '@playwright/test';
import { selectDailyScheduleIfEmpty, setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const fullProperty = { id: 'branch', companyId: 'company', name: 'AAA1', street: 'Kauppakatu 1', postalCode: '55100', city: 'Imatra', sizeM2: 34, cleaningType: 'Deep', roomCount: 2, bedCount: 3, isActive: true };
// A property without size or cleaning type cannot take a shift: the server says why.
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

test('Add shift has no Name field and names the shift from the property in the decided format', async ({ page }) => {
  const name = await createShift(page, fullProperty);
  expect(name).toBe('Deep Cleaning - Kauppakatu 1, 55100 Imatra |34m2|2H|3V');
  await expect(page.getByRole('cell', { name: 'Deep Cleaning - Kauppakatu 1, 55100 Imatra |34m2|2H|3V', exact: true })).toBeVisible();
});

test('a property without size or cleaning type is refused, and the dialog says why', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && method === 'POST') return route.fulfill({ status: 400, headers, json: { title: 'Validation failed', detail: 'Add the property\u2019s size and cleaning type (Deep or Normal) before scheduling a shift for it.' } });
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse([], route.request().url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [bareProperty] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('Add the property\u2019s size and cleaning type (Deep or Normal) before scheduling a shift for it.')).toBeVisible();
});
