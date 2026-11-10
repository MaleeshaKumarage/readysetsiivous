import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { partnerCopy, partnerStatus, type PartnerLanguage } from '../src/lib/partnerLocale';
import type { Branch } from '../src/lib/adminApi';
import type { PartnerIdentity, KeyRequest } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', isActive: true, street: 'Lake road 1', city: 'Tampere', roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' };

// Stateful fake of the key request endpoints, so a created or cancelled request shows up on the next load.
async function fixture(page: Page, language: PartnerLanguage, seeded: KeyRequest[] = []) {
  await setupAdminMocks(page);
  await page.addInitScript(value => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; localStorage.setItem('readyset:partner-language', value); }, language);
  const items = [...seeded];
  const writes: { method: string; path: string; body: Record<string, unknown> | null }[] = [];
  await page.route(/\/api\/v1\/(?:admin|partner)\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path === '/api/v1/partner/key-requests' && req.method() === 'GET') return route.fulfill({ headers, json: { feeNet: 15, vatRatePercent: 25.5, items } });
    if (path === '/api/v1/partner/key-requests' && req.method() === 'POST') {
      const body = req.postDataJSON() as Record<string, unknown>; writes.push({ method: 'POST', path, body });
      const created: KeyRequest = { id: `key-${items.length + 1}`, revision: 1, companyId, branchId: String(body.branchId), branchName: branch.name, kind: String(body.kind), date: String(body.date), notes: (body.notes as string | null) ?? null, feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null };
      items.unshift(created);
      return route.fulfill({ headers, json: created });
    }
    if (req.method() === 'POST' && /\/api\/v1\/partner\/key-requests\/[^/]+\/cancel$/.test(path)) {
      const body = req.postDataJSON() as Record<string, unknown>; writes.push({ method: 'POST', path, body });
      const id = path.split('/')[5], item = items.find(x => x.id === id)!;
      Object.assign(item, { status: 'Cancelled', cancellationReason: body.reason, revision: item.revision + 1 });
      return route.fulfill({ headers, json: item });
    }
    if (req.method() === 'GET' && /\/key-requests\/[^/]+\/photos\/[^/]+$/.test(path)) return route.fulfill({ headers, contentType: 'image/png', body: Buffer.from(pngBase64, 'base64') });
    if (req.method() === 'GET') {
      const json = path === '/api/v1/partner/me' ? identity
        : path.endsWith('/companies') ? { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 }
        : path.endsWith('/branches') || path.endsWith('/quotes') ? [branch]
        : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
      return route.fulfill({ headers, json });
    }
    return route.fulfill({ status: 409, headers, json: { detail: 'Unexpected mutation.' } });
  });
  return { writes };
}

for (const language of ['en', 'fi'] as const) {
  const copy = partnerCopy[language];

  test(`a partner sends a key pickup request after seeing the fee in ${language}`, async ({ page }) => {
    const { writes } = await fixture(page, language);
    await page.goto('/partner/');
    await page.getByRole('tab', { name: copy.keys, exact: true }).click();

    await expect(page.getByText(/15[.,]00/).first()).toBeVisible();
    await page.getByLabel(copy.keyDate, { exact: true }).fill('2026-12-01');
    await page.getByLabel(copy.keyNotes, { exact: true }).fill('Lockbox 1234');
    await page.getByRole('button', { name: copy.submitKeyRequest, exact: true }).click();

    await expect(page.getByRole('status')).toHaveText(copy.keySaved);
    expect(writes).toHaveLength(1);
    expect(writes[0].body).toMatchObject({ branchId: branch.id, kind: 'Pickup', date: '2026-12-01', notes: 'Lockbox 1234' });
    expect(writes[0].body?.clientRequestId).toMatch(/^[0-9a-f-]{36}$/);
    await expect(page.getByRole('article', { name: branch.name + ' 2026-12-01' })).toBeVisible();
  });

  test(`a partner cancels a requested key handover with a reason in ${language}`, async ({ page }) => {
    const seeded: KeyRequest = { id: 'key-a', revision: 3, companyId, branchId: branch.id, branchName: branch.name, kind: 'Handover', date: '2026-12-05', notes: null, feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null };
    const { writes } = await fixture(page, language, [seeded]);
    await page.goto('/partner/');
    await page.getByRole('tab', { name: copy.keys, exact: true }).click();

    await page.getByRole('article', { name: branch.name + ' 2026-12-05' }).getByRole('button', { name: copy.cancelKeyRequest, exact: true }).click();
    await page.getByLabel(copy.keyCancelReason, { exact: true }).fill('Keys already collected');
    await page.getByRole('button', { name: copy.confirmKeyCancel, exact: true }).click();

    await expect(page.getByRole('status')).toHaveText(copy.keyCancelled);
    expect(writes).toEqual([{ method: 'POST', path: '/api/v1/partner/key-requests/key-a/cancel', body: { version: 3, reason: 'Keys already collected' } }]);
    await expect(page.getByRole('article', { name: branch.name + ' 2026-12-05' }).getByText(partnerStatus('Cancelled', language))).toBeVisible();
  });
}

