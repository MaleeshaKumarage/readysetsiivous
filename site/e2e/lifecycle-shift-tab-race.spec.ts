import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('a slow earlier tab response cannot replace the tab chosen last', async ({ page }) => {
  await setupAdminMocks(page);
  let releaseCompleted!: () => void;
  const completedAnswered = new Promise<void>(resolve => { releaseCompleted = resolve; });
  // The Completed tab answers slowly; the Active tab answers at once. Each reply names its tab.
  await page.route(/\/api\/v1\/admin\/shifts\/page/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const tab = new URL(route.request().url()).searchParams.get('tab');
    if (tab === 'completed') await new Promise(resolve => setTimeout(resolve, 1500));
    const isCompleted = tab === 'completed';
    const item = { id: `${tab}-shift`, companyId: 'company', branchId: 'branch', name: isCompleted ? 'Slow completed visit' : 'Fast active visit',
      isActive: true, isCompleted, validFrom: '2026-10-01', schedule: { type: 3 } };
    await route.fulfill({ headers, json: { items: [item], total: 1, counts: { active: 1, completed: 1, inactive: 0 } } });
    if (isCompleted) releaseCompleted();
  });
  await page.goto('/admin/shifts/');
  await expect(page.getByText('Fast active visit')).toBeVisible();
  await page.getByRole('tab', { name: /^Completed/ }).click();
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('tab', { name: /^Active/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Fast active visit')).toBeVisible();
  // Wait until the slow Completed reply has arrived, then check that it did not replace the Active rows.
  await completedAnswered;
  await expect(page.getByText('Slow completed visit')).toHaveCount(0);
  await expect(page.getByText('Fast active visit')).toBeVisible();
});

test('a workspace shift that leaves the current page stays open and usable', async ({ page }) => {
  await setupAdminMocks(page);
  const moved = { id: 'moved', companyId: 'company', branchId: 'branch', name: 'Moved visit', isActive: true, isCompleted: false, validFrom: '2026-10-01', schedule: { type: 3 } };
  let fetched = 0;
  // The first Active page holds the shift; after switching to Completed it is no longer on any page of the list.
  await page.route(/\/api\/v1\/admin\/shifts\/page/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const tab = new URL(route.request().url()).searchParams.get('tab');
    const items = tab === 'completed'
      ? [{ id: 'done', companyId: 'company', branchId: 'branch', name: 'Done visit', isActive: true, isCompleted: true, validFrom: '2026-10-01', schedule: { type: 3 } }]
      : [moved];
    return route.fulfill({ headers, json: { items, total: items.length, counts: { active: 1, completed: 1, inactive: 0 } } });
  });
  await page.route(/\/api\/v1\/admin\/shifts\/moved$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    fetched += 1;
    return route.fulfill({ headers, json: moved });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('row').filter({ hasText: 'Moved visit' }).getByRole('button', { name: 'Manage', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Manage — Moved visit', exact: true });
  await expect(dialog).toBeVisible();
  // The open workspace is a modal over the tabs, so the tab change is dispatched to the tab itself.
  await page.getByRole('tab', { name: /^Completed/ }).dispatchEvent('click');
  // The open workspace keeps its shift: it is fetched directly rather than dropped with the old page.
  await expect(page.getByRole('row').filter({ hasText: 'Moved visit' })).toHaveCount(0);
  await expect(dialog).toBeVisible();
  await expect.poll(() => fetched).toBeGreaterThan(0);
});
