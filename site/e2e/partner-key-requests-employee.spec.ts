import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const pngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
const photoId = '66666666-6666-4666-8666-666666666666';

test('an assigned employee completes a key request and adds completion photos', async ({ page }) => {
  const item = { id: 'key-7', revision: 4, companyId: '11111111-1111-4111-8111-111111111111', branchId: '22222222-2222-4222-8222-222222222222', branchName: 'Lake apartment', kind: 'Pickup', date: '2026-12-03', notes: 'Lockbox 1234', feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null, completedUtc: null as string | null, assignedEmployeeId: 'employee-a', assignedEmployeeName: 'Test Staff', photos: [] as { id: string; sizeBytes: number; uploadedUtc: string }[] };
  const writes: { path: string; body: Record<string, unknown> | null; multipart: boolean }[] = [];
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ headers, json: route.request().url().includes('/calendar') ? { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-05', toLocalDate: '2026-11-06' } : [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/key-requests\/[^/]+\/photos\/[^/]+$/, route => route.fulfill({ headers, contentType: 'image/png', body: Buffer.from(pngBase64, 'base64') }));
  await page.route(/\/api\/v1\/me\/key-requests\/[^/]+\/complete$/, route => {
    writes.push({ path: new URL(route.request().url()).pathname, body: route.request().postDataJSON(), multipart: false });
    Object.assign(item, { revision: 5, status: 'Completed', completedUtc: '2026-12-03T10:00:00Z' });
    return route.fulfill({ headers, json: item });
  });
  await page.route(/\/api\/v1\/me\/key-requests\/[^/]+\/photos$/, route => {
    writes.push({ path: new URL(route.request().url()).pathname, body: null, multipart: (route.request().headers()['content-type'] ?? '').startsWith('multipart/form-data') });
    item.photos.push({ id: photoId, sizeBytes: 70, uploadedUtc: '2026-12-03T10:05:00Z' });
    return route.fulfill({ headers, json: item });
  });
  await page.route(/\/api\/v1\/me\/key-requests(?:\?.*)?$/, route => route.fulfill({ headers, json: [item] }));

  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Keys', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Key requests' })).toBeVisible();
  await expect(page.getByText('Lockbox 1234')).toBeVisible();

  await page.getByRole('button', { name: 'Mark completed', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Key request marked completed.');
  expect(writes[0]).toMatchObject({ path: '/api/v1/me/key-requests/key-7/complete', body: { revision: 4 } });

  await page.getByLabel('Add photo for Lake apartment 2026-12-03', { exact: true }).setInputFiles({ name: 'keys.png', mimeType: 'image/png', buffer: Buffer.from(pngBase64, 'base64') });
  await expect(page.getByRole('status')).toHaveText('Photo added.');
  expect(writes[1]).toMatchObject({ path: '/api/v1/me/key-requests/key-7/photos', multipart: true });
  await expect(page.getByRole('img', { name: 'Key request photo 1' })).toBeVisible();
});

test('an assigned employee adds a photo during the visit, before marking the key request completed', async ({ page }) => {
  const item = { id: 'key-8', revision: 2, companyId: '11111111-1111-4111-8111-111111111111', branchId: '22222222-2222-4222-8222-222222222222', branchName: 'Lake apartment', kind: 'Handover', date: '2026-12-04', notes: null, feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null, completedUtc: null as string | null, assignedEmployeeId: 'employee-a', assignedEmployeeName: 'Test Staff', photos: [] as { id: string; sizeBytes: number; uploadedUtc: string }[] };
  const writes: { path: string; multipart: boolean }[] = [];
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ headers, json: route.request().url().includes('/calendar') ? { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-05', toLocalDate: '2026-11-06' } : [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/key-requests\/[^/]+\/photos\/[^/]+$/, route => route.fulfill({ headers, contentType: 'image/png', body: Buffer.from(pngBase64, 'base64') }));
  await page.route(/\/api\/v1\/me\/key-requests\/[^/]+\/photos$/, route => {
    writes.push({ path: new URL(route.request().url()).pathname, multipart: (route.request().headers()['content-type'] ?? '').startsWith('multipart/form-data') });
    item.photos.push({ id: photoId, sizeBytes: 70, uploadedUtc: '2026-12-04T09:30:00Z' });
    return route.fulfill({ headers, json: item });
  });
  await page.route(/\/api\/v1\/me\/key-requests(?:\?.*)?$/, route => route.fulfill({ headers, json: [item] }));

  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Keys', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Mark completed', exact: true })).toBeVisible();

  await page.getByLabel('Add photo for Lake apartment 2026-12-04', { exact: true }).setInputFiles({ name: 'handover.png', mimeType: 'image/png', buffer: Buffer.from(pngBase64, 'base64') });
  await expect(page.getByRole('status')).toHaveText('Photo added.');
  expect(writes[0]).toMatchObject({ path: '/api/v1/me/key-requests/key-8/photos', multipart: true });
  await expect(page.getByRole('img', { name: 'Key request photo 1' })).toBeVisible();
  // Still open: completing the request remains a separate step.
  await expect(page.getByRole('button', { name: 'Mark completed', exact: true })).toBeVisible();
});

test('a refused employee photo keeps its reason on screen after the list reloads', async ({ page }) => {
  const item = { id: 'key-9', revision: 2, companyId: '11111111-1111-4111-8111-111111111111', branchId: '22222222-2222-4222-8222-222222222222', branchName: 'Lake apartment', kind: 'Pickup', date: '2026-12-05', notes: null, feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null, completedUtc: null as string | null, assignedEmployeeId: 'employee-a', assignedEmployeeName: 'Test Staff', photos: [] as { id: string; sizeBytes: number; uploadedUtc: string }[] };
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ headers, json: route.request().url().includes('/calendar') ? { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-05', toLocalDate: '2026-11-06' } : [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/key-requests\/[^/]+\/photos$/, route => route.fulfill({ status: 400, headers, json: { title: 'Validation failed', errors: { File: ['Key request photos must be JPEG, PNG or WebP.'] } } }));
  await page.route(/\/api\/v1\/me\/key-requests(?:\?.*)?$/, route => route.fulfill({ headers, json: [item] }));

  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Keys', exact: true }).click();
  await page.getByLabel('Add photo for Lake apartment 2026-12-05', { exact: true }).setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('not an image') });

  // The reload after the refused upload succeeds, but the reason stays visible.
  await expect(page.getByText('Key request photos must be JPEG, PNG or WebP.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Mark completed', exact: true })).toBeVisible();
});
