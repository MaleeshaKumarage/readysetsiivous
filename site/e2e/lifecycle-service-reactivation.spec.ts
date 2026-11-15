import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

test('service reactivation settles failed request and uses status-only commands', async ({ page }) => {
  await setupAdminMocks(page);
  let active = false;
  let attempts = 0;
  const service = { id: '11111111-1111-4111-8111-111111111111', slug: 'restore-service', name: { values: { fi: 'Retained service' } },
    description: { values: { fi: 'Description' } }, additionalInfo: { values: { fi: 'Instructions' } },
    currency: 'SEK', imageUrl: '/uploads/retained.png', priceNet: 20, durationMinutes: 75 };
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
  await page.route(/\/api\/v1\/admin\/services/, route => {
    const request = route.request();
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (request.method() === 'POST') {
      expect(request.postData()).toBeNull();
      if (request.url().endsWith('/reactivate')) {
        attempts++;
        if (attempts === 1) return route.fulfill({ status: 500, headers });
        active = true;
      } else {
        expect(request.url()).toMatch(/\/deactivate$/);
        active = false;
      }
      return route.fulfill({ status: 204, headers });
    }
    expect(request.method()).toBe('GET');
    return route.fulfill({ status: 200, headers, json: [{ ...service, isActive: active }] });
  });
  await page.goto('/admin/services/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('cell', { name: 'Retained service' })).toBeVisible();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toHaveText('Could not reactivate service. Please retry.');
  await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Retained service' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('cell', { name: 'Retained service' })).toBeVisible();
  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Retained service' })).toHaveCount(0);
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('cell', { name: 'Retained service' })).toBeVisible();
  expect(attempts).toBe(2);
});
