import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': '*' };
const ACTIVATE_REFUSED = 'This agreement was cancelled, so it cannot be reactivated. Create a new agreement instead.';
const DELETE_REFUSED = 'This agreement still has signers, so it cannot be deleted. Keep it for the record, or cancel it first.';
const INACTIVE = { id: 'agr-1', title: 'Old contract', code: 'AG-1', status: 'Inactive', isActive: false, signerCount: 1, signedCount: 0, createdUtc: '2026-10-01T00:00:00Z', companyId: 'company', companyName: 'Fixture Oy' };

async function setup(page: import('@playwright/test').Page) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers, json: { items: [{ id: 'company', name: 'Fixture Oy', isActive: true }], total: 1 } }));
  const calls: string[] = [];
  await page.route('**/api/v1/admin/agreements**', route => {
    const request = route.request(); const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (path.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: [], companies: [] } });
    if (path.endsWith('/activate')) { calls.push('activate'); return route.fulfill({ headers, status: 409, json: { title: 'Agreement conflict', detail: ACTIVATE_REFUSED } }); }
    if (request.method() === 'DELETE') { calls.push('delete'); return route.fulfill({ headers, status: 409, json: { title: 'Agreement conflict', detail: DELETE_REFUSED } }); }
    return route.fulfill({ headers, json: [INACTIVE] });
  });
  return calls;
}

test('a refused reactivation explains the next step as a toast and keeps the agreement listed', async ({ page }) => {
  const calls = await setup(page);
  await page.goto('/admin/agreements/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByText('Agreement activity not changed').first()).toBeVisible();
  await expect(page.getByText(ACTIVATE_REFUSED).first()).toBeVisible();
  await expect(page.getByText('Old contract').first()).toBeVisible();
  expect(calls).toEqual(['activate']);
});

test('a refused delete explains the next step as a toast and keeps the agreement listed', async ({ page }) => {
  const calls = await setup(page);
  page.once('dialog', dialog => dialog.accept());
  await page.goto('/admin/agreements/');
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByText('Agreement not deleted').first()).toBeVisible();
  await expect(page.getByText(DELETE_REFUSED).first()).toBeVisible();
  await expect(page.getByText('Old contract').first()).toBeVisible();
  expect(calls).toEqual(['delete']);
});
