import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

async function setup(page: Page) {
  await setupAdminMocks(page);
  const original = '2026-11-02T06:00:00.000Z';
  let occurrences: any[] = [{ startUtc: original, endUtc: '2026-11-02T08:00:00.000Z' }];
  const writes: any[] = [];
  const occurrenceQueries: URLSearchParams[] = [];
  let deletes = 0;
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.includes('/occurrence-exceptions')) {
      if (request.method() === 'DELETE') { deletes += 1; occurrences = [{ startUtc: original, endUtc: '2026-11-02T08:00:00.000Z' }]; return route.fulfill({ status: 204, headers }); }
      const body = request.postDataJSON(); writes.push(body);
      const saved = { ...body, exceptionId: 'exception', originalStartUtc: body.originalStartUtc ?? null };
      occurrences = [saved]; return route.fulfill({ status: 200, headers, json: saved });
    }
    if (path.endsWith('/occurrences')) {
      occurrenceQueries.push(new URL(request.url()).searchParams);
      return route.fulfill({ status: 200, headers, json: occurrences });
    }
    const json = path.endsWith('/shifts') ? [{ id: 'shift', name: 'Morning clean', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } }]
      : path.endsWith('/employees') ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', isActive: true }]
      : path.endsWith('/companies') ? { items: [], total: 0 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/');
  return { writes, occurrenceQueries, get deletes() { return deletes; } };
}

test('admin moves one occurrence in tenant local time and can restore it', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await expect(list.getByText('11/2/2026, 8:00:00 AM')).toBeVisible();
  await list.getByRole('button', { name: 'Change' }).click();
  const editor = page.getByRole('dialog', { name: 'Change occurrence' });
  await editor.getByLabel(/Start/).fill('2026-11-02T09:00');
  await editor.getByLabel(/End/).fill('2026-11-02T11:00');
  await editor.getByLabel('Reason').fill('Customer requested a later visit');
  await editor.getByRole('button', { name: 'Save occurrence' }).click();
  await expect(list.getByText('Move / change time')).toBeVisible();
  expect(state.writes[0]).toMatchObject({ originalStartUtc: '2026-11-02T06:00:00.000Z', startUtc: '2026-11-02T07:00:00.000Z', endUtc: '2026-11-02T09:00:00.000Z', status: 1 });
  await list.getByRole('button', { name: 'Restore' }).click();
  await expect(list.getByText('Scheduled')).toBeVisible();
});

test('admin cancellation requires a reason', async ({ page }) => {
  await setup(page); await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  await page.getByRole('dialog', { name: /Occurrences/ }).getByRole('button', { name: 'Cancel' }).click();
  await page.getByRole('dialog', { name: 'Change occurrence' }).getByRole('button', { name: 'Save occurrence' }).click();
  await expect(page.getByRole('dialog', { name: 'Change occurrence' }).getByRole('alert')).toHaveText('Start, end and reason are required.');
});

test('admin adds an extra visit with a replacement employee and deletes it', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('button', { name: 'Add extra visit' }).click();
  const editor = page.getByRole('dialog', { name: 'Change occurrence' });
  await editor.getByLabel(/Start/).fill('2026-11-03T09:00');
  await editor.getByLabel(/End/).fill('2026-11-03T11:00');
  await editor.getByLabel('Replacement employee (optional)').click();
  await page.getByRole('option', { name: 'Test Cleaner' }).click();
  await editor.getByLabel('Reason').fill('Requested deep clean');
  await editor.getByRole('button', { name: 'Save occurrence' }).click();
  expect(state.writes[0]).toMatchObject({
    originalStartUtc: null,
    startUtc: '2026-11-03T07:00:00.000Z',
    endUtc: '2026-11-03T09:00:00.000Z',
    status: 4,
    employeeId: 'cleaner',
  });
  await list.getByRole('button', { name: 'Delete extra' }).click();
  expect(state.deletes).toBe(1);
});

test('admin reloads occurrences for a selected date range', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByLabel('From').fill('2026-12-01');
  await list.getByLabel('To').fill('2026-12-31');
  await list.getByRole('button', { name: 'Load' }).click();
  await expect.poll(() => state.occurrenceQueries.length).toBe(2);
  expect(state.occurrenceQueries.at(-1)?.get('from')).toBe('2026-11-30T22:00:00.000Z');
  expect(state.occurrenceQueries.at(-1)?.get('to')).toBe('2026-12-31T21:59:59.999Z');
});

test('admin cancels occurrence editing without saving', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('button', { name: 'Change' }).click();
  const editor = page.getByRole('dialog', { name: 'Change occurrence' });
  await editor.getByLabel('Reason').fill('Do not save this');
  await editor.getByRole('button', { name: 'Cancel' }).click();
  await expect(editor).toBeHidden();
  expect(state.writes).toHaveLength(0);
});
