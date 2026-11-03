import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };

test('Employees column shows active assignment count after reload without opening Employees', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const company = { id: 'c-1', name: 'CleanTech Oy', businessId: '1111111-1', isActive: true };
  const employees = [
    { id: 'e-1', firstName: 'Sami', lastName: 'Siivooja', email: 'sami@test.fi', role: 'employee', isActive: true, skills: [], serviceAreas: [], certifications: [], defaultHours: {} },
    { id: 'e-2', firstName: 'Liisa', lastName: 'Lakaisija', email: 'liisa@test.fi', role: 'employee', isActive: true, skills: [], serviceAreas: [], certifications: [], defaultHours: {} },
    { id: 'e-3', firstName: 'Old', lastName: 'Worker', email: 'old@test.fi', role: 'employee', isActive: true, skills: [], serviceAreas: [], certifications: [], defaultHours: {} },
  ];
  const shift = { id: 's-1', companyId: 'c-1', branchId: 'b-1', name: 'Morning shift', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } };

  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ status: 200, headers: CORS, json: { items: [company], total: 1 } }));
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ status: 200, headers: CORS, json: employees }));
  const assignments = [
    { id: 'a-1', shiftId: 's-1', employeeId: 'e-1', assignedAtUtc: '2026-11-01T08:00:00Z', isActive: true },
    { id: 'a-2', shiftId: 's-1', employeeId: 'e-2', assignedAtUtc: '2026-11-01T08:00:00Z', isActive: true },
    { id: 'a-3', shiftId: 's-1', employeeId: 'e-3', assignedAtUtc: '2026-10-01T08:00:00Z', isActive: false },
  ];
  await page.route('**/api/v1/admin/shifts**', route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (route.request().url().includes('/s-1/assignments')) return route.fulfill({ status: 200, headers: CORS, json: assignments });
    return route.fulfill({ status: 200, headers: CORS, json: [shift] });
  });

  await page.goto('/admin/shifts/');
  await page.reload();
  const row = page.getByRole('row').filter({ hasText: 'Morning shift' });
  await expect(row).toBeVisible();
  await expect(row.getByRole('cell').nth(2)).toHaveText('2');
});
