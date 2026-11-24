import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Covers every From / To / Clear combination on the employee Schedules Past view, on both
// chromium-desktop and mobile-touch (the config runs each test in both projects).

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

const pastOccurrences = [
  { shiftId: 'riverside-20', shiftName: 'Riverside clean', startUtc: '2026-11-20T08:00:00Z', endUtc: '2026-11-20T12:00:00Z', timeZoneId: 'Europe/Helsinki', form: null },
  { shiftId: 'oakfield-18', shiftName: 'Oakfield clean', startUtc: '2026-11-18T13:00:00Z', endUtc: '2026-11-18T17:00:00Z', timeZoneId: 'Europe/Helsinki', form: null },
];

type CalendarMode = 'populated' | 'empty' | 'error';

async function setup(page: Page, mode: CalendarMode = 'populated') {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  const calendarRequests: URL[] = [];
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => {
    const url = new URL(route.request().url());
    calendarRequests.push(url);
    if (mode === 'error') return route.fulfill({ status: 500, headers, json: { error: 'boom' } });
    const from = url.searchParams.get('from');
    const to = url.searchParams.get('to');
    const occurrences = mode === 'empty' ? [] : pastOccurrences.filter(o => (!from || o.startUtc.slice(0, 10) >= from) && (!to || o.startUtc.slice(0, 10) <= to));
    return route.fulfill({ headers, json: { occurrences, timeZoneId: 'Europe/Helsinki', fromLocalDate: from ?? '2026-11-01', toLocalDate: to ?? '2026-11-22' } });
  });
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  return calendarRequests;
}

async function openPast(page: Page) {
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  await expect(page.getByRole('tab', { name: 'Past', exact: true })).toHaveAttribute('aria-selected', 'true');
}

function lastRequest(requests: URL[]) {
  const last = requests[requests.length - 1];
  return { from: last.searchParams.get('from'), to: last.searchParams.get('to'), view: last.searchParams.get('view') };
}

test('past view with no dates asks for the default window and hides Clear dates', async ({ page }) => {
  const requests = await setup(page);
  await openPast(page);
  await expect(page.getByText('Riverside clean')).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Clear dates' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Load earlier shifts' })).toBeVisible();
  expect(lastRequest(requests)).toEqual({ from: null, to: null, view: 'past' });
});

test('setting only From sends from and keeps the list to shifts on or after it', async ({ page }) => {
  const requests = await setup(page);
  await openPast(page);
  await page.getByLabel('From date', { exact: true }).fill('2026-11-19');
  await expect(page.getByText('Riverside clean')).toHaveCount(1);
  await expect(page.getByText('Oakfield clean')).toHaveCount(0);
  expect(lastRequest(requests)).toEqual({ from: '2026-11-19', to: null, view: 'past' });
  await expect(page.getByRole('button', { name: 'Clear dates' })).toBeVisible();
  // While a date is set, the "Load earlier" control is hidden.
  await expect(page.getByRole('button', { name: 'Load earlier shifts' })).toHaveCount(0);
});

test('setting only To sends to and keeps the list to shifts on or before it', async ({ page }) => {
  const requests = await setup(page);
  await openPast(page);
  await page.getByLabel('To date', { exact: true }).fill('2026-11-19');
  await expect(page.getByText('Oakfield clean')).toHaveCount(1);
  await expect(page.getByText('Riverside clean')).toHaveCount(0);
  expect(lastRequest(requests)).toEqual({ from: null, to: '2026-11-19', view: 'past' });
});

test('setting both dates sends both and shows only shifts inside the range', async ({ page }) => {
  const requests = await setup(page);
  await openPast(page);
  await page.getByLabel('From date', { exact: true }).fill('2026-11-18');
  await page.getByLabel('To date', { exact: true }).fill('2026-11-18');
  await expect(page.getByText('Oakfield clean')).toHaveCount(1);
  await expect(page.getByText('Riverside clean')).toHaveCount(0);
  expect(lastRequest(requests)).toEqual({ from: '2026-11-18', to: '2026-11-18', view: 'past' });
});

test('a range with no completed shifts shows the empty message', async ({ page }) => {
  await setup(page, 'empty');
  await openPast(page);
  await page.getByLabel('From date', { exact: true }).fill('2026-10-01');
  await page.getByLabel('To date', { exact: true }).fill('2026-10-02');
  await expect(page.getByText('No shifts scheduled.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Clear dates' })).toBeVisible();
});

test('Clear dates resets both fields and returns to the default window', async ({ page }) => {
  const requests = await setup(page);
  await openPast(page);
  await page.getByLabel('From date', { exact: true }).fill('2026-11-19');
  await page.getByLabel('To date', { exact: true }).fill('2026-11-22');
  await page.getByRole('button', { name: 'Clear dates' }).click();
  await expect(page.getByLabel('From date', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('To date', { exact: true })).toHaveValue('');
  await expect(page.getByText('Oakfield clean')).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Clear dates' })).toHaveCount(0);
  expect(lastRequest(requests)).toEqual({ from: null, to: null, view: 'past' });
});

test('the date fields carry min and max bounds that keep the range in order', async ({ page }) => {
  await setup(page);
  await openPast(page);
  await page.getByLabel('From date', { exact: true }).fill('2026-11-10');
  await expect(page.getByLabel('To date', { exact: true })).toHaveAttribute('min', '2026-11-10');
  await page.getByLabel('To date', { exact: true }).fill('2026-11-15');
  await expect(page.getByLabel('From date', { exact: true })).toHaveAttribute('max', '2026-11-15');
});

test('dates cannot be set after today', async ({ page }) => {
  await page.clock.setFixedTime(new Date('2026-11-23T10:00:00Z'));
  await setup(page);
  await openPast(page);
  await expect(page.getByLabel('To date', { exact: true })).toHaveAttribute('max', '2026-11-23');
  await expect(page.getByLabel('From date', { exact: true })).toHaveAttribute('max', '2026-11-23');
});

test('a failed past load shows a retry that reloads the same range', async ({ page }) => {
  const requests = await setup(page, 'error');
  await openPast(page);
  await page.getByLabel('From date', { exact: true }).fill('2026-11-19');
  await expect(page.getByRole('button', { name: 'Retry loading shifts' })).toBeVisible();
  const before = requests.length;
  await page.getByRole('button', { name: 'Retry loading shifts' }).click();
  await expect.poll(() => requests.length).toBeGreaterThan(before);
  expect(lastRequest(requests).from).toBe('2026-11-19');
});
