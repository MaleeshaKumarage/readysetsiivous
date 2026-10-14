import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

test('agreement lifecycle preserves completed and partial documents independently of signing status', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const items = [
    { id: 'draft', title: 'Active draft', status: 'Draft', isActive: true },
    { id: 'complete', title: 'Active completed', status: 'Completed', isActive: true },
    { id: 'partial', title: 'Inactive partial', status: 'PartiallySigned', isActive: false },
    { id: 'historic', title: 'Inactive completed', status: 'Completed', isActive: false },
  ].map(item => ({ ...item, code: item.id, signerCount: 1, signedCount: item.status === 'Draft' ? 0 : 1 }));
  let listRequests = 0;
  await page.route('**/api/v1/admin/agreements**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/document')) return route.fulfill({ headers, contentType: 'application/pdf', body: '%PDF-1.4\nSigned document\n%%EOF' });
    const item = items.find(item => path.endsWith(`/${item.id}`));
    if (item) return route.fulfill({ headers, json: { ...item, signers: [{ id: 'signer', name: 'Signed cleaner', email: 'signed@example.com', token: 'preserved-token', status: 'Signed' }] } });
    listRequests++;
    return route.fulfill({ headers, json: items });
  });
  await page.goto('/admin/agreements/');
  await expect(page.getByRole('tab', { name: 'Active (2)', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Active completed', { exact: true })).toBeVisible();
  await expect(page.getByText('Inactive completed', { exact: true })).toBeHidden();
  const initialListRequests = listRequests;
  await page.getByRole('tab', { name: 'Inactive (2)', exact: true }).click();
  await expect(page.getByText('Inactive partial', { exact: true })).toBeVisible();
  await expect(page.getByText('Inactive completed', { exact: true })).toBeVisible();
  const historic = page.getByRole('row').filter({ hasText: 'Inactive completed' });
  const download = page.waitForEvent('download');
  await historic.getByRole('button', { name: 'PDF', exact: true }).click();
  expect((await download).suggestedFilename()).toBe('agreement-historic.pdf');
  await historic.getByRole('button', { name: 'Signers' }).click();
  await expect(page.getByRole('dialog').getByText('Signed cleaner', { exact: true })).toBeVisible();
  await expect(page.getByRole('dialog').getByText('Signed', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  const partial = page.getByRole('row').filter({ hasText: 'Inactive partial' });
  const partialDownload = page.waitForEvent('download');
  await partial.getByRole('button', { name: 'PDF', exact: true }).click();
  expect((await partialDownload).suggestedFilename()).toBe('agreement-partial.pdf');
  await partial.getByRole('button', { name: 'Signers' }).click();
  await expect(page.getByRole('dialog').getByText('Signed cleaner', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await page.getByRole('tab', { name: 'Active (2)', exact: true }).click();
  await expect(page.getByText('Active draft', { exact: true })).toBeVisible();
  expect(listRequests).toBe(initialListRequests);
});

test('employees request all lifecycle records and inactive employees offer reactivation without invite actions', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let includeInactive = '';
  let listRequests = 0;
  const employees = [
    { id: 'active', firstName: 'Active', lastName: 'Cleaner', email: 'active@example.com', role: 'employee', isActive: true },
    { id: 'inactive', firstName: 'Retired', lastName: 'Cleaner', email: 'retired@example.com', role: 'employee', isActive: false },
  ];
  await page.route('**/api/v1/admin/employees**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    includeInactive = new URL(route.request().url()).searchParams.get('includeInactive') ?? '';
    listRequests++;
    return route.fulfill({ headers, json: employees });
  });
  await page.goto('/admin/employees/');
  await expect(page.getByText('Active Cleaner', { exact: true })).toBeVisible();
  expect(includeInactive).toBe('true');
  const initialListRequests = listRequests;
  await page.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  const retired = page.getByRole('row').filter({ hasText: 'Retired Cleaner' });
  await expect(retired).toBeVisible();
  await expect(retired.getByRole('button', { name: 'Reactivate' })).toBeVisible();
  await expect(retired.getByRole('button', { name: 'Invite', exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: 'Active (1)', exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: 'Active Cleaner' }).getByRole('button', { name: 'Invite', exact: true })).toBeVisible();
  expect(listRequests).toBe(initialListRequests);
});

test('inactive-only employees and agreements show helpful Active empty states', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/employees**', route => route.fulfill({ headers, json: [{ id: 'old', firstName: 'Old', lastName: 'Cleaner', email: 'old@example.com', role: 'employee', isActive: false }] }));
  await page.route('**/api/v1/admin/agreements**', route => route.fulfill({ headers, json: [{ id: 'old', title: 'Old agreement', code: 'old', status: 'Completed', isActive: false, signerCount: 1, signedCount: 1 }] }));
  await page.goto('/admin/employees/');
  await expect(page.getByText('No active employees. Check the Inactive tab.')).toBeVisible();
  await page.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByText('Old Cleaner', { exact: true })).toBeVisible();
  await page.goto('/admin/agreements/');
  await expect(page.getByText('No active agreements. Check the Inactive tab.')).toBeVisible();
  await page.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByText('Old agreement', { exact: true })).toBeVisible();
});
