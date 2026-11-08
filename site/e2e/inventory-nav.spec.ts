import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

test('admin reaches Inventory from the Stock navigation and the page renders', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/inventory\/locations(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/inventory\/items(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.goto('/admin/');

  // On phones the navigation is behind the menu button; on desktop the sidebar is always shown.
  if (test.info().project.name === 'mobile-touch') await page.getByRole('button', { name: 'Toggle navigation' }).click();
  const link = page.getByRole('link', { name: 'Inventory', exact: true }).first();
  await expect(link).toBeVisible();
  await link.click();

  await expect(page).toHaveURL(/\/admin\/inventory\/?$/);
  await expect(page.getByRole('heading', { name: 'Inventory', level: 2 })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Take log', exact: true })).toBeVisible();
});

