import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
async function setup(page: Page) {
  await setupAdminMocks(page);
  let loads = 0;
  let shifts = [
    { id: 'active', name: 'Active shift', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
    { id: 'inactive', name: 'Inactive shift', companyId: 'company', branchId: 'branch', isActive: false, schedule: { type: 0, dailyStart: '10:00', dailyEnd: '12:00' } },
  ];
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const url = new URL(request.url()); const path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts') && request.method() === 'POST') {
      const created = { ...request.postDataJSON(), id: 'created', isActive: true }; shifts.push(created);
      return route.fulfill({ status: 201, headers, json: created });
    }
    if (path.endsWith('/deactivate')) {
      shifts = shifts.map(s => path.includes(`/${s.id}/`) ? { ...s, isActive: false } : s);
      return route.fulfill({ status: 204, headers });
    }
    if (path.endsWith('/shifts')) {
      loads++; expect(url.searchParams.get('includeInactive')).toBe('true');
      return route.fulfill({ status: 200, headers, json: shifts });
    }
    const json = path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Company', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'Company', isActive: true }, branches: [{ id: 'branch', name: 'Branch', isActive: true }] }
      : path.endsWith('/employees') ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', isActive: true }] : [];
    return route.fulfill({ status: 200, headers, json });
  });
  return { get loads() { return loads; } };
}

for (const width of [1280, 390]) test(`admin shift lifecycle tabs filter without reloading at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 844 }); const state = await setup(page);
  await page.goto('/admin/shifts/');
  const active = page.getByRole('tab', { name: 'Active (1)', exact: true });
  const inactive = page.getByRole('tab', { name: 'Inactive (1)', exact: true });
  await expect(active).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('cell', { name: 'Active shift', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Inactive shift', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Employees', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
  const loads = state.loads;
  await active.focus(); await active.press('End'); await expect(inactive).toBeFocused();
  await expect(page.getByRole('cell', { name: 'Inactive shift', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Employees', exact: true })).toHaveCount(0);
  await inactive.press('Home');
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toHaveCount(0);
  expect(state.loads).toBe(loads);
});

test('admin creating from Inactive returns to Active and deactivation moves the new shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Add shift' });
  await modal.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'Company', exact: true }).click();
  await modal.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'Branch', exact: true }).click();
  await modal.getByRole('textbox', { name: /^Name\s*\*?$/ }).fill('Created shift'); await modal.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('tab', { name: 'Active (2)', exact: true })).toHaveAttribute('aria-selected', 'true');
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'Created shift', exact: true }) });
  await expect(row).toBeVisible(); await row.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(row).toHaveCount(0); await page.getByRole('tab', { name: 'Inactive (2)', exact: true }).click();
  await expect(row).toBeVisible(); await expect(row.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();
});

test('late admin assignment response is discarded when lifecycle tabs change', async ({ page }) => {
  await setup(page);
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  let started = false; let delayed = true;
  await page.route(/\/admin\/shifts\/active\/assignments$/, async route => {
    if (route.request().method() !== 'GET' || !delayed) return route.fallback();
    delayed = false; started = true; await gate;
    return route.fulfill({ status: 200, headers, json: [{ id: 'late', employeeId: 'cleaner', isActive: true }] });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Employees', exact: true }).click();
  await expect.poll(() => started).toBe(true);
  await page.getByRole('tab', { name: /^Inactive/ }).click(); await page.getByRole('tab', { name: /^Active/ }).click();
  release(); await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Employees', exact: true }).click();
  await expect(page.getByText('No employees assigned.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unassign Test Cleaner' })).toHaveCount(0);
});
