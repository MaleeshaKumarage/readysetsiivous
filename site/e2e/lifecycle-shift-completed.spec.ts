import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { shiftRowAction, confirmShiftPopup } from './shift-actions';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };
const oneTime = (name: string, id: string, isCompleted: boolean, isActive = true) => ({
  id, companyId: 'c-1', branchId: 'b-1', name, isActive, isCompleted,
  schedule: { type: 6, oneTimeDate: '2026-11-02T00:00:00', oneTimeStart: '09:00:00', oneTimeEnd: '11:00:00' },
});
const weekly = (name: string, id: string) => ({
  id, companyId: 'c-1', branchId: 'b-1', name, isActive: true, isCompleted: false,
  schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' },
});

async function setup(page: Page, shifts: unknown[], options: { listDelay?: Promise<void> } = {}) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ status: 200, headers: CORS, json: { items: [{ id: 'c-1', name: 'CleanTech Oy', businessId: '1111111-1', isActive: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ status: 200, headers: CORS, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (route.request().url().includes('/assignment-counts')) return route.fulfill({ status: 200, headers: CORS, json: {} });
    if (route.request().url().includes('/assignments')) return route.fulfill({ status: 200, headers: CORS, json: [] });
    if (options.listDelay) return options.listDelay.then(() => route.fulfill({ status: 200, headers: CORS, json: shiftsResponse(shifts, route.request().url()) }));
    return route.fulfill({ status: 200, headers: CORS, json: shiftsResponse(shifts, route.request().url()) });
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
  const pager = page.getByRole('navigation', { name: 'Shifts pages' });
  await expect(pager.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
  await pager.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Team' })).toHaveCount(25);
  await expect(page.getByText('Showing 1–25 of 30', { exact: true })).toBeVisible();
  await expect(page.getByText('Page 1 of 2', { exact: true })).toBeVisible();
  await expect(pager.getByRole('button', { name: 'Previous', exact: true })).toBeDisabled();
});

test('short shift lists show no pager', async ({ page }) => {
  await setup(page, [weekly('Weekly team', 's-weekly'), oneTime('Done visit', 's-done', true)]);
  await page.goto('/admin/shifts/');
  await expect(page.getByRole('row').filter({ hasText: 'Weekly team' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Shifts pages' })).toHaveCount(0);
});

test('arrow keys, Home and End move between Active, Completed and Inactive, wrapping at the ends', async ({ page }) => {
  await setup(page, [weekly('Weekly team', 's-weekly'), oneTime('Done visit', 's-done', true), oneTime('Archived visit', 's-archived', false, false)]);
  await page.goto('/admin/shifts/');
  const tab = (name: string) => page.getByRole('tab', { name: new RegExp(`^${name} \\(`) });
  await expect(page.getByRole('row').filter({ hasText: 'Weekly team' })).toBeVisible();
  await tab('Active').focus();
  await page.keyboard.press('ArrowRight');
  await expect(tab('Completed')).toHaveAttribute('aria-selected', 'true'); await expect(tab('Completed')).toBeFocused();
  await expect(page.getByRole('row').filter({ hasText: 'Done visit' })).toBeVisible();
  await page.keyboard.press('ArrowRight');
  await expect(tab('Inactive')).toHaveAttribute('aria-selected', 'true'); await expect(tab('Inactive')).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(tab('Active')).toHaveAttribute('aria-selected', 'true'); await expect(tab('Active')).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(tab('Inactive')).toHaveAttribute('aria-selected', 'true'); await expect(tab('Inactive')).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(tab('Completed')).toHaveAttribute('aria-selected', 'true');
  await page.keyboard.press('Home');
  await expect(tab('Active')).toHaveAttribute('aria-selected', 'true'); await expect(tab('Active')).toBeFocused();
  await page.keyboard.press('End');
  await expect(tab('Inactive')).toHaveAttribute('aria-selected', 'true'); await expect(tab('Inactive')).toBeFocused();
  await expect(page.getByRole('row').filter({ hasText: 'Archived visit' })).toBeVisible();
});

test('the Completed tab is present while the shift list is still loading', async ({ page }) => {
  let release!: () => void;
  const listDelay = new Promise<void>(resolve => { release = resolve; });
  await setup(page, [oneTime('Done visit', 's-done', true)], { listDelay });
  await page.goto('/admin/shifts/');
  // Counts are unknown while loading, but all three tabs are already shown.
  await expect(page.getByRole('tab', { name: 'Completed', exact: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Active', exact: true })).toHaveAttribute('aria-selected', 'true');
  release();
  await expect(page.getByRole('tab', { name: /^Completed \(1\)/ })).toBeVisible();
});

test('the shift page is clamped when the list shrinks, so later growth does not jump to a stale page', async ({ page }) => {
  let shifts = Array.from({ length: 26 }, (_, index) => weekly(`Team ${String(index).padStart(2, '0')}`, `s-${index}`));
  let deactivations = 0;
  await setup(page, []);
  // Registered after setup, so this stateful handler wins for the shift endpoints.
  await page.route(/\/api\/v1\/admin\/shifts/, route => {
    const request = route.request(), url = request.url();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (url.includes('/assignment-counts')) return route.fulfill({ status: 200, headers: CORS, json: {} });
    const off = /\/shifts\/([^/]+)\/deactivate/.exec(url);
    if (off) {
      deactivations++;
      shifts = shifts.map(shift => shift.id === off[1] ? { ...shift, isActive: false } : shift);
      // On the second change another admin has meanwhile added six shifts.
      if (deactivations === 2) shifts = [...shifts, ...Array.from({ length: 6 }, (_, index) => weekly(`Extra ${index}`, `x-${index}`))];
      return route.fulfill({ status: 204, headers: CORS });
    }
    return route.fulfill({ status: 200, headers: CORS, json: shiftsResponse(shifts, route.request().url()) });
  });
  await page.goto('/admin/shifts/');
  const pager = page.getByRole('navigation', { name: 'Shifts pages' });
  await pager.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText('Page 2 of 2', { exact: true })).toBeVisible();
  // Deactivating the only row on page 2 leaves 25 active shifts: one page, no pager.
  await shiftRowAction(page, page.getByRole('row').filter({ hasText: 'Team 25' }), 'Deactivate'); await confirmShiftPopup(page, 'Deactivate');
  await expect(pager).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: 'Team' })).toHaveCount(25);
  // The list grows past one page again; the user stays on page 1 instead of jumping to the old page 2.
  await shiftRowAction(page, page.getByRole('row').filter({ hasText: 'Team 00' }), 'Deactivate'); await confirmShiftPopup(page, 'Deactivate');
  await expect(page.getByText('Showing 1–25 of 30', { exact: true })).toBeVisible();
  await expect(page.getByText('Page 1 of 2', { exact: true })).toBeVisible();
});

test('switching to Completed and back to Active returns the list to page 1', async ({ page }) => {
  await setup(page, [...Array.from({ length: 30 }, (_, index) => weekly(`Team ${String(index).padStart(2, '0')}`, `s-${index}`)), oneTime('Done visit', 's-done', true)]);
  await page.goto('/admin/shifts/');
  await page.getByRole('navigation', { name: 'Shifts pages' }).getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.getByText('Page 2 of 2', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: /^Completed \(1\)/ }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Done visit' })).toBeVisible();
  await page.getByRole('tab', { name: /^Active \(30\)/ }).click();
  await expect(page.getByRole('tab', { name: /^Active \(30\)/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Page 1 of 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Team 00' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Team' })).toHaveCount(25);
});
