import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

// A "Varied times by weekday" shift: each chosen weekday uses its own times. A weekday whose times were never
// edited shows the default 08:00-17:00 on screen, and that default must be what is saved and validated.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

async function setup(page: Page) {
  await setupAdminMocks(page);
  const created: any[] = [];
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts') && method === 'POST') { created.push(request.postDataJSON()); return route.fulfill({ status: 200, headers, json: { id: 'new-shift', name: 'Shift', companyId: 'company', branchId: 'branch', isActive: true, schedule: request.postDataJSON().schedule } }); }
    const json = isShiftsRoute(path) ? shiftsResponse([], request.url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'Acme Hotels', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'Helsinki Office', isActive: true, sizeM2: 45 }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Acme Hotels', isActive: true }], total: 1 }
      : path.endsWith('/employees') ? [] : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('button', { name: 'Add shift', exact: true })).toBeEnabled();
  return created;
}

test('a varied-times shift saves the default times of each chosen weekday', async ({ page }) => {
  const created = await setup(page);
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company').click();
  await page.getByRole('option', { name: 'Acme Hotels', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Branch', exact: true }).click();
  await page.getByRole('option', { name: 'Helsinki Office', exact: true }).click();
  await dialog.getByLabel(/^Start date/).fill('2026-11-16');
  await dialog.getByRole('textbox', { name: 'Schedule', exact: true }).click();
  await page.getByRole('option', { name: 'Varied times by weekday', exact: true }).click();
  await dialog.getByLabel('Days', { exact: true }).click({ force: true });
  await page.getByRole('option', { name: 'Tuesday', exact: true }).click();
  await page.getByRole('option', { name: 'Thursday', exact: true }).click();
  await page.keyboard.press('Escape');
  // The times are left at their shown defaults.
  await expect(dialog.getByLabel('Start', { exact: true }).first()).toHaveValue('08:00');
  await expect(dialog.getByLabel('End', { exact: true }).first()).toHaveValue('17:00');
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(created).toHaveLength(1);
  expect(created[0].schedule).toMatchObject({ type: 1, dailyTimes: { Tuesday: { start: '08:00', end: '17:00' }, Thursday: { start: '08:00', end: '17:00' } } });
});

test('a varied-times shift is refused when a chosen weekday has an end before its start', async ({ page }) => {
  const created = await setup(page);
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company').click();
  await page.getByRole('option', { name: 'Acme Hotels', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Branch', exact: true }).click();
  await page.getByRole('option', { name: 'Helsinki Office', exact: true }).click();
  await dialog.getByLabel(/^Start date/).fill('2026-11-16');
  await dialog.getByRole('textbox', { name: 'Schedule', exact: true }).click();
  await page.getByRole('option', { name: 'Varied times by weekday', exact: true }).click();
  await dialog.getByLabel('Days', { exact: true }).click({ force: true });
  await page.getByRole('option', { name: 'Tuesday', exact: true }).click();
  await page.keyboard.press('Escape');
  await dialog.getByLabel('End', { exact: true }).first().fill('07:00');
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();

  await expect(dialog.getByText('ensure each end time is after its start time', { exact: false })).toBeVisible();
  expect(created).toHaveLength(0);
});
