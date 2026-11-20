import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };
const WARNING = 'Business ID 1234567-8 is already used by customer "Metehe Oy". The lead was saved; check that it is the same business before converting.';

test('admin saves a lead whose business ID is already used: the lead is saved and the warning is shown', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let saves = 0;
  await page.route('**/api/v1/admin/leads**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (method === 'GET' && path.endsWith('/leads')) return route.fulfill({ headers, json: { items: [], total: 0 } });
    if (method === 'POST' && path.endsWith('/leads')) {
      saves++;
      const fields = request.postDataJSON() as Record<string, unknown>;
      return route.fulfill({ status: 201, headers, json: {
        lead: { id: 'lead-1', version: 'v1', fields, isActive: true, status: 'New', imageCount: 0, companyId: null, branchId: null, convertedUtc: null, updatedUtc: '2026-11-18T09:00:00Z' },
        images: [], customerAvailable: false, businessIdWarning: WARNING } });
    }
    return route.fallback();
  });

  await page.goto('/admin/leads/');
  await page.getByRole('button', { name: 'New lead', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company name', exact: true }).fill('Methehe Oy');
  await page.getByRole('textbox', { name: 'Business ID (required for conversion)' }).fill('1234567-8');
  await page.getByRole('button', { name: 'Save lead', exact: true }).click();

  await expect.poll(() => saves).toBe(1);
  await expect(page.getByRole('status').filter({ hasText: 'Lead saved.' })).toContainText(WARNING);
});
