import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { openShiftWorkspace, shiftAction, confirmShiftPopup } from './shift-actions';

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

test('each shift row exposes one More actions menu, with details first and no separate Manage button', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  const row = page.getByRole('row').filter({ hasText: 'Workspace shift' }).first();
  await expect(row.getByRole('button', { name: 'Manage', exact: true })).toHaveCount(0);
  await expect(row.getByRole('gridcell', { name: 'Active', exact: true })).toBeVisible();
  await row.getByRole('button', { name: 'More actions for Workspace shift', exact: true }).click();
  for (const label of ['Pricing', 'Edit schedule', 'Assign employees', 'Occurrences', 'Checklist template', 'Deactivate']) {
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
  await shiftAction(page, 'Workspace shift', 'Assign employees');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
});

test('workspace Edit schedule opens the existing Edit shift dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Edit schedule');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(edit).toBeVisible();
  // Shift names are generated from the property, so a shift opened for editing shows its name read-only.
  await expect(edit.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Workspace shift');
});

test('workspace Checklist template opens the existing template dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Checklist template');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  const template = page.getByRole('dialog', { name: 'Assign checklist template', exact: true });
  await expect(template).toBeVisible();
  await expect(template.getByText('Workspace shift', { exact: true })).toBeVisible();
});

test('workspace Pricing opens the existing shift pricing dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Pricing');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace shift', exact: true })).toHaveCount(0);
  await expect(page.getByRole('dialog').filter({ hasText: 'Shift pricing — Workspace shift' })).toBeVisible();
});

test('workspace Occurrences opens the existing occurrences dialog for that shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Occurrences');
  await expect(page.getByRole('dialog', { name: 'Occurrences — Workspace shift', exact: true })).toBeVisible();
});

test('on-call workspace explains dated visits and offers one-time scheduling', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await openShiftWorkspace(page, 'Workspace on-call');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace on-call', exact: true });
  await expect(dialog.getByText('Next step: Admin: schedule a dated visit, then assign a cleaner.', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await shiftAction(page, 'Workspace on-call', 'Schedule one-time visit');
  await expect(page.getByRole('dialog', { name: 'Change occurrence', exact: true })).toBeVisible();
});

test('completed one-time shift shows Completed status and a no-further-visits next step', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Completed/ }).click();
  const row = page.getByRole('row').filter({ hasText: 'Workspace done' });
  await expect(row.getByRole('gridcell', { name: 'Completed', exact: true })).toBeVisible();
  await openShiftWorkspace(page, 'Workspace done');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace done', exact: true }).getByText('Next step: Completed. No further visits.', { exact: true })).toBeVisible();
});

test('inactive shift workspace offers only reactivate and delete', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Workspace paused' }).getByRole('gridcell', { name: 'Inactive', exact: true })).toBeVisible();
  await openShiftWorkspace(page, 'Workspace paused');
  const dialog = page.getByRole('dialog', { name: 'Manage — Workspace paused', exact: true });
  await expect(dialog.getByText('Next step: Admin: reactivate to schedule again, or delete if permanent removal is intended.', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Reactivate', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'More actions for Workspace paused', exact: true }).click();
  await expect(page.getByRole('menuitem', { name: 'Reactivate', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Delete', exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Occurrences', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
});

test('workspace Reactivate restores the shift and closes the workspace', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await shiftAction(page, 'Workspace paused', 'Reactivate'); await confirmShiftPopup(page, 'Reactivate');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace paused', exact: true })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Workspace paused' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Workspace paused' })).toBeVisible();
});

test('workspace Deactivate and Delete move and remove the shift', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace staffed', 'Deactivate'); await confirmShiftPopup(page, 'Deactivate');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Workspace staffed' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  page.once('dialog', dialog => dialog.accept());
  await shiftAction(page, 'Workspace staffed', 'Delete'); await confirmShiftPopup(page, 'Delete');
  await expect(page.getByRole('dialog', { name: 'Manage — Workspace staffed', exact: true })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Workspace staffed' })).toHaveCount(0);
});

test('row menu action still reaches the existing Employees panel', async ({ page }) => {
  await setup(page); await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Assign employees');
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeVisible();
});

test('phone-width workspace has no horizontal overflow and its actions stay inside the viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setup(page); await page.goto('/admin/shifts/');
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  await page.getByRole('button', { name: 'More actions for Workspace shift', exact: true }).click();
  const viewport = page.viewportSize();
  for (const label of ['Edit schedule', 'Assign employees', 'Occurrences', 'Pricing']) {
    const box = await page.getByRole('menuitem', { name: label, exact: true }).boundingBox();
    expect(box).not.toBeNull();
    if (box && viewport) {
      expect(box.x).toBeGreaterThanOrEqual(0);
      expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
    }
  }
});

test('editing a shift keeps its generated name read-only, and saving sends the name the server builds', async ({ page }) => {
  await setup(page);
  const saved: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/daily$/, route => {
    if (route.request().method() === 'PUT') { saved.push(route.request().postDataJSON() as Record<string, unknown>); return route.fulfill({ status: 200, headers, json: { id: 'daily', ...saved[saved.length - 1] } }); }
    return route.fallback();
  });
  await page.goto('/admin/shifts/');
  await shiftAction(page, 'Workspace shift', 'Edit schedule');
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  const nameField = edit.getByRole('textbox', { name: 'Name', exact: true });
  await expect(nameField).toHaveValue('Workspace shift');
  await expect(nameField).toHaveAttribute('readonly', '');
  await expect(edit.getByRole('button', { name: 'Reset to generated name', exact: true })).toHaveCount(0);
  await edit.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(edit).toHaveCount(0);
  expect(saved).toHaveLength(1);
  // The name is the generated one, never a blank or stale value.
  expect(String(saved[0].name)).toMatch(/Cleaning/);
});
