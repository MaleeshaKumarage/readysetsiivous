import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('the schedule picker cannot be cleared, so a shift never gets a schedule nobody chose', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\//, route => {
    const url = route.request().url(); const path = new URL(url).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ headers, json: path.endsWith('/shifts') ? []
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  const schedule = dialog.getByLabel('Schedule', { exact: true });
  // The schedule is an explicit choice: a new shift starts with no schedule selected.
  await expect(schedule).toHaveValue('');
  await dialog.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByText('Choose a schedule type.')).toBeVisible();
  // Pick a schedule, then re-choose it. Re-choosing the selected option used to clear the picker.
  await schedule.click();
  await page.getByRole('option', { name: 'Monthly', exact: true }).click();
  await expect(schedule).toHaveValue('Monthly');
  await schedule.click();
  await page.getByRole('option', { name: 'Monthly', exact: true }).click();
  await expect(schedule).toHaveValue('Monthly');
});
