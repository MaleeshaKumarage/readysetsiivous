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
  await expect(schedule).toHaveValue('Daily');
  // Choosing the selected option again used to clear the picker and then save as Daily without a warning.
  await schedule.click();
  await page.getByRole('option', { name: 'Daily', exact: true }).click();
  await expect(schedule).toHaveValue('Daily');
  await expect(dialog.getByRole('button', { name: 'Clear', exact: true })).toHaveCount(0);
});
