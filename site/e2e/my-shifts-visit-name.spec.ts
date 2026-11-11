import { test, expect, Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// A cleaner's visit on a property's main shift keeps the shift name as its title and shows the visit's own name
// under it: number, dates and where it came from. Nothing about the price reaches the cleaner.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const MAIN = 'Deep Cleaning - Testikatu 1, 00100 Helsinki |34m2|2H|1V';
const base = { shiftId: '33333333-3333-4333-8333-333333333333', shiftName: MAIN, timeZoneId: 'Europe/Helsinki', form: null };
const adminVisit = { ...base, startUtc: '2026-11-10T06:00:00.000Z', endUtc: '2026-11-10T08:00:00.000Z', visitNumber: 3, visitSource: 'AdminPanel', notes: 'Ring the bell at the back door' };
const partnerRange = { ...base, startUtc: '2026-11-21T06:00:00.000Z', endUtc: '2026-11-25T14:00:00.000Z', visitNumber: 5, visitSource: 'PartnerPortal' };
const recurring = { ...base, shiftId: '55555555-5555-4555-8555-555555555555', shiftName: 'Morning clean', startUtc: '2026-11-11T06:00:00.000Z', endUtc: '2026-11-11T08:00:00.000Z' };

async function setupEmployee(page: Page, visits: any[]) {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-11-03T10:00:00Z') });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })],
      employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/shifts(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: [] }));
  await page.route(/\/api\/v1\/me\/shifts\/calendar\?/, route => {
    const view = new URL(route.request().url()).searchParams.get('view');
    return route.fulfill({ headers, json: { occurrences: view === 'future' ? visits : [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-04', toLocalDate: '2026-12-03' } });
  });
}

test('a visit on a main shift shows the shift name and its own numbered name', async ({ page }) => {
  await setupEmployee(page, [adminVisit, recurring, partnerRange]);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Future', exact: true }).click();
  await page.getByRole('button', { name: /10\/11\/2026 Tuesday.*1 shift/ }).click();
  const single = page.getByRole('group', { name: new RegExp(`${adminVisit.startUtc}`) });
  await expect(single.getByRole('heading', { name: MAIN, exact: true })).toBeVisible();
  await expect(single.getByTestId('visit-name')).toHaveText('#3 · Tue 10 Nov · Admin panel');
  await expect(single).toContainText('Ring the bell at the back door');
  // The cleaner never sees a price.
  await expect(single).not.toContainText(/€|\bEUR\b|price|\bVAT\b/i);

  await page.getByRole('button', { name: /21\/11\/2026 Saturday.*1 shift/ }).click();
  const range = page.getByRole('group', { name: new RegExp(`${partnerRange.startUtc}`) });
  await expect(range.getByRole('heading', { name: MAIN, exact: true })).toBeVisible();
  await expect(range.getByTestId('visit-name')).toHaveText('#5 · 21–25 Nov · Partner portal');
  await expect(range).not.toContainText(/€|\bEUR\b|price|\bVAT\b/i);
});

test('a visit on an ordinary shift shows no visit name', async ({ page }) => {
  await setupEmployee(page, [recurring]);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Future', exact: true }).click();
  await page.getByRole('button', { name: /11\/11\/2026 Wednesday.*1 shift/ }).click();
  const card = page.getByRole('group', { name: /Morning clean/ });
  await expect(card.getByRole('heading', { name: 'Morning clean', exact: true })).toBeVisible();
  await expect(card.getByTestId('visit-name')).toHaveCount(0);
});
