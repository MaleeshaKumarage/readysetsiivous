import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const company = { id: 'company', name: 'AAA', businessId: '1234567-8', isActive: true, isPartnerEnabled: false, billingEmail: null, billingStreet: null, billingPostalCode: null, billingCity: null, billingCountry: null, invoiceNumberPrefix: null, paymentTermDays: 14 };

test('admin adds a property with an area, and the area is saved on the property', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = []; let branches: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'POST' && path.endsWith('/branches')) {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      const saved = { id: 'branch', companyId: 'company', isActive: true, ...body }; branches = [saved]; return route.fulfill({ headers, json: saved });
    }
    return route.fulfill({ headers, json: path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });

  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Add branch' });
  await modal.getByRole('textbox', { name: 'Name', exact: true }).fill('Imatra apartment');
  await modal.getByRole('textbox', { name: 'City', exact: true }).fill('Imatra');
  await modal.getByLabel('Area m² (optional)').fill('45');
  await modal.getByRole('button', { name: 'Add branch', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ name: 'Imatra apartment', areaSquareMeters: 45 });
});
