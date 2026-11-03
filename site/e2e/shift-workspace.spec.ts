import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { openShiftWorkspace, shiftAction } from './shift-actions';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

const shifts = [
  { id: 'daily', name: 'Workspace shift', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  { id: 'oncall', name: 'Workspace on-call', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 4 } },
  { id: 'paused', name: 'Workspace paused', companyId: 'company', branchId: 'branch', isActive: false, schedule: { type: 0, dailyStart: '10:00', dailyEnd: '12:00' } },
];

async function setup(page: Page) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.endsWith('/shifts/assignment-counts')) return route.fulfill({ status: 200, headers, json: { daily: 0, oncall: 0, paused: 0 } });
    if (path.endsWith('/shifts')) return route.fulfill({ status: 200, headers, json: shifts });
    const json = path.endsWith('/companies') ? { items: [{ id: 'company', name: 'Company', isActive: true }], total: 1 }
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'Company', isActive: true }, branches: [{ id: 'branch', name: 'Branch', isActive: true }] }
      : path.endsWith('/employees') ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', isActive: true }] : [];
    return route.fulfill({ status: 200, headers, json });
  });
}

test('each shift row exposes one primary Manage action and a More actions menu', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  const row = page.getByRole('row').filter({ hasText: 'Workspace shift' }).first();
  await expect(row.getByRole('button', { name: 'Manage', exact: true })).toBeVisible();
  await expect(row.getByRole('cell', { name: 'Active', exact: true })).toBeVisible();
  await row.getByRole('button', { name: 'More actions for Workspace shift', exact: true }).click();
  for (const label of ['Pricing', 'Edit', 'Employees', 'Occurrences', 'Checklist template', 'Deactivate']) {
    await expect(page.getByRole('menuitem', { name: label, exact: true })).toBeVisible();
  }
  await expect(page.getByRole('menuitem', { name: 'Schedule one-time visit', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
});

test('workspace sections show schedule, assignees, visits, checklist and pricing with the next step', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true });
  await expect(dialog.getByText('Next step: Admin: assign employees to this shift.', { exact: true })).toBeVisible();
  for (const section of ['Schedule', 'Assignees', 'Visits', 'Checklist', 'Pricing']) {
    await expect(dialog.getByRole('heading', { name: section, exact: true })).toBeVisible();
  }
  await expect(dialog.getByText('Daily 08:00–10:00', { exact: true })).toBeVisible();
  await expect(dialog.getByText('0 assigned', { exact: true })).toBeVisible();
});

test('workspace actions close the workspace and open the existing assignment panel', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  await page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true }).getByRole('button', { name: 'Assign employees', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
});

test('workspace Occurrences opens the existing occurrences dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  await page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true }).getByRole('button', { name: 'Occurrences', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Occurrences — Workspace shift', exact: true })).toBeVisible();
});

test('on-call workspace explains dated visits and offers one-time scheduling', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace on-call');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace on-call', exact: true });
  await expect(dialog.getByText('Next step: Admin: schedule a dated visit, then assign a cleaner.', { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Schedule one-time visit', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Change occurrence', exact: true })).toBeVisible();
});

test('inactive shift workspace offers only reactivate and delete', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await openShiftWorkspace(page, 'Workspace paused');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace paused', exact: true });
  await expect(dialog.getByText('Next step: Admin: reactivate to schedule again, or delete if permanent removal is intended.', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Occurrences', exact: true })).toHaveCount(0);
});

test('row menu action still reaches the existing Employees panel on a phone-width page', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Employees');
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});
