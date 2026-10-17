import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

test('admin requires a start date, supports an open end date, and edits the range', async ({ page }) => {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-10-17T22:00:00Z') });
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
  const writes: any[] = []; let shifts: any[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && method === 'POST' || path.includes('/shifts/') && method === 'PUT') {
      const body = route.request().postDataJSON(); writes.push(body); const saved = { id: 'shift', ...body };
      shifts = [saved]; return route.fulfill({ headers, json: saved });
    }
    return route.fulfill({ headers, json: path.endsWith('/shifts') ? shifts
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift' }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue('2026-10-18');
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(dialog.getByText('Shift start date is required.')).toBeVisible(); expect(writes).toHaveLength(0);
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Dated cleaning');
  await dialog.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await dialog.getByLabel('End date (optional)', { exact: true }).fill('2026-10-31');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(dialog.getByText('End date must be on or after the start date.')).toBeVisible(); expect(writes).toHaveLength(0);
  await dialog.getByLabel('End date (optional)', { exact: true }).fill('');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('cell', { name: 'Dated cleaning', exact: true })).toBeVisible();
  expect(writes[0].validFrom).toBe('2026-11-01'); expect(writes[0].validUntil).toBeUndefined();
  await page.getByRole('row').filter({ hasText: 'Dated cleaning' }).getByRole('button', { name: 'Edit', exact: true }).click();
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(edit.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue('2026-11-01');
  await edit.getByLabel('End date (optional)', { exact: true }).fill('2026-11-30');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0); expect(writes[1].validUntil).toBe('2026-11-30');
});
