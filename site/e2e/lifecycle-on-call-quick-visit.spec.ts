import { test, expect, Page } from '@playwright/test';
import { shiftAction } from './shift-actions';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

// An active on-call flexible shift with a default cleaner and notes.
const quickShift = {
  id: 'quick', companyId: 'company', branchId: 'branch', name: 'Quick on-call', isActive: true, notes: 'Gate code 42',
  defaultEmployeeId: 'emp-1', validFrom: '2026-10-01T00:00:00', validUntil: null, schedule: { type: 4 },
};
// Same shift without notes or a default cleaner.
const bareShift = { ...quickShift, id: 'bare', name: 'Bare on-call', notes: null, defaultEmployeeId: null };
// A dated one-time shift: no quick visit action.
const datedShift = {
  id: 'dated', companyId: 'company', branchId: 'branch', name: 'Dated shift', isActive: true, notes: null, qualityCycleTemplateId: null,
  validFrom: '2026-10-01T00:00:00', validUntil: '2026-10-01T00:00:00', schedule: { type: 6, oneTimeDate: '2026-10-01', oneTimeStart: '09:00', oneTimeEnd: '11:30' },
};

async function mockAdmin(page: Page, seeded: any[]) {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-11-03T10:00:00Z') });
  const shifts = [...seeded];
  const exceptionWrites: any[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const path = new URL(route.request().url()).pathname;
    return route.fulfill({ headers, json: isShiftsRoute(path) ? shiftsResponse(shifts, route.request().url())
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  // Registered after the generic admin route, so it takes precedence for the exception save.
  await page.route(/\/api\/v1\/admin\/shifts\/[^/]+\/occurrence-exceptions$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    exceptionWrites.push(route.request().postDataJSON());
    return route.fulfill({ headers, json: { id: 'exception-1' } });
  });
  return exceptionWrites;
}

test('on-call quick visit prefills the default cleaner and notes; the user sets only date and times', async ({ page }) => {
  const writes = await mockAdmin(page, [quickShift]);
  await page.goto('/admin/shifts/');
  await shiftAction(page, 'Quick on-call', 'Add visit');

  const editor = page.getByRole('dialog', { name: 'Change occurrence', exact: true });
  await expect(editor.getByRole('textbox', { name: 'Reason', exact: true })).toHaveValue('Gate code 42');
  await editor.getByLabel(/^Start \(/).fill('2026-11-20T09:00');
  await editor.getByLabel(/^End \(/).fill('2026-11-20T11:00');
  await editor.getByRole('button', { name: 'Save occurrence', exact: true }).click();

  await expect(editor).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ status: 4, employeeId: 'emp-1', reason: 'Gate code 42' });
  await expect(page.getByText('One-time visit scheduled.')).toBeVisible();
});

test('quick visit uses "Quick visit" as the reason and no cleaner when the shift has neither', async ({ page }) => {
  const writes = await mockAdmin(page, [bareShift]);
  await page.goto('/admin/shifts/');
  await shiftAction(page, 'Bare on-call', 'Add visit');

  const editor = page.getByRole('dialog', { name: 'Change occurrence', exact: true });
  await expect(editor.getByRole('textbox', { name: 'Reason', exact: true })).toHaveValue('Quick visit');
  await editor.getByLabel(/^Start \(/).fill('2026-11-21T09:00');
  await editor.getByLabel(/^End \(/).fill('2026-11-21T11:00');
  await editor.getByRole('button', { name: 'Save occurrence', exact: true }).click();

  await expect(editor).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ status: 4, employeeId: null, reason: 'Quick visit' });
});

test('dated one-time shifts do not offer the quick visit action', async ({ page }) => {
  await mockAdmin(page, [datedShift]);
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'More actions for Dated shift', exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Add visit', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
});
