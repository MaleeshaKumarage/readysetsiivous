import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { shiftRowAction } from './shift-actions';

// The work orders list has no Employees column (the design keeps assignees in Assign employees).
// Missing cleaner comes from the assignment counts; Assign employees lists the active assignees.
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

async function mockShifts(page: Page, counts: { status: number; json?: unknown }, assignments: unknown[] = []) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ status: 200, headers: CORS, json: { items: [company], total: 1 } }));
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ status: 200, headers: CORS, json: employees }));
  const calls = { counts: 0, perShift: 0, names: 0 };
  await page.route('**/api/v1/admin/shifts**', route => {
    const url = route.request().url();
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (url.includes('/assignee-names')) { calls.names++; return route.fulfill({ status: 200, headers: CORS, json: {} }); }
    if (url.includes('/assignment-counts')) { calls.counts++; return route.fulfill({ status: counts.status, headers: CORS, json: counts.json ?? {} }); }
    if (/\/shifts\/[^/]+\/assignments/.test(url)) { calls.perShift++; return route.fulfill({ status: 200, headers: CORS, json: assignments }); }
    return route.fulfill({ status: 200, headers: CORS, json: shiftsResponse(shifts, url) });
  });
  return calls;
}

test('the work orders list has no Employees column and does not request assignee names', async ({ page }) => {
  const calls = await mockShifts(page, { status: 200, json: { 's-1': 1, 's-2': 1 } });
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('region', { name: 'Work orders table' }).getByRole('columnheader')).toHaveText(['Property', '', 'Visits', 'Actions']);
  await expect(page.getByRole('row').filter({ hasText: 'Morning shift' })).toBeVisible();
  expect(calls.names).toBe(0);
});

test('a shift with no active cleaner shows the Missing cleaner tag', async ({ page }) => {
  await mockShifts(page, { status: 200, json: { 's-1': 0, 's-2': 1 } });
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('row').filter({ hasText: 'Morning shift' }).getByText('Missing cleaner', { exact: true })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Evening shift' }).getByText('Missing cleaner', { exact: true })).toHaveCount(0);
});

test('Assign employees lists the active assignees and leaves out inactive ones', async ({ page }) => {
  await mockShifts(page, { status: 200, json: { 's-1': 1, 's-2': 0 } }, roster);
  await page.goto('/admin/shifts/');
  const morning = page.getByRole('row').filter({ hasText: 'Morning shift' });
  await shiftRowAction(page, morning, 'Assign employees');
  await expect(page.getByRole('heading', { name: 'Assign employees — Morning shift' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unassign Sami Siivooja' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unassign Liisa Lakaisija' })).toHaveCount(0);
});

test('a failed assignment count load leaves the list and Assign employees usable', async ({ page }) => {
  await mockShifts(page, { status: 500 }, roster);
  await page.goto('/admin/shifts/');
  const morning = page.getByRole('row').filter({ hasText: 'Morning shift' });
  await expect(morning).toBeVisible();
  await shiftRowAction(page, morning, 'Assign employees');
  await expect(page.getByRole('button', { name: 'Unassign Sami Siivooja' })).toBeVisible();
});
