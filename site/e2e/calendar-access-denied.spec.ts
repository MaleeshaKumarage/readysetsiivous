import { test, expect } from '@playwright/test';
import { setupScheduler, column, panel, PAST } from './scheduler-fixture';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const DETAIL = 'Your account does not have shift calendar access. Ask an owner to enable Shift calendar for your employee record, then reload.';

// The Scheduler reads work times from the shift calendar. When the server refuses it, the schedule still works
// and the visit panel says why work times and photos are unavailable, with no retry that cannot succeed.
async function openDenied(page: import('@playwright/test').Page, body: unknown) {
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 403, headers, json: body });
  });
  await setupScheduler(page, { query: `?date=${PAST}` });
  // The fixture's catch-all route is registered later, so the refusal is installed again on top of it.
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 403, headers, json: body });
  });
  await page.reload();
  await column(page, 'Anna Korhonen', PAST).getByRole('button', { name: /^Kallio, Lindqvist flat/ }).click();
}

test('a calendar refusal shows the server reason and the next step, without a retry that cannot succeed', async ({ page }) => {
  await openDenied(page, { detail: DETAIL });
  await expect(panel(page).getByText(DETAIL)).toBeVisible();
  await expect(panel(page).getByRole('button', { name: 'Record start and end' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Retry' })).toHaveCount(0);
});

test('a calendar refusal without a reason still tells the user what to do', async ({ page }) => {
  await openDenied(page, {});
  await expect(panel(page).getByText('Shift calendar access is not enabled for your account. Ask an owner to enable it, then reload.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry' })).toHaveCount(0);
});
