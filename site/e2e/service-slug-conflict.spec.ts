import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS_HEADERS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const DUPLICATE = "Slug 'test-slug' already exists. Change the service name or slug so it is unique, then save again.";

test('a duplicate service slug names the slug and the fix, in the form and as a toast, and keeps the entered values', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/services**', async route => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    if (method === 'GET') return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    if (method === 'POST') return route.fulfill({ status: 409, json: { title: 'Slug already exists', detail: DUPLICATE }, headers: CORS_HEADERS });
    return route.continue();
  });
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Add service' }).click();
  await page.getByLabel('Slug').fill('test-slug');
  await page.getByLabel('Name (Finnish)').fill('Test Name');
  await page.getByRole('button', { name: 'Create service' }).click();
  await expect(page.getByText(DUPLICATE).first()).toBeVisible();
  await expect(page.getByText('Service not created')).toBeVisible();
  await expect(page.getByLabel('Slug')).toHaveValue('test-slug');
  await expect(page.getByLabel('Name (Finnish)')).toHaveValue('Test Name');
});
