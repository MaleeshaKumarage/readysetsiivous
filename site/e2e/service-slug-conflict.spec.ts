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

test('a refused service update says it was not updated and keeps the edited name and dialog open', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const UPDATE_REFUSED = 'Service could not be saved because another service uses this slug. Change the slug, then save again.';
  const row = { id: 'svc-1', slug: 'deep-clean', category: 'cleaning', name: { values: { fi: 'Syväsiivous', en: 'Deep clean' } }, description: { values: { fi: 'Kuvaus', en: 'Desc' } }, additionalInfo: null, durationMinutes: 60, priceNet: 50, vatRatePercent: 25.5, currency: 'EUR', isActive: true, isFeatured: false, sortOrder: 0, icon: null, imageUrl: null };
  let puts = 0;
  await page.route('**/api/v1/admin/services**', async route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    if (path.endsWith('/services/svc-1') && method === 'PUT') { puts++; return route.fulfill({ status: 409, headers: CORS_HEADERS, json: { title: 'Slug already exists', detail: UPDATE_REFUSED } }); }
    if (method === 'GET') return route.fulfill({ status: 200, json: [row], headers: CORS_HEADERS });
    return route.continue();
  });
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Edit service', exact: true });
  await dialog.getByLabel('Name (Finnish)').fill('Syväsiivous 2');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText('Service not updated').first()).toBeVisible();
  await expect(dialog.getByText(UPDATE_REFUSED)).toBeVisible();
  await expect(dialog.getByLabel('Name (Finnish)')).toHaveValue('Syväsiivous 2');
  expect(puts).toBe(1);
});
