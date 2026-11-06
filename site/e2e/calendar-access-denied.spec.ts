import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const DETAIL = 'Your account does not have shift calendar access. Ask an owner to enable Shift calendar for your employee record, then reload.';

async function openCalendarDenied(page: import('@playwright/test').Page, body: unknown) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/shifts\/calendar(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ status: 403, headers, json: body });
  });
  await page.goto('/admin/shifts/calendar/');
}

test('a calendar refusal shows the server reason and the next step, without a retry that cannot succeed', async ({ page }) => {
  await openCalendarDenied(page, { detail: DETAIL });
  await expect(page.getByRole('alert').filter({ hasText: DETAIL })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry' })).toHaveCount(0);
});

test('a calendar refusal without a reason still tells the user what to do', async ({ page }) => {
  await openCalendarDenied(page, {});
  await expect(page.getByRole('alert').filter({ hasText: 'Shift calendar access is not enabled for your account. Ask an owner to enable it, then reload.' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry' })).toHaveCount(0);
});
