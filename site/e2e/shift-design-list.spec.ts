import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The Shifts list matches the design: Property, Visits and Actions columns. Visits shows a progress ring
// whose hover/focus text counts completed, ongoing, scheduled and cancelled visits. Missing cleaner,
// Missing Quality Cycle and Missing price tags sit on the property row.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const COMPANY = '11111111-1111-4111-8111-111111111111';
const BRANCH = '33333333-3333-4333-8333-333333333333';

test('the Shifts list shows Property, Visits and Actions with the visit progress and warning tags', async ({ page }) => {
  await setupAdminMocks(page);
  const now = new Date();
  const day = (d: number) => new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), d)).toISOString().slice(0, 10);
  await page.route(/\/api\/v1\/admin\/shifts\/design-list\/occurrences/, route => route.fulfill({ headers, json: [
    { startUtc: `${day(3)}T08:00:00Z`, endUtc: `${day(3)}T10:00:00Z`, status: 1, visitNumber: 1, visitCleaningType: 'Normal', assigneeIds: [], workStatus: 'Completed', isCancelledManualVisit: false },
    { startUtc: `${day(4)}T08:00:00Z`, endUtc: `${day(4)}T10:00:00Z`, status: 1, visitNumber: 2, visitCleaningType: 'Normal', assigneeIds: [], workStatus: 'Completed', isCancelledManualVisit: false },
    { startUtc: `${day(5)}T08:00:00Z`, endUtc: `${day(5)}T10:00:00Z`, status: 1, visitNumber: 3, visitCleaningType: 'Normal', assigneeIds: [], workStatus: 'InProgress', isCancelledManualVisit: false },
    { startUtc: `${day(6)}T08:00:00Z`, endUtc: `${day(6)}T10:00:00Z`, status: 1, visitNumber: 4, visitCleaningType: 'Normal', assigneeIds: [], workStatus: 'Scheduled', isCancelledManualVisit: false },
    { startUtc: `${day(7)}T08:00:00Z`, endUtc: `${day(7)}T10:00:00Z`, status: 4, visitNumber: 5, visitCleaningType: 'Normal', assigneeIds: [], workStatus: null, isCancelledManualVisit: true },
  ] }));
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [
    { id: 'design-list', companyId: COMPANY, branchId: BRANCH, name: 'Cleaning - Lakeside 4, 00100 Helsinki', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00', validFrom: day(1) }, isBranchMain: true, qualityCycleTemplateId: null },
  ], total: 1, counts: { active: 1, completed: 0, inactive: 0 } } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignee-names/, route => route.fulfill({ headers, json: { 'design-list': [] } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignment-counts/, route => route.fulfill({ headers, json: { 'design-list': 0 } }));
  await page.goto('/admin/shifts/');

  const table = page.getByRole('region', { name: 'Work orders table' }).getByRole('table');
  const headerRow = table.getByRole('row').first();
  await expect(headerRow.getByRole('columnheader')).toHaveText(['Property', 'Visits', 'Actions']);

  const progress = page.getByRole('img', { name: '2 of 4 visits completed, 1 ongoing, 1 scheduled, 1 cancelled' });
  await expect(progress).toBeVisible();
  await progress.hover();
  await expect(page.getByText('2 of 4 completed')).toBeVisible();

  await expect(page.getByText('Missing cleaner')).toBeVisible();
  await expect(page.getByText('Missing Quality Cycle')).toBeVisible();
  // The list's actions stay reachable while the progress tooltip is open.
  await progress.hover();
  await expect(page.getByRole('button', { name: /More actions for/ }).first()).toBeVisible();
  await page.getByRole('button', { name: /More actions for/ }).first().click();
  await expect(page.getByRole('menuitem', { name: 'Visits' })).toBeVisible();
  await page.keyboard.press('Escape');

  // A row click opens the visits in a side drawer, and each visit has a ⋯ menu.
  await page.getByRole('cell', { name: /Lakeside 4/ }).first().click();
  const drawer = page.getByRole('dialog', { name: /Visits of Cleaning - Lakeside/ });
  await expect(drawer).toBeVisible();
  if (process.env.SHIFT_SHOTS) await page.screenshot({ path: `${process.env.SHIFT_SHOTS}/drawer-${test.info().project.name}.png`, fullPage: false });
  await expect(drawer.getByRole('button', { name: 'Actions for visit 1' })).toBeVisible();
  await drawer.getByRole('button', { name: 'Actions for visit 1' }).click();
  await expect(page.getByRole('menuitem', { name: 'Change time' })).toBeVisible();
  await page.keyboard.press('Escape');
  await drawer.getByRole('button', { name: 'Close visits' }).click();
  await expect(drawer).toHaveCount(0);

  // Screenshot for review; written to the scratchpad folder when SHIFT_SHOTS is set.
  if (process.env.SHIFT_SHOTS) await page.screenshot({ path: `${process.env.SHIFT_SHOTS}/list-${test.info().project.name}.png`, fullPage: false });
});
