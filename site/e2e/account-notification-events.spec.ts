import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The profile page lets each person switch the in-app alert on or off for each event that applies to their role.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Authorization,Content-Type', 'Access-Control-Allow-Methods': 'GET,PUT,OPTIONS' };

const cleanerEvents = [
  { key: 'ShiftAssigned', group: 'Cleaner', label: 'Assigned to a shift', description: 'A visit or shift is given to you.', app: true, emailAvailable: false },
  { key: 'ShiftRemoved', group: 'Cleaner', label: 'Removed from a shift', description: 'A visit or shift is taken off your schedule.', app: true, emailAvailable: false },
  { key: 'ShiftUpdated', group: 'Cleaner', label: 'Shift changed', description: 'Times or details of a shift you are on have changed.', app: true, emailAvailable: false },
];
const staffEvents = [
  { key: 'StaffVisitOfferDeclined', group: 'Staff', label: 'Cleaner declined a visit', description: 'A cleaner declined a visit offer.', app: true, emailAvailable: false },
];

async function setup(page: Page, roles: string[], employeeRole: string, events: typeof cleanerEvents) {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(list => { (window as any).__MOCK_ROLES__ = list; }, roles);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers, json: { realm: 'tenant-a', employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: employeeRole, isActive: true } } }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ status: 200, headers, json: { autoAcceptWork: false } }));
  const state = { events: events.map(item => ({ ...item })), puts: [] as { key: string; body: unknown }[], failNext: false };
  await page.route(/\/api\/v1\/me\/notification-preferences(\/[^/]+)?$/, async route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (request.method() === 'PUT') {
      const key = decodeURIComponent(new URL(request.url()).pathname.split('/').pop() ?? '');
      const body = request.postDataJSON();
      state.puts.push({ key, body });
      if (state.failNext) { state.failNext = false; return route.fulfill({ status: 500, headers, json: { detail: 'Could not save notification settings. Try again.' } }); }
      state.events = state.events.map(item => item.key === key ? { ...item, app: body.app } : item);
    }
    return route.fulfill({ status: 200, headers, json: { events: state.events } });
  });
  await page.goto('/account/profile/?tenant=tenant-a');
  return state;
}

test('a cleaner sees only cleaner events and can switch one off in the app', async ({ page }) => {
  const state = await setup(page, ['employee'], 'employee', cleanerEvents);
  await expect(page.getByRole('heading', { name: 'For cleaners', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'For staff', exact: true })).toHaveCount(0);

  const removed = page.getByRole('switch', { name: 'Removed from a shift: in the app' });
  await expect(removed).toBeChecked();
  await removed.uncheck();
  await expect(removed).not.toBeChecked();
  await expect.poll(() => state.puts.length).toBe(1);
  expect(state.puts[0]).toEqual({ key: 'ShiftRemoved', body: { app: false } });

  // Email is shown for every event and cannot be switched on yet.
  await expect(page.getByRole('switch', { name: 'Removed from a shift: email' })).toBeDisabled();
  await expect(page.getByText('Email (coming soon)').first()).toBeVisible();
  if (process.env.PORTAL_SHOT_DIR) await page.screenshot({ path: `${process.env.PORTAL_SHOT_DIR}/notification-events-cleaner.png`, fullPage: true });
});

test('an administrator who is also an employee sees both groups', async ({ page }) => {
  await setup(page, ['admin', 'employee'], 'admin', [...cleanerEvents, ...staffEvents]);
  await expect(page.getByRole('heading', { name: 'For cleaners', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'For staff', exact: true })).toBeVisible();
  await expect(page.getByRole('switch', { name: 'Cleaner declined a visit: in the app' })).toBeChecked();
});

test('a failed save puts the switch back and says why', async ({ page }) => {
  const state = await setup(page, ['employee'], 'employee', cleanerEvents);
  state.failNext = true;
  const assigned = page.getByRole('switch', { name: 'Assigned to a shift: in the app' });
  await expect(assigned).toBeEnabled();
  await assigned.uncheck();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not save notification settings' })).toHaveCount(1);
  await expect(assigned).toBeChecked();
});
