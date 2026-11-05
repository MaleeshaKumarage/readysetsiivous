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
  // dispatchEvent keeps focus in the price field, so the value is not clamped to 0 on blur before the submit runs.
  await page.getByRole('button', { name: 'Create service' }).dispatchEvent('click');
  await expect(page.getByText('Price cannot be negative.').first()).toBeVisible();
  await expect(page.getByLabel('Slug')).toHaveValue('negative-price');
  // Give any stray request a moment to appear before asserting none was sent.
  await page.waitForTimeout(300);
  expect(writes).toEqual([]);
});
