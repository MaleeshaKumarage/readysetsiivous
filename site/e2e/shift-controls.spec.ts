import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';

// Work orders controls that were not covered before: property search, the company and type filters,
// the Export notice, the Visits toggle, and the Show visits dropdown.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const shifts = [
  { id: 'morning', companyId: 'c-1', branchId: 'b-1', name: 'Morning clean', isActive: true, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } },
  { id: 'evening', companyId: 'c-2', branchId: 'b-2', name: 'Evening clean', isActive: true, schedule: { type: 3, biWeeklyDay: 1, biWeeklyStart: '18:00', biWeeklyEnd: '20:00' } },
];
const day = (d: number) => new Date(Date.UTC(new Date().getUTCFullYear(), new Date().getUTCMonth(), d)).toISOString().slice(0, 10);
const visit = (n: number, extra: object) => ({ startUtc: `${day(2 + n)}T08:00:00Z`, endUtc: `${day(2 + n)}T10:00:00Z`, status: 1, visitNumber: n, visitCleaningType: 'Normal', assigneeIds: ['e1'], workStatus: 'Scheduled', isCancelledManualVisit: false, canAssign: true, ...extra });

async function setup(page: Page) {
  await setupAdminMocks(page);
  const pageUrls: string[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/morning\/occurrences/, route => route.fulfill({ headers, json: [
    visit(1, { workStatus: 'Scheduled' }),
    visit(2, { workStatus: 'InProgress' }),
    visit(3, { workStatus: 'Completed' }),
    visit(4, { isCancelledManualVisit: true, status: 3, exceptionId: 'c4' }),
    visit(5, { assigneeIds: [] }),
  ] }));
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    pageUrls.push(route.request().url());
    return route.fulfill({ headers, json: shiftsResponse(shifts, route.request().url()) });
  });
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: 'c-1', name: 'Alpha Oy', isActive: true }, { id: 'c-2', name: 'Beta Oy', isActive: true }], total: 2 } }));
  await page.goto('/admin/shifts/');
  return { pageUrls };
}

test('property search narrows the list by name and clearing it restores every work order', async ({ page }) => {
  await setup(page);
  const search = page.getByRole('textbox', { name: 'Search work orders', exact: true });
  await search.fill('Evening');
  await expect(page.getByRole('row').filter({ hasText: 'Evening clean' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Morning clean' })).toHaveCount(0);
  await search.fill('no such property');
  await expect(page.getByText('No work orders match this view.', { exact: true })).toBeVisible();
  await search.fill('');
  await expect(page.getByRole('row').filter({ hasText: 'Morning clean' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Evening clean' })).toBeVisible();
});

test('the company and type filters send their choice to the server', async ({ page }) => {
  const { pageUrls } = await setup(page);
  await page.getByRole('textbox', { name: 'Filter by company', exact: true }).click();
  await page.getByRole('option', { name: 'Beta Oy', exact: true }).click();
  await expect.poll(() => pageUrls.some(url => url.includes('companyId=c-2'))).toBe(true);
  await page.getByRole('textbox', { name: 'Filter by type', exact: true }).click();
  await page.getByRole('option', { name: 'Daily', exact: true }).click();
  await expect.poll(() => pageUrls.some(url => url.includes('scheduleType=0'))).toBe(true);
});

test('Export downloads the work orders on this page as a CSV file', async ({ page }) => {
  await setup(page);
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Export', exact: true }).click();
  const file = await download;
  expect(file.suggestedFilename()).toBe('work-orders-active.csv');
  const path = await file.path();
  const text = require('fs').readFileSync(path, 'utf8') as string;
  expect(text.split('\n')[0]).toBe('Property,Address,Company,Schedule,Tab');
  expect(text).toContain('"Morning clean"');
  expect(text).toContain('"Evening clean"');
  await expect(page.getByRole('status')).toHaveText('Exported 2 work orders.');
});

test('the Visits button opens the visits panel and a second press closes it', async ({ page }) => {
  await setup(page);
  const button = page.getByRole('row').filter({ hasText: 'Morning clean' }).getByRole('button', { name: 'Visits', exact: true });
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  await expect(page.getByRole('dialog', { name: 'Visits of Morning clean', exact: true })).toBeVisible();
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  // The open panel covers the list, so it is closed from its own × button.
  await page.getByRole('button', { name: 'Close visits', exact: true }).click();
  await expect(page.getByRole('dialog', { name: /^Visits of/ })).toHaveCount(0);
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await button.click();
  await expect(page.getByRole('dialog', { name: 'Visits of Morning clean', exact: true })).toBeVisible();
});

test('the Show visits dropdown narrows the visits to one status or to visits with no cleaner', async ({ page }) => {
  await setup(page);
  await page.getByRole('row').filter({ hasText: 'Morning clean' }).getByRole('button', { name: 'Visits', exact: true }).click();
  const panel = page.getByRole('dialog', { name: 'Visits of Morning clean', exact: true });
  const show = panel.getByRole('combobox', { name: 'Show visits', exact: true });
  const visitRows = panel.getByRole('row').filter({ hasNot: page.getByRole('columnheader') });
  await expect(show).toHaveValue('all');
  await expect(visitRows).toHaveCount(5);
  await show.selectOption('ongoing');
  await expect(visitRows).toHaveCount(1);
  await expect(visitRows.first()).toContainText('In progress');
  await show.selectOption('completed');
  await expect(visitRows).toHaveCount(1);
  await show.selectOption('cancelled');
  await expect(visitRows).toHaveCount(1);
  await show.selectOption('noclean');
  await expect(visitRows).toHaveCount(1);
  await show.selectOption('scheduled');
  await expect(visitRows).toHaveCount(2);
  await show.selectOption('all');
  await expect(visitRows).toHaveCount(5);
});