test('staff save the key request fee from Partner operations', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = [];
  // The stored fee changes on save, so the reload shows what the server now holds.
  let storedFee = 15;
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path === '/key-requests' && req.method() === 'GET') return route.fulfill({ headers, json: { feeNet: storedFee, vatRatePercent: 25.5, items: [] } });
    if (path === '/key-request-fee' && req.method() === 'PUT') { const body = req.postDataJSON() as { feeNet: number }; writes.push(body); storedFee = body.feeNet; return route.fulfill({ headers, json: { feeNet: storedFee, vatRatePercent: 25.5, items: [] } }); }
    return route.fulfill({ headers, json: path === '/branches' ? [branch] : [] });
  });

  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Key requests', exact: true }).click();
  await expect(page.getByText('Current fee 15.00 EUR excl. VAT; VAT 25.5%.')).toBeVisible();
  await page.getByLabel('Fee per key request, excl. VAT (EUR)', { exact: true }).fill('22.5');
  await page.getByRole('button', { name: 'Save fee', exact: true }).click();

  await expect(page.getByRole('status')).toHaveText('Key request fee saved. New requests use this fee.');
  await expect(page.getByText('Current fee 22.50 EUR excl. VAT; VAT 25.5%.')).toBeVisible();
  expect(writes).toEqual([{ feeNet: 22.5 }]);
});

const pngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
const photoId = '44444444-4444-4444-8444-444444444444';

