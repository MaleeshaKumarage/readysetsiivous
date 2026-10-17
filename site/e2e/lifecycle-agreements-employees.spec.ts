import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

test('legacy agreement preview applies explicit company and version, retaining selection after a conflict', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const agreementId = '10000000-0000-0000-0000-000000000001';
  const companyId = '20000000-0000-0000-0000-000000000001';
  const expectedVersion = '30000000-0000-0000-0000-000000000001';
  const companies = [{ id: companyId, name: 'Fixture company Oy', businessId: '1234567-8', isActive: true }];
  const candidate = { agreementId, title: 'Legacy draft', status: 'Draft', expectedVersion, missingCompanyId: '00000000-0000-0000-0000-000000000000' };
  let repaired = false;
  let conflict = true;
  const requests: unknown[] = [];
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers, json: { items: companies, total: 1 } }));
  await page.route('**/api/v1/admin/agreements**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: repaired ? [] : [candidate], companies } });
    if (path.endsWith(`/${agreementId}/repair-company`)) {
      expect(route.request().method()).toBe('POST');
      requests.push(route.request().postDataJSON());
      if (conflict) return route.fulfill({ status: 409, headers, json: { detail: 'Agreement changed. Review the latest version.' } });
      repaired = true;
      return route.fulfill({ headers, json: { agreementId, companyId } });
    }
    return route.fulfill({ headers, json: [{ id: 'signed', title: 'Signed history', code: 'signed', status: 'Completed', isActive: true, signerCount: 1, signedCount: 1 }] });
  });
  await page.goto('/admin/agreements/');
  await expect(page.getByText('Legacy draft', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Apply reviewed link' })).toBeDisabled();
  const select = page.getByRole('textbox', { name: 'Company for Legacy draft' });
  await select.click();
  await page.getByRole('option', { name: 'Fixture company Oy' }).click();
  await page.getByRole('button', { name: 'Apply reviewed link' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Agreement changed. Review the latest version.' })).toBeVisible();
  await expect(select).toHaveValue('Fixture company Oy');
  expect(requests).toEqual([{ companyId, expectedVersion }]);
  conflict = false;
  await page.getByRole('button', { name: 'Apply reviewed link' }).click();
  await expect(page.getByText('Legacy draft', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Signed history', { exact: true })).toBeVisible();
  expect(requests).toEqual([{ companyId, expectedVersion }, { companyId, expectedVersion }]);
});

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
    if (new URL(route.request().url()).pathname.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: [], companies: [] } });
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
  await page.route('**/api/v1/admin/agreements**', route => route.fulfill({ headers, json: new URL(route.request().url()).pathname.endsWith('/legacy-repairs') ? { agreements: [], companies: [] } : [{ id: 'old', title: 'Old agreement', code: 'old', status: 'Completed', isActive: false, signerCount: 1, signedCount: 1 }] }));
  await page.goto('/admin/employees/');
  await expect(page.getByText('No active employees. Check the Inactive tab.')).toBeVisible();
  await page.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByText('Old Cleaner', { exact: true })).toBeVisible();
  await page.goto('/admin/agreements/');
  await expect(page.getByText('No active agreements. Check the Inactive tab.')).toBeVisible();
  await page.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByText('Old agreement', { exact: true })).toBeVisible();
});

test('inactive agreements can be deleted when unsigned and signed agreements are retained', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let items = [
    { id: 'unsigned', title: 'Inactive unsigned', code: 'unsigned', status: 'Draft', isActive: false, signerCount: 1, signedCount: 0 },
    { id: 'signed', title: 'Inactive partially signed', code: 'signed', status: 'PartiallySigned', isActive: false, signerCount: 2, signedCount: 1 },
  ];
  let deleteCalls = 0;
  let retainOnDelete = true;
  await page.route('**/api/v1/admin/agreements**', async route => {
    if (new URL(route.request().url()).pathname.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: [], companies: [] } });
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'GET') return route.fulfill({ headers, json: items });
    if (route.request().method() === 'DELETE') {
      deleteCalls++;
      if (retainOnDelete) return route.fulfill({ status: 409, headers, json: { detail: 'Agreements with signatures are retained.' } });
      items = items.filter(item => item.id !== 'unsigned');
      return route.fulfill({ status: 204, headers });
    }
    return route.fulfill({ status: 405, headers });
  });

  page.on('dialog', dialog => dialog.accept());
  await page.goto('/admin/agreements/');
  await page.getByRole('tab', { name: 'Inactive (2)', exact: true }).click();
  const signed = page.getByRole('row').filter({ hasText: 'Inactive partially signed' });
  await expect(signed.getByRole('button', { name: 'Delete' })).toBeDisabled();
  await expect(signed.getByRole('button', { name: 'Delete' })).toHaveAttribute('title', 'Agreements with signatures are retained.');

  const unsigned = page.getByRole('row').filter({ hasText: 'Inactive unsigned' });
  await unsigned.getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText('Agreements with signatures are retained.', { exact: true })).toBeVisible();
  expect(deleteCalls).toBe(1);

  retainOnDelete = false;
  await unsigned.getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText('Inactive unsigned', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Inactive partially signed', { exact: true })).toBeVisible();
  expect(deleteCalls).toBe(2);
});
