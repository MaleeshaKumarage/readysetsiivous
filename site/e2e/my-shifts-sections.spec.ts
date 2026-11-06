import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

async function setup(page: Page) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({
    headers,
    json: route.request().url().includes('/calendar')
      ? { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-05', toLocalDate: '2026-11-06' }
      : [],
  }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
}

test('schedules, offers and settings each show only their own content', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');

  const schedules = page.getByRole('tab', { name: 'Schedules', exact: true });
  const offers = page.getByRole('tab', { name: 'Offers', exact: true });
  const settings = page.getByRole('tab', { name: 'Settings', exact: true });
  await expect(schedules).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('button', { name: 'Inventory', exact: true })).toBeVisible();
  await expect(page.getByText('Automatic acceptance is off.')).toHaveCount(0);

  await offers.click();
  await expect(offers).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('No pending cleaning offers.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Inventory', exact: true })).toHaveCount(0);

  await settings.click();
  await expect(settings).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Automatic acceptance is off.')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Change work preferences in My account' })).toBeVisible();
  await expect(page.getByText('No pending cleaning offers.')).toHaveCount(0);
});

test('arrow keys move between sections and focus follows', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Schedules', exact: true }).focus();

  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Offers', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tab', { name: 'Offers', exact: true })).toBeFocused();

  await page.keyboard.press('End');
  await expect(page.getByRole('tab', { name: 'Settings', exact: true })).toHaveAttribute('aria-selected', 'true');

  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Schedules', exact: true })).toHaveAttribute('aria-selected', 'true');
});

test('inventory opens in a modal from Schedules and closes with Escape', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');

  await page.getByRole('button', { name: 'Inventory', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('heading', { name: 'Log an item taken' })).toBeVisible();

  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});
