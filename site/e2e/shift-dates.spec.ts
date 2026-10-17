import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

test('admin waits for valid scheduling settings before defaulting a new shift date', async ({ page }) => {
  await setupAdminMocks(page); let zone = 'Invalid/Zone'; let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
  await page.route(/\/api\/v1\/admin\//, route => route.fulfill({ headers, json: new URL(route.request().url()).pathname.endsWith('/companies') ? { items: [], total: 0 } : [] }));
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
  await edit.getByRole('textbox', { name: 'Start date', exact: true }).fill('');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit.getByText('Shift start date is required.')).toBeVisible();
  expect(writes).toHaveLength(1);
  await edit.getByRole('textbox', { name: 'Start date', exact: true }).fill('2026-11-01');
  await edit.getByLabel('End date (optional)', { exact: true }).fill('2026-11-30');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0); expect(writes[1].validUntil).toBe('2026-11-30');
  await page.getByRole('row').filter({ hasText: 'Dated cleaning' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(edit.getByLabel('End date (optional)', { exact: true })).toHaveValue('2026-11-30');
  await edit.getByLabel('End date (optional)', { exact: true }).fill('');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0);
  expect(writes[2].validFrom).toBe('2026-11-01'); expect(writes[2].validUntil).toBeUndefined();
  shifts = [{ ...shifts[0], id: 'legacy', name: 'Legacy cleaning', validFrom: null, validUntil: null }];
  await page.reload();
  await page.getByRole('row').filter({ hasText: 'Legacy cleaning' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(edit.getByRole('textbox', { name: 'Start date', exact: true })).toHaveValue('');
  await expect(edit.getByRole('textbox', { name: 'Start date', exact: true })).not.toHaveAttribute('required');
  await edit.getByRole('textbox', { name: 'Name', exact: true }).fill('Legacy cleaning updated');
  await edit.getByRole('button', { name: 'Save changes' }).click();
  await expect(edit).toHaveCount(0);
  expect(writes[3].validFrom).toBeUndefined(); expect(writes[3].validUntil).toBeUndefined();
});
