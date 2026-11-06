import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const REASON = 'This company still has 2 active shift(s). Deactivate or move them first, then deactivate the company.';

test('deactivating a company with active shifts is refused with the reason and the company stays active', async ({ page }) => {
  await setupAdminMocks(page);
  const company = { id: 'company-a', businessId: '1234567-8', name: 'Busy Oy', contactName: '', contactEmail: '', contactPhone: '', notes: '', isActive: true, isPartnerEnabled: false };
  await page.route(/\/api\/v1\/admin\/companies\/company-a\/deactivate$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, status: 409, json: { title: 'Shift conflict', detail: REASON } });
  });
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    return route.fulfill({ headers, json: { items: [company], total: 1 } });
  });
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Deactivate', exact: true }).first().click();
  await expect(page.getByRole('alert').filter({ hasText: REASON }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Deactivate', exact: true }).first()).toBeVisible();
});
