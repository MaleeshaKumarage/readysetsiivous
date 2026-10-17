import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

async function setup(page: Page) {
  await setupAdminMocks(page);
  const original = '2026-11-02T06:00:00.000Z';
  let occurrences: any[] = [{ startUtc: original, endUtc: '2026-11-02T08:00:00.000Z', assigneeIds: [], rosterVersion: 'v1' }];
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
      : path.endsWith('/employees') ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', isActive: true }, { id: 'second', firstName: 'Second', lastName: 'Cleaner', isActive: true }]
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

test('admin records a company-cancelled scheduled occurrence', async ({ page }) => {
  const state = await setup(page);
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  const list = page.getByRole('dialog', { name: /Occurrences/ });
  await list.getByRole('button', { name: 'Cancel' }).click();
  const editor = page.getByRole('dialog', { name: 'Change occurrence' });
  await editor.getByLabel('Action').click();
  await page.getByRole('option', { name: 'Company cancelled' }).click();
  await editor.getByLabel('Reason').fill('Cleaner unavailable');
  await editor.getByRole('button', { name: 'Save occurrence' }).click();

  expect(state.writes[0]).toMatchObject({
    originalStartUtc: '2026-11-02T06:00:00.000Z',
    status: 3,
  });
  await expect(list.getByText('Company cancelled')).toBeVisible();
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

for (const mode of ['Replace regular assignees', 'Add extra employees', 'Restore recurring assignment']) {
  test(`visit assignee preview and save: ${mode}`, async ({ page }) => {
    await setup(page); const calls: any[] = []; let fail = true;
    await page.route(/\/visit-assignees$/, route => {
      const body = route.request().postDataJSON(); calls.push(body);
      if (fail) { fail = false; return route.fulfill({ status: 409, headers, json: { detail: 'Overlapping visit. Retry after reviewing assignments.' } }); }
      return route.fulfill({ status: 200, headers, json: { employeeIds: body.employeeIds, preview: body.preview, message: 'Other visits unchanged.' } });
    });
    await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
    const list = page.getByRole('dialog', { name: /Occurrences/ });
    await expect(list.getByRole('columnheader', { name: 'Assignees' })).toBeVisible();
    await list.getByRole('button', { name: 'Assign', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Assign visit', exact: true });
    await dialog.getByLabel('Assignment action').click(); await page.getByRole('option', { name: mode, exact: true }).click();
    if (!mode.startsWith('Restore')) {
      await dialog.getByLabel('Employees').click(); await page.getByRole('option', { name: 'Test Cleaner', exact: true }).click(); await page.keyboard.press('Escape');
    }
    await dialog.getByLabel('Assignment reason').fill('Planned coverage');
    await expect(dialog.getByRole('button', { name: 'Save assignment' })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Preview assignment' }).click();
    await expect(dialog.getByRole('alert')).toHaveText('Overlapping visit. Retry after reviewing assignments.');
    await expect(dialog.getByLabel('Assignment reason')).toHaveValue('Planned coverage');
    await dialog.getByRole('button', { name: 'Preview assignment' }).click();
    await expect(dialog.getByText('Assignment preview')).toBeVisible();
    await dialog.getByRole('button', { name: 'Save assignment' }).click();
    await expect(dialog).toHaveCount(0);
    expect(calls.at(-1)).toMatchObject({ mode: mode.startsWith('Replace') ? 1 : mode.startsWith('Add') ? 2 : 0,
      preview: false, fromThisDate: false, rosterVersion: 'v1', originalStartUtc: '2026-11-02T06:00:00.000Z' });
  });
}

test('handover previews and saves from the chosen date', async ({ page }) => {
  await setup(page); const calls: any[] = [];
  await page.route(/\/visit-assignees$/, route => { const body = route.request().postDataJSON(); calls.push(body); return route.fulfill({ headers, json: { employeeIds: body.employeeIds, preview: body.preview, message: 'One-off overrides remain.' } }); });
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  await page.getByRole('dialog', { name: /Occurrences/ }).getByRole('button', { name: 'Assign', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Assign visit', exact: true });
  await dialog.getByLabel('Apply to').click(); await page.getByRole('option', { name: 'From this date onward' }).click();
  await dialog.getByLabel('Employees').click(); await page.getByRole('option', { name: 'Test Cleaner', exact: true }).click(); await page.keyboard.press('Escape');
  await dialog.getByLabel('Assignment reason').fill('Permanent handover');
  await dialog.getByRole('button', { name: 'Preview assignment' }).click();
  await expect(dialog.getByText(/Replace also removes later recurring handovers/)).toBeVisible();
  await dialog.getByRole('button', { name: 'Save assignment' }).click();
  expect(calls.at(-1)).toMatchObject({ fromThisDate: true, preview: false, originalStartUtc: '2026-11-02T06:00:00.000Z' });
});

test('team assignment includes both selected employees in preview and request', async ({ page }) => {
  await setup(page); const calls: any[] = [];
  await page.route(/\/visit-assignees$/, route => { const body = route.request().postDataJSON(); calls.push(body); return route.fulfill({ headers, json: { employeeIds: body.employeeIds, preview: body.preview, message: 'Team cover.' } }); });
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  await page.getByRole('dialog', { name: /Occurrences/ }).getByRole('button', { name: 'Assign', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Assign visit', exact: true });
  await dialog.getByLabel('Employees').click(); await page.getByRole('option', { name: 'Test Cleaner', exact: true }).click();
  await dialog.getByLabel('Employees').click(); await page.getByRole('option', { name: 'Second Cleaner', exact: true }).click(); await page.keyboard.press('Escape');
  await dialog.getByLabel('Assignment reason').fill('Two cleaner team'); await dialog.getByRole('button', { name: 'Preview assignment' }).click();
  await expect(dialog.getByText(/Selected visit:.*Test Cleaner.*Second Cleaner/)).toBeVisible();
  await dialog.getByRole('button', { name: 'Save assignment' }).click();
  expect(calls.at(-1).employeeIds).toEqual(['cleaner', 'second']);
});

test('assignee cells explain named, unassigned and locked work; Cancel sends no assignment', async ({ page }) => {
  await setup(page); let writes = 0;
  await page.route(/\/visit-assignees$/, route => { writes++; return route.fulfill({ status: 500, headers }); });
  await page.route(/\/occurrences\?/, route => route.fulfill({ headers, json: [
    { startUtc: '2026-11-02T06:00:00Z', endUtc: '2026-11-02T08:00:00Z', assigneeIds: ['cleaner'], assignmentSource: 'Replacement', canAssign: true },
    { startUtc: '2026-11-03T06:00:00Z', endUtc: '2026-11-03T08:00:00Z', assigneeIds: [], assignmentSource: 'Cancelled', status: 2, canAssign: false },
    { startUtc: '2026-11-04T06:00:00Z', endUtc: '2026-11-04T08:00:00Z', assigneeIds: ['cleaner'], assignmentSource: 'Recurring', canAssign: false, assignmentLockReason: 'Started work cannot be reassigned.' },
  ] }));
  await page.getByRole('button', { name: 'Occurrences', exact: true }).click();
  const list = page.getByRole('dialog', { name: /Occurrences/ }); const rows = list.getByRole('row');
  await expect(rows.nth(1).getByText('Test Cleaner', { exact: true })).toBeVisible();
  await expect(rows.nth(1).getByText('Replacement', { exact: true })).toBeVisible();
  await expect(rows.nth(2).getByText('Unassigned', { exact: true })).toBeVisible();
  await expect(rows.nth(2).getByRole('button', { name: 'Assign', exact: true })).toBeDisabled();
  await expect(rows.nth(3).getByRole('button', { name: 'Assign', exact: true })).toBeDisabled();
  await expect(rows.nth(3).getByText('Started work cannot be reassigned.')).toBeVisible();
  await rows.nth(1).getByRole('button', { name: 'Assign', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Assign visit', exact: true }); await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0); expect(writes).toBe(0);
});
