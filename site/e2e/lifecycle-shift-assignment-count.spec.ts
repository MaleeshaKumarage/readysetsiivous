import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { shiftRowAction } from './shift-actions';

// The Employees column names the active assignees. Names come from one bulk request for the page's shifts,
// and opening Assign employees fills in that shift's names from the freshly loaded roster.
const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };
const company = { id: 'c-1', name: 'CleanTech Oy', businessId: '1111111-1', isActive: true };
const employees = [
  { id: 'e-1', firstName: 'Sami', lastName: 'Siivooja', email: 'sami@test.fi', role: 'employee', isActive: true, skills: [], serviceAreas: [], certifications: [], defaultHours: {} },
  { id: 'e-2', firstName: 'Liisa', lastName: 'Lakaisija', email: 'liisa@test.fi', role: 'employee', isActive: true, skills: [], serviceAreas: [], certifications: [], defaultHours: {} },
];
const shifts = [
  { id: 's-1', companyId: 'c-1', branchId: 'b-1', name: 'Morning shift', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  { id: 's-2', companyId: 'c-1', branchId: 'b-1', name: 'Evening shift', isActive: true, schedule: { type: 0, dailyStart: '18:00', dailyEnd: '20:00' } },
];

// One active and one inactive assignment: only the active one is named.
const roster = [
  { id: 'a-1', shiftId: 's-1', employeeId: 'e-1', assignedAtUtc: '2026-11-01T08:00:00Z', isActive: true },
  { id: 'a-2', shiftId: 's-1', employeeId: 'e-2', assignedAtUtc: '2026-10-01T08:00:00Z', isActive: false },
];

async function mockShifts(page: Page, names: { status: number; json?: unknown }, assignments: unknown[] = []) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ status: 200, headers: CORS, json: { items: [company], total: 1 } }));
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ status: 200, headers: CORS, json: employees }));
  const calls = { names: 0, perShift: 0 };
  await page.route('**/api/v1/admin/shifts**', route => {
    const url = route.request().url();
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (url.includes('/assignee-names')) { calls.names++; return route.fulfill({ status: names.status, headers: CORS, json: names.json ?? {} }); }
    if (/\/shifts\/[^/]+\/assignments/.test(url)) { calls.perShift++; return route.fulfill({ status: 200, headers: CORS, json: assignments }); }
    return route.fulfill({ status: 200, headers: CORS, json: shiftsResponse(shifts, url) });
  });
  return calls;
}

test('Employees column names the active assignees from one bulk request after reload', async ({ page }) => {
  const calls = await mockShifts(page, { status: 200, json: { 's-1': ['Liisa Lakaisija', 'Sami Siivooja'], 's-2': [] } });
  await page.goto('/admin/shifts/');
  await page.reload();
  const morning = page.getByRole('row').filter({ hasText: 'Morning shift' });
  const evening = page.getByRole('row').filter({ hasText: 'Evening shift' });
  await expect(morning.getByRole('cell').nth(2)).toHaveText('Liisa Lakaisija, Sami Siivooja');
  await expect(evening.getByRole('cell').nth(2)).toHaveText('None');
  expect(calls.perShift).toBe(0);
  expect(calls.names).toBeGreaterThan(0);
});

test('Employees column reports a failed names load instead of showing zero', async ({ page }) => {
  await mockShifts(page, { status: 500 });
  await page.goto('/admin/shifts/');
  const morning = page.getByRole('row').filter({ hasText: 'Morning shift' });
  await expect(morning.getByRole('cell').nth(2)).toHaveText('—');
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load employee names. Refresh the page to retry.' })).toBeVisible();
});

test('opening Assign employees fills the names from the freshly loaded roster, naming only active rows', async ({ page }) => {
  await mockShifts(page, { status: 200, json: { 's-1': ['Liisa Lakaisija'], 's-2': [] } }, roster);
  await page.goto('/admin/shifts/');
  const morning = page.getByRole('row').filter({ hasText: 'Morning shift' });
  await expect(morning.getByRole('cell').nth(2)).toHaveText('Liisa Lakaisija');
  await shiftRowAction(page, morning, 'Assign employees');
  await expect(page.getByRole('row').filter({ hasText: 'Morning shift' }).first().getByRole('cell').nth(2)).toHaveText('Sami Siivooja');
});

test('when the names load fails, an opened roster still fills that shift\'s names', async ({ page }) => {
  await mockShifts(page, { status: 500 }, roster);
  await page.goto('/admin/shifts/');
  const morning = page.getByRole('row').filter({ hasText: 'Morning shift' });
  await expect(morning.getByRole('cell').nth(2)).toHaveText('—');
  await shiftRowAction(page, morning, 'Assign employees');
  await expect(page.getByRole('row').filter({ hasText: 'Morning shift' }).first().getByRole('cell').nth(2)).toHaveText('Sami Siivooja');
});
