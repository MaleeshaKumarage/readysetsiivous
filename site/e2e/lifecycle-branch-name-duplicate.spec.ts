import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const company = { id: 'company', name: 'AAA', businessId: '1234567-8', isActive: true, isPartnerEnabled: false, billingEmail: null, billingStreet: null, billingPostalCode: null, billingCity: null, billingCountry: null, invoiceNumberPrefix: null, paymentTermDays: 14 };
const existing = { id: 'lake', companyId: 'company', name: 'Lake Apartment', street: '', postalCode: '', city: '', country: 'FI', isActive: true };
const DUPLICATE = 'A property named "lake apartment" already exists in this company. Choose another name.';

test('admin adding a property with a name already used in the company sees the duplicate message and keeps the entered values', async ({ page }) => {
  await setupAdminMocks(page);
  let posts = 0;
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'POST' && path.endsWith('/branches')) { posts++; return route.fulfill({ status: 409, headers, json: { title: 'Property name conflict', detail: DUPLICATE } }); }
    const json = path.endsWith('/companies/company') ? { company, branches: [existing] }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });

  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Add branch' });
  await modal.getByRole('textbox', { name: 'Name', exact: true }).fill('lake apartment');
  await modal.getByRole('button', { name: 'Add branch', exact: true }).click();

  await expect.poll(() => posts).toBe(1);
  await expect(modal.getByRole('alert')).toHaveText(DUPLICATE);
  await expect(modal).toBeVisible();
  await expect(modal.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('lake apartment');
});