test('staff assign a cleaner, complete a key request and attach a completion photo', async ({ page }) => {
  await setupAdminMocks(page);
  const cleaner = { id: 'emp-1', firstName: 'Aino', lastName: 'Virtanen', isActive: true, role: 'employee' };
  const retired = { id: 'emp-2', firstName: 'Old', lastName: 'Worker', isActive: false, role: 'employee' };
  const item = { id: 'key-9', revision: 1, companyId, branchId: branch.id, branchName: branch.name, kind: 'Pickup', date: '2026-12-01', notes: null, feeNet: 15, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null, completedUtc: null as string | null, assignedEmployeeId: null as string | null, assignedEmployeeName: null as string | null, photos: [] as { id: string; sizeBytes: number; uploadedUtc: string }[] };
  const writes: { path: string; body: Record<string, unknown> | null; multipart: boolean; file?: boolean }[] = [];
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [cleaner, retired] }));
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path === '/key-requests' && req.method() === 'GET') return route.fulfill({ headers, json: { feeNet: 15, vatRatePercent: 25.5, items: [item] } });
    if (path === `/key-requests/${item.id}/assignment`) {
      writes.push({ path, body: req.postDataJSON(), multipart: false });
      Object.assign(item, { revision: 2, assignedEmployeeId: cleaner.id, assignedEmployeeName: 'Aino Virtanen' });
      return route.fulfill({ headers, json: item });
    }
    if (path === `/key-requests/${item.id}/complete`) {
      writes.push({ path, body: req.postDataJSON(), multipart: false });
      Object.assign(item, { revision: 3, status: 'Completed', completedUtc: '2026-12-01T10:00:00Z' });
      return route.fulfill({ headers, json: item });
    }
    if (path === `/key-requests/${item.id}/photos` && req.method() === 'POST') {
      writes.push({ path, body: null, multipart: (req.headers()['content-type'] ?? '').startsWith('multipart/form-data'), file: (req.postDataBuffer()?.toString('latin1') ?? '').includes('filename="keys.png"') });
      item.photos.push({ id: photoId, sizeBytes: 70, uploadedUtc: '2026-12-01T10:05:00Z' });
      return route.fulfill({ headers, json: item });
    }
    if (path === `/key-requests/${item.id}/photos/${photoId}`) return route.fulfill({ headers, contentType: 'image/png', body: Buffer.from(pngBase64, 'base64') });
    return route.fulfill({ headers, json: path === '/branches' ? [branch] : [] });
  });

  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Key requests', exact: true }).click();
  // Retired staff are not offered for assignment.
  const picker = page.getByRole('combobox', { name: 'Collected or handed over by', exact: true });
  await expect(picker.locator('option', { hasText: 'Old Worker' })).toHaveCount(0);

  await picker.selectOption(cleaner.id);
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Employee assigned.');
  expect(writes[0]).toMatchObject({ path: `/key-requests/${item.id}/assignment`, body: { revision: 1, employeeId: cleaner.id } });

  await page.getByRole('button', { name: 'Mark completed', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Key request marked completed.');
  expect(writes[1]).toMatchObject({ path: `/key-requests/${item.id}/complete`, body: { revision: 2 } });

  await page.getByLabel(`Add photo for ${branch.name} 2026-12-01`, { exact: true }).setInputFiles({ name: 'keys.png', mimeType: 'image/png', buffer: Buffer.from(pngBase64, 'base64') });
  await expect(page.getByRole('status')).toHaveText('Photo added.');
  expect(writes[2]).toMatchObject({ path: `/key-requests/${item.id}/photos`, multipart: true, file: true });
  await expect(page.getByRole('img', { name: /Key request photo/ })).toBeVisible();
});

for (const language of ['en', 'fi'] as const) {
  const copy = partnerCopy[language];
  test(`a partner sees the completion photos of a finished key request in ${language}`, async ({ page }) => {
    const completed: KeyRequest = { id: 'key-c', revision: 4, companyId, branchId: branch.id, branchName: branch.name, kind: 'Pickup', date: '2026-12-03', notes: null, feeNet: 15, vatRatePercent: 25.5, status: 'Completed', cancellationReason: null, completedUtc: '2026-12-03T10:00:00Z', assignedEmployeeId: 'emp-1', assignedEmployeeName: 'Aino Virtanen', photos: [{ id: '55555555-5555-4555-8555-555555555555', sizeBytes: 70, uploadedUtc: '2026-12-03T10:05:00Z' }] };
    await fixture(page, language, [completed]);
    await page.goto('/partner/');
    await page.getByRole('tab', { name: copy.keys, exact: true }).click();

    await expect(page.getByText(copy.completionPhotos, { exact: false })).toBeVisible();
    await expect(page.getByRole('img', { name: `${copy.completionPhoto} 1` })).toBeVisible();
    // The assignee is staff-only and never appears in the partner view.
    await expect(page.getByText('Assigned to')).toHaveCount(0);
    await expect(page.getByText('Aino Virtanen')).toHaveCount(0);
    // A finished request offers no cancellation.
    await expect(page.getByRole('button', { name: copy.cancelKeyRequest, exact: true })).toHaveCount(0);
  });
}

for (const language of ['en', 'fi'] as const) {
  const copy = partnerCopy[language];
  test(`a partner creates a key handover request and the kind is sent in ${language}`, async ({ page }) => {
    const { writes } = await fixture(page, language);
    await page.goto('/partner/');
    await page.getByRole('tab', { name: copy.keys, exact: true }).click();
    await page.getByRole('radio', { name: copy.keyHandover, exact: true }).check();
    await page.getByLabel(copy.keyDate, { exact: true }).fill('2026-12-04');
    await page.getByRole('button', { name: copy.submitKeyRequest, exact: true }).click();

    await expect(page.getByRole('status')).toHaveText(copy.keySaved);
    expect(writes).toHaveLength(1);
    expect(writes[0].body).toMatchObject({ branchId: branch.id, kind: 'Handover', date: '2026-12-04' });
  });
}

