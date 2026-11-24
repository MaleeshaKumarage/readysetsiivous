import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { PartnerIdentity } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const existing: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', isActive: true, street: 'Lake road 1', city: 'Tampere', roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' };
const DUPLICATE = 'A property named "lake apartment" already exists in this company. Choose another name.';

async function fixture(page: Page) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; });
  const posts: string[] = [];
  await page.route(/\/api\/v1\/(?:admin|partner)\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'POST' && path.endsWith('/partner/branches')) { posts.push(path); return route.fulfill({ status: 409, headers, json: { title: 'Property name conflict', detail: DUPLICATE } }); }
    if (req.method() !== 'GET') return route.fulfill({ status: 409, headers, json: { detail: 'Unexpected write in this test.' } });
    const suffix = path.split('/partner')[1];
    const json = path === '/api/v1/partner/me' ? identity
      : path.endsWith('/companies') ? { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 }
      : suffix === '/branches' || suffix === '/quotes' ? [existing]
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ headers, json });
  });
  return { posts };
}

test('partner adding a property with a name already used by their company sees the duplicate message and keeps the entered values', async ({ page }) => {
  const state = await fixture(page);
  await page.goto('/partner/');
  await page.getByRole('button', { name: 'Add property', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add property', exact: true });
  await dialog.getByLabel('Property name', { exact: true }).fill('lake apartment');
  await dialog.getByRole('button', { name: 'Save property', exact: true }).click();

  await expect.poll(() => state.posts.length).toBe(1);
  await expect(page.getByText(DUPLICATE, { exact: true }).first()).toBeVisible();
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Property name', { exact: true })).toHaveValue('lake apartment');
});
