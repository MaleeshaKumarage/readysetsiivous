import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };
const oneTime = (name: string, id: string, isCompleted: boolean, isActive = true) => ({
  id, companyId: 'c-1', branchId: 'b-1', name, isActive, isCompleted,
  schedule: { type: 6, oneTimeDate: '2026-11-02T00:00:00', oneTimeStart: '09:00:00', oneTimeEnd: '11:00:00' },
});
const weekly = (name: string, id: string) => ({
  id, companyId: 'c-1', branchId: 'b-1', name, isActive: true, isCompleted: false,
  schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' },
});

async function setup(page: Page, shifts: unknown[]) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ status: 200, headers: CORS, json: { items: [{ id: 'c-1', name: 'CleanTech Oy', businessId: '1111111-1', isActive: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ status: 200, headers: CORS, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (route.request().url().includes('/assignments')) return route.fulfill({ status: 200, headers: CORS, json: [] });
    return route.fulfill({ status: 200, headers: CORS, json: shifts });
  });
}

test('one-time visits with a submitted checklist show under Completed, not Active', async ({ page }) => {
  await setup(page, [oneTime('Done visit', 's-done', true), oneTime('Open visit', 's-open', false), weekly('Weekly team', 's-weekly')]);
  await page.goto('/admin/shifts/');
  const active = page.getByRole('tab', { name: /^Active \(2\)/ });
  await expect(active).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('row').filter({ hasText: 'Done visit' })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Open visit' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Weekly team' })).toBeVisible();
  await page.getByRole('tab', { name: /^Completed \(1\)/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Done visit' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Open visit' })).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Weekly team' })).toHaveCount(0);
});

test('an inactive one-time visit stays under Inactive even if its checklist was submitted', async ({ page }) => {
  await setup(page, [oneTime('Archived visit', 's-archived', true, false)]);
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('tab', { name: /^Completed \(0\)/ })).toBeVisible();
  await page.getByRole('tab', { name: /^Inactive \(1\)/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Archived visit' })).toBeVisible();
});

test('large shift lists page 25 rows at a time', async ({ page }) => {
  await setup(page, Array.from({ length: 30 }, (_, index) => weekly(`Team ${String(index).padStart(2, '0')}`, `s-${index}`)));
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('row').filter({ hasText: 'Team' })).toHaveCount(25);
  await expect(page.getByText('Showing 1–25 of 30', { exact: true })).toBeVisible();
  await page.getByRole('navigation', { name: 'Shifts pages' }).getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Team' })).toHaveCount(5);
  await expect(page.getByText('Page 2 of 2', { exact: true })).toBeVisible();
});
