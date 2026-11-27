import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// The work order menu opens the property's price history; the visits filter stays on one row; and a network failure reads as a sentence.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const COMPANY = '11111111-1111-4111-8111-111111111111';
const BRANCH = '33333333-3333-4333-8333-333333333333';

async function setup(page: import('@playwright/test').Page, options: { pricingFails?: boolean; onCall?: boolean } = {}) {
  await setupAdminMocks(page);
  const day = (d: number) => new Date(Date.UTC(new Date().getUTCFullYear(), new Date().getUTCMonth(), d)).toISOString().slice(0, 10);
  await page.route(/\/api\/v1\/admin\/shifts\/price-history\/occurrences/, route => route.fulfill({ headers, json: [
    { startUtc: `${day(3)}T08:00:00Z`, endUtc: `${day(3)}T10:00:00Z`, status: 1, visitNumber: 1, visitCleaningType: 'Normal', assigneeIds: [], workStatus: 'Scheduled', isCancelledManualVisit: false, canAssign: true },
  ] }));
  await page.route(/\/api\/v1\/admin\/shifts\/price-history\/pricing/, route => options.pricingFails ? route.abort('failed') : route.fulfill({ headers, json: { plans: [], overrides: [] } }));
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => route.fulfill({ headers, json: { items: [
    { id: 'price-history', companyId: COMPANY, branchId: BRANCH, name: 'Cleaning - Lakeside 4, 00100 Helsinki', isActive: true, schedule: options.onCall ? { type: 4 } : { type: 0, dailyStart: '08:00', dailyEnd: '10:00', validFrom: day(1) }, isBranchMain: false, qualityCycleTemplateId: null },
  ], total: 1, counts: { active: 1, completed: 0, inactive: 0 } } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignee-names/, route => route.fulfill({ headers, json: { 'price-history': [] } }));
  await page.route(/\/api\/v1\/admin\/shifts\/assignment-counts/, route => route.fulfill({ headers, json: { 'price-history': 0 } }));
  await page.route(new RegExp(`/api/v1/admin/companies/${COMPANY}$`), route => route.fulfill({ headers, json: {
    company: { id: COMPANY, name: 'Alpha Oy', status: 'active' },
    branches: [{ id: BRANCH, name: 'Lake apartment', street: 'Lakeside 4', postalCode: '00100', city: 'Helsinki', sizeM2: 34, roomCount: 3, bedCount: 5, isActive: true, pricingReady: true,
      priceVersions: [
        { effectiveFrom: '2026-01-01', mode: 'Monthly', priceNet: 120, vatRatePercent: 25.5, durationMinutes: 60 },
        { effectiveFrom: '2026-06-01', mode: 'PerOccurrence', priceNet: 40, vatRatePercent: 25.5, durationMinutes: 120 },
      ] }],
  } }));
  await page.goto('/admin/shifts/');
}

test('the work order menu opens the price history table, newest change first', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: /^Actions for Cleaning/ }).click();
  await page.getByRole('menuitem', { name: 'Price history', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: /^Price history — Cleaning/ });
  await expect(dialog).toBeVisible();
  const rows = dialog.getByRole('row');
  await expect(rows.nth(1)).toContainText('2026-06-01');
  await expect(rows.nth(1)).toContainText('Per completed visit');
  await expect(rows.nth(1)).toContainText('€40.00');
  await expect(rows.nth(2)).toContainText('2026-01-01');
  await expect(rows.nth(2)).toContainText('Fixed monthly fee');
});

test('the month and visits dropdowns sit on one row without a horizontal scrollbar', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'Visits', exact: true }).click();
  const month = page.getByRole('dialog', { name: /^Visits of/ }).getByRole('combobox', { name: 'Month', exact: true });
  const shows = page.getByRole('dialog', { name: /^Visits of/ }).getByRole('combobox', { name: 'Show visits', exact: true });
  await expect(shows).toBeVisible();
  const tops = await Promise.all([month, shows].map(el => el.evaluate(node => Math.round(node.getBoundingClientRect().top))));
  expect(tops[0]).toBe(tops[1]);
  const overflow = await page.getByTestId('row-visits').evaluate(el => el.scrollWidth - el.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('a network failure in the visit price dialog reads as a plain sentence, not "Failed to fetch"', async ({ page }) => {
  // Visit price applies to on-call and one-time shifts (or per-visit pricing), so the on-call shift is used here.
  await setup(page, { pricingFails: true, onCall: true });
  await page.getByRole('button', { name: /^Actions for Cleaning/ }).click();
  await page.getByRole('menuitem', { name: 'Visits', exact: true }).click();
  await page.getByRole('button', { name: /^Actions for visit/ }).nth(0).click();
  await page.getByRole('menuitem', { name: 'Visit price', exact: true }).click();
  await expect(page.getByText('Could not reach the server. Check your connection and try again.')).toBeVisible();
  await expect(page.getByText('Failed to fetch', { exact: true })).toHaveCount(0);
});
