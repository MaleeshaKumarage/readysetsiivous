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
