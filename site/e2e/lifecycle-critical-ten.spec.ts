import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Authorization,Content-Type', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS' };

async function setupShifts(page: Page) {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && route.request().method() === 'POST') {
      const body = route.request().postDataJSON(); writes.push(body);
      return route.fulfill({ headers, status: 201, json: { ...body, id: 'created' } });
    }
    const shifts = [
      { id: 'past', name: 'Past one-time visit', isActive: true, companyId: 'company', branchId: 'branch', schedule: { type: 6, oneTimeDate: '2020-01-15', oneTimeStart: '08:00', oneTimeEnd: '09:00' } },
      ...[
        { type: 0, dailyStart: '08:00:00', dailyEnd: '09:00:00' },
        { type: 2, weeklyDays: [1, 3], weeklyStart: '10:00:00', weeklyEnd: '12:00:00' },
        { type: 3, biWeeklyDay: 5, biWeeklyStart: '14:00:00', biWeeklyEnd: '15:00:00' },
        { type: 5, monthlyDay: 15, monthlyStart: '13:00:00', monthlyEnd: '14:00:00' },
      ].map(schedule => ({ id: `schedule-${schedule.type}`, name: `Schedule ${schedule.type}`, isActive: true, companyId: 'company', branchId: 'branch', schedule })),
    ];
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse(shifts, route.request().url())
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Synthetic company', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'Synthetic company', isActive: true }, branches: [{ id: 'branch', name: 'Synthetic branch', isActive: true }] }
      : [] });
  });
  await page.goto('/admin/shifts/');
  return writes;
}

test('schedule is explicit and on-call creation does not invent a daily visit', async ({ page }) => {
  const writes = await setupShifts(page);
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await expect(dialog.getByRole('textbox', { name: /^Schedule/ })).toHaveValue('');
  await dialog.getByLabel('Company', { exact: true }).click();
  await page.getByRole('option', { name: 'Synthetic company', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click();
  await page.getByRole('option', { name: 'Synthetic branch', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('On-call cleaning');
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('Choose a schedule type.')).toBeVisible();
  expect(writes).toHaveLength(0);
  await dialog.getByRole('textbox', { name: /^Schedule/ }).click();
  await page.getByRole('option', { name: 'On-call / flexible', exact: true }).click();
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0].schedule).toEqual({ type: 4 });
  await expect(page.getByRole('dialog', { name: /Occurrences/ })).toBeVisible();
});

test('past unfinished one-time shift asks the admin to check completion', async ({ page }) => {
  await setupShifts(page);
  await expect(page.getByRole('row').filter({ hasText: 'Past one-time visit' }).getByText('Past visit · check completion', { exact: true })).toBeVisible();
});

test('recurring schedules show HH:mm and retain biweekly and monthly times', async ({ page }) => {
  await setupShifts(page);
  for (const label of ['Daily 08:00–09:00', 'Monday, Wednesday 10:00–12:00', 'Bi-weekly Friday 14:00–15:00', 'Monthly day 15 13:00–14:00']) {
    await expect(page.getByRole('cell', { name: label, exact: true })).toBeVisible();
  }
});

for (const area of ['admin', 'manager']) for (const problem of ['network', 'server', 'denied']) {
  test(`${area}: access ${problem} has a safe support reference or explicit denial`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.route('**/api/v1/me', route => route.request().method() === 'OPTIONS' ? route.fulfill({ headers }) : problem === 'network' ? route.abort('failed') : route.fulfill({ headers, status: problem === 'server' ? 503 : 403, json: {} }));
    await page.goto(`/${area}/`);
    if (problem === 'denied') {
      await expect(page.locator('[role="alert"]:not(#__next-route-announcer__)')).toContainText(area === 'admin' ? 'cannot access' : 'Access denied');
      await expect(page.getByText(/Support reference:/)).toHaveCount(0);
    } else {
      await expect(page.locator('[role="alert"]:not(#__next-route-announcer__)')).toContainText(problem === 'network' ? 'ACCESS-NETWORK' : 'ACCESS-HTTP-503');
      await expect(page.getByRole('button', { name: 'Retry verification', exact: true })).toBeVisible();
    }
  });
}

test('lost booking response recovers the same request and private key', async ({ page }) => {
  const writes: Record<string, unknown>[] = [];
  await page.route('**/api/v1/public/*/services?*', route => route.fulfill({ headers, json: [{ id: '11111111-1111-1111-1111-111111111111', name: 'Synthetic cleaning', priceNet: 50 }] }));
  await page.route('**/api/v1/public/*/availability?*', route => route.fulfill({ headers, json: [{ startTime: '12:00', endTime: '13:00' }] }));
  await page.route('**/api/v1/public/*/bookings', route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    writes.push(route.request().postDataJSON());
    if (writes.length === 1) return route.abort('failed');
    if (writes.length === 2) return route.fulfill({ headers, status: 429, json: {} });
    return route.fulfill({ headers, status: 201, json: { bookingId: 'booking', bookingNumber: 'RSS-2030-000001', customerReference: 'ABC123', managementKey: 'B'.repeat(64) } });
  });
  await page.goto('/en/varaus/');
  await page.getByRole('button', { name: /Synthetic cleaning/ }).click();
  await page.locator('#booking-date').fill('2030-01-15');
  await page.getByRole('button', { name: '12:00', exact: true }).click();
  for (const [id, value] of Object.entries({ customerName: 'Synthetic customer', customerPhone: '123', street: 'Street', city: 'Helsinki' })) await page.locator(`#${id}`).fill(value);
  await page.locator('#accepted').check();
  await page.getByRole('button', { name: 'Confirm booking', exact: true }).click();
  await expect(page.locator('#customerName')).toHaveValue('Synthetic customer');
  await expect(page.locator('#customerName')).toBeDisabled();
  await page.getByRole('button', { name: 'Recover booking result', exact: true }).click();
  await expect(page.locator('#customerName')).toBeDisabled();
  await page.getByRole('button', { name: 'Recover booking result', exact: true }).click();
  await expect(page.getByText('RSS-2030-000001', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(3); expect(writes[1]).toEqual(writes[0]); expect(writes[2]).toEqual(writes[0]);
  expect(writes[0].idempotencyKey).toMatch(/^[A-F0-9]{64}$/);
  expect(await page.evaluate(() => JSON.stringify({ ...sessionStorage, ...localStorage }))).not.toContain(writes[0].idempotencyKey);
});
