import { test, expect, Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const shiftId = '33333333-3333-4333-8333-333333333333';
const formId = '44444444-4444-4444-8444-444444444444';

// Occurrences as the server materializes them from a one-time shift: one visit per local date.
const pastVisit = {
  shiftId, shiftName: 'Past visit', startUtc: '2026-10-01T06:00:00.000Z', endUtc: '2026-10-01T08:30:00.000Z',
  timeZoneId: 'Europe/Helsinki', form: null,
};
const futureVisit = {
  shiftId, shiftName: 'Future visit', startUtc: '2026-11-10T06:00:00.000Z', endUtc: '2026-11-10T08:30:00.000Z',
  timeZoneId: 'Europe/Helsinki', form: null,
};

async function setupEmployee(page: Page, visits: any[]) {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-11-03T10:00:00Z') });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })],
      employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/shifts(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ headers, json: [] });
  });
  const counts = { starts: 0, ends: 0 };
  await page.route(/\/api\/v1\/me\/shifts\/calendar\?/, route => {
    const view = new URL(route.request().url()).searchParams.get('view');
    const occurrences = visits.filter(v => (view === 'past' && v.startUtc < '2026-11-03T00:00') || (view === 'today' && v.startUtc.startsWith('2026-11-03')) || (view === 'future' && v.startUtc >= '2026-11-04'));
    return route.fulfill({ headers, json: { occurrences, timeZoneId: 'Europe/Helsinki',
      fromLocalDate: view === 'past' ? '2026-09-04' : '2026-11-04', toLocalDate: view === 'past' ? '2026-11-02' : '2026-12-03' } });
  });
  await page.route(new RegExp(`/api/v1/me/shifts/${shiftId}/start$`), async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    counts.starts++;
    visits[0].form = { id: formId, shiftId, employeeId: 'employee-a', templateId: '00000000-0000-0000-0000-000000000000', templateTitle: 'Daily activities',
      items: [], photoUrls: [], cleanerNotes: '', isSubmitted: false, startedAtUtc: await page.evaluate(() => new Date().toISOString()), submittedUtc: null };
    return route.fulfill({ headers, json: visits[0].form });
  });
  await page.route(new RegExp(`/api/v1/me/quality-cycle/${formId}/end$`), async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    counts.ends++;
    const endedAtUtc = await page.evaluate(() => new Date().toISOString());
    const form = { ...visits[0].form, ...route.request().postDataJSON(), isSubmitted: true, submittedUtc: endedAtUtc, endedAtUtc };
    visits[0].form = form;
    return route.fulfill({ headers, json: form });
  });
  return counts;
}

test('an assigned past one-time visit appears under Past and can be started and ended', async ({ page }) => {
  const visits = [{ ...pastVisit }];
  const counts = await setupEmployee(page, visits);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  await page.getByRole('button', { name: /01\/10\/2026 Thursday.*1 shift/ }).click();
  const card = page.getByRole('group', { name: /Past visit/ });
  await expect(card).toBeVisible();
  await expect(card.getByText(/Scheduled:.*Oct 1, 2026/)).toBeVisible();
  await card.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(card.getByText(/^Actual start: /)).toBeVisible();
  await card.getByRole('button', { name: 'Record activities & end' }).click();
  await card.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(card.getByText('Done', { exact: true })).toBeVisible();
  expect(counts).toEqual({ starts: 1, ends: 1 });
});

test('a future one-time visit is listed under Future but cannot be started early', async ({ page }) => {
  const counts = await setupEmployee(page, [{ ...futureVisit }]);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Future', exact: true }).click();
  await page.getByRole('button', { name: /10\/11\/2026 Tuesday.*1 shift/ }).click();
  const card = page.getByRole('group', { name: /Future visit/ });
  await expect(card).toBeVisible();
  await expect(card.getByRole('button', { name: 'Start', exact: true })).toBeDisabled();
  await expect(card.getByText('Available on the scheduled date. The actual start time will be recorded.')).toBeVisible();
  expect(counts.starts).toBe(0);
});

test('a one-time visit on today is startable and can be ended on the same day', async ({ page }) => {
  // 2026-11-03 10:00 UTC is 12:00 Helsinki; the visit started at 10:00 local, so it is startable now.
  const visits = [{ shiftId, shiftName: 'Today visit', startUtc: '2026-11-03T08:00:00.000Z', endUtc: '2026-11-03T10:00:00.000Z', timeZoneId: 'Europe/Helsinki', form: null }];
  const counts = await setupEmployee(page, visits);
  await page.goto('/me/shifts/');
  const card = page.getByRole('group', { name: /Today visit/ });
  await expect(card).toBeVisible();
  await expect(card.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
  await card.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(card.getByText(/^Actual start: /)).toBeVisible();
  await card.getByRole('button', { name: 'Record activities & end' }).click();
  await card.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(card.getByText('Done', { exact: true })).toBeVisible();
  expect(counts).toEqual({ starts: 1, ends: 1 });
});
