import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS_HEADERS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

test('a negative price is blocked in the form and no request is sent', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const writes: string[] = [];
  await page.route('**/api/v1/admin/services**', async route => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    if (method === 'GET') return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    writes.push(method);
    return route.fulfill({ status: 200, json: {}, headers: CORS_HEADERS });
  });
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Add service' }).click();
  await page.getByLabel('Slug').fill('negative-price');
  await page.getByLabel('Name (Finnish)').fill('Negative price');
  await page.getByLabel('Price (€)').fill('-5');
  // A normal click blurs the price field first; the field no longer clamps -5 to 0, so the real user flow reaches the check.
  await page.getByRole('button', { name: 'Create service' }).click();
  await expect(page.getByLabel('Price (€)')).toHaveValue('-5');
  await expect(page.getByText('Price cannot be negative.').first()).toBeVisible();
  await expect(page.getByLabel('Slug')).toHaveValue('negative-price');
  // Give any stray request a moment to appear before asserting none was sent.
  await page.waitForTimeout(300);
  expect(writes).toEqual([]);
});
