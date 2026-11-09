import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { openShiftWorkspace, shiftAction } from './shift-actions';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

type MockShift = { id: string; name: string; companyId: string; branchId: string; isActive: boolean; isCompleted?: boolean; schedule: Record<string, unknown>; qualityCycleTemplateId?: string };

function initialShifts(): MockShift[] {
  return [
    { id: 'daily', name: 'Workspace shift', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
    { id: 'oncall', name: 'Workspace on-call', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 4 } },
    { id: 'paused', name: 'Workspace paused', companyId: 'company', branchId: 'branch', isActive: false, schedule: { type: 0, dailyStart: '10:00', dailyEnd: '12:00' } },
    { id: 'staffed', name: 'Workspace staffed', companyId: 'company', branchId: 'branch', isActive: true, schedule: { type: 0, dailyStart: '13:00', dailyEnd: '14:00' } },
    { id: 'done', name: 'Workspace done', companyId: 'company', branchId: 'branch', isActive: true, isCompleted: true, schedule: { type: 6, oneTimeDate: '2026-10-01', oneTimeStart: '09:00', oneTimeEnd: '11:00' } },
  ];
}

async function setup(page: Page) {
  await setupAdminMocks(page);
  let shifts = initialShifts();
  await page.route(/\/api\/v1\/admin\//, route => {
    const request = route.request(); const url = new URL(request.url()); const path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const action = path.match(/\/shifts\/([^/]+)\/(deactivate|reactivate)$/);
    if (action) {
      const [, id, verb] = action;
      shifts = shifts.map(s => s.id === id ? { ...s, isActive: verb === 'reactivate' } : s);
      const updated = shifts.find(s => s.id === id);
      return verb === 'reactivate' ? route.fulfill({ status: 200, headers, json: updated }) : route.fulfill({ status: 204, headers });
    }
    const removed = path.match(/\/shifts\/([^/]+)$/);
    if (request.method() === 'DELETE' && removed) {
      shifts = shifts.filter(s => s.id !== removed[1]);
      return route.fulfill({ status: 204, headers });
    }
    if (path.endsWith('/pricing')) return route.fulfill({ status: 200, headers, json: { plans: [], overrides: [] } });
    if (path.endsWith('/shifts/assignment-counts')) return route.fulfill({ status: 200, headers, json: { daily: 0, oncall: 0, paused: 0, staffed: 1, done: 0 } });
    if (path.endsWith('/shifts/page')) return route.fulfill({ status: 200, headers, json: shiftsResponse(shifts, route.request().url()) });
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

test('workspace next step reports assigned cleaners when a shift is staffed', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace staffed');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true });
  await expect(dialog.getByText('1 assigned', { exact: true })).toBeVisible();
  await expect(dialog.getByText('Next step: Assigned cleaners see visits in My shifts. Admin: review visit changes under Occurrences.', { exact: true })).toBeVisible();
});

test('workspace assign action closes the workspace and opens the existing assignment panel', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  await page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true }).getByRole('button', { name: 'Assign employees', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
});

test('workspace Edit schedule opens the existing Edit shift dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  await page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true }).getByRole('button', { name: 'Edit schedule', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(edit).toBeVisible();
  await expect(edit.getByRole('textbox', { name: /^Name/ })).toHaveCount(0);
});

test('workspace Checklist template opens the existing template dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  await page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true }).getByRole('button', { name: 'Checklist template', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  const template = page.getByRole('dialog', { name: 'Assign checklist template', exact: true });
  await expect(template).toBeVisible();
  await expect(template.getByText('Workspace shift', { exact: true })).toBeVisible();
});

test('workspace Pricing opens the existing shift pricing dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  await page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true }).getByRole('button', { name: 'Pricing', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  await expect(page.getByRole('dialog').filter({ hasText: 'Shift pricing — Workspace shift' })).toBeVisible();
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

test('completed one-time shift shows Completed status and a no-further-visits next step', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Completed/ }).click();
  const row = page.getByRole('row').filter({ hasText: 'Workspace done' });
  await expect(row.getByRole('cell', { name: 'Completed', exact: true })).toBeVisible();
  await openShiftWorkspace(page, 'Workspace done');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace done', exact: true }).getByText('Next step: Completed. No further visits.', { exact: true })).toBeVisible();
});

test('inactive shift workspace offers only reactivate and delete', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Workspace paused' }).getByRole('cell', { name: 'Inactive', exact: true })).toBeVisible();
  await openShiftWorkspace(page, 'Workspace paused');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace paused', exact: true });
  await expect(dialog.getByText('Next step: Admin: reactivate to schedule again, or delete if permanent removal is intended.', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Delete', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Occurrences', exact: true })).toHaveCount(0);
});

test('workspace Reactivate restores the shift and closes the workspace', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await openShiftWorkspace(page, 'Workspace paused');
  await page.getByRole('dialog', { name: 'Manage — Workspace paused', exact: true }).getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace paused', exact: true })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Workspace paused' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Workspace paused' })).toBeVisible();
});

test('workspace Deactivate and Delete move and remove the shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace staffed');
  await page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true }).getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Workspace staffed' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await openShiftWorkspace(page, 'Workspace staffed');
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true }).getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Workspace staffed' })).toHaveCount(0);
});

test('row menu action still reaches the existing Employees panel', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Employees');
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
});

test('phone-width workspace has no horizontal overflow and its actions stay inside the viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace shift');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  const viewport = page.viewportSize();
  for (const label of ['Edit schedule', 'Assign employees', 'Occurrences', 'Pricing']) {
    const box = await dialog.getByRole('button', { name: label, exact: true }).boundingBox();
    expect(box).not.toBeNull();
    if (box && viewport) {
      expect(box.x).toBeGreaterThanOrEqual(0);
      expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
    }
  }
});
