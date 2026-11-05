import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const CONFLICT = 'A shift with this name already exists in this branch. Change the name, then save again.';

test('a refused shift save explains the fix as a toast and in the dialog, and keeps the entered values', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts') && method === 'POST') return route.fulfill({ status: 409, headers, json: { title: 'Shift conflict', detail: CONFLICT } });
    return route.fulfill({ headers, json: path.endsWith('/shifts') ? []
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Morning clean');
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByText('Shift not created').first()).toBeVisible();
  await expect(dialog.getByText(CONFLICT)).toBeVisible();
  await expect(dialog.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Morning clean');
});

test('a field validation failure names the field problem instead of a generic title', async ({ page }) => {
  await setupAdminMocks(page);
  const FIELD = 'End date must be on or after the start date.';
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts') && method === 'POST') return route.fulfill({ status: 400, headers, json: { title: 'Validation failed', status: 400, errors: { ValidUntil: [FIELD] } } });
    return route.fulfill({ headers, json: path.endsWith('/shifts') ? []
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Ranged clean');
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-10');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(dialog.getByText(FIELD)).toBeVisible();
  await expect(dialog.getByText('Validation failed')).toHaveCount(0);
});
