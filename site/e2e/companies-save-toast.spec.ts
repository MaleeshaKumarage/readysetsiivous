import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const DUPLICATE = 'A company with this business ID already exists in this tenant. Check the business ID, or open the existing company.';

test('a rejected company save explains the duplicate business ID as a toast and in the dialog, and keeps the entered values', async ({ page }) => {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') return route.fulfill({ status: 409, headers, json: { title: 'Company business ID conflict', detail: DUPLICATE } });
    return route.fulfill({ headers, json: { items: [], total: 0 } });
  });
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Add company' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/Business ID/).fill('1234567-8');
  await dialog.getByLabel(/^Name/).fill('Duplicate Oy');
  await dialog.getByRole('button', { name: /^(Create|Save)/ }).click();
  await expect(page.getByText('Company not saved').first()).toBeVisible();
  await expect(page.getByText(DUPLICATE).first()).toBeVisible();
  await expect(dialog.getByLabel(/Business ID/)).toHaveValue('1234567-8');
});
