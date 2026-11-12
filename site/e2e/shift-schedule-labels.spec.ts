import { test, expect } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

// The shift list names each schedule in words a person can act on: varied times list their weekdays, and the
// property's visit shift says it holds numbered visits rather than reading as an on-call shift.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('the shift list shows the weekdays of a varied-times shift and marks the visit shift as numbered visits', async ({ page }) => {
  await setupAdminMocks(page);
  const shifts = [
    { id: 'varied', name: 'Cleaning - Varied 1, Helsinki', companyId: 'company', branchId: 'branch', isActive: true,
      schedule: { type: 1, dailyTimes: { Tuesday: { start: '08:00', end: '17:00' }, Thursday: { start: '08:00', end: '17:00' } } } },
    { id: 'main', name: 'Cleaning - Visits 2, Helsinki', companyId: 'company', branchId: 'branch', isActive: true, isBranchMain: true,
      schedule: { type: 4 } },
  ];
  await page.route(/\/api\/v1\/admin\//, route => {
    const url = route.request().url(); const path = new URL(url).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = isShiftsRoute(path) ? shiftsResponse(shifts, url) : path.endsWith('/companies') ? { items: [], total: 0 } : path.endsWith('/employees') ? [] : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/');
  const varied = page.getByRole('row').filter({ hasText: 'Cleaning - Varied 1' });
  await expect(varied).toContainText('Varied times: Tuesday, Thursday');
  const visits = page.getByRole('row').filter({ hasText: 'Cleaning - Visits 2' });
  await expect(visits).toContainText('Visit shift (numbered visits)');
  await expect(visits).not.toContainText('On-call');
});
