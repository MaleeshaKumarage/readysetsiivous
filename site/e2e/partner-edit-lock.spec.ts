import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleaningRequest, PartnerIdentity } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', isActive: true, street: 'Lake road 1', city: 'Tampere', roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true } as Branch;
const base: CleaningRequest = { id: 'lock-request', version: 3, companyId, branchId: branch.id, branchName: branch.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: 'Original instructions', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null } as CleaningRequest;
const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true } } as PartnerIdentity;

async function open(page: Page, request: CleaningRequest) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; });
  await page.route(/\/api\/v1\/(?:admin|partner)\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() !== 'GET') return route.fulfill({ status: 409, headers, json: { detail: 'Unexpected mutation.' } });
    const suffix = path.split('/partner')[1];
    const json = path === '/api/v1/partner/me' ? identity : path.endsWith('/employees') ? []
      : path.endsWith('/companies') ? { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 }
      : suffix === '/branches' || suffix === '/quotes' ? [branch]
      : suffix === '/requests' ? [request]
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ headers, json });
  });
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  return page.getByRole('article', { name: /Lake apartment 2026-12-01/ });
}

test('owner can edit a cleaning request that is not yet assigned, and sees that no cleaner is assigned', async ({ page }) => {
  const card = await open(page, base);
  await expect(card.getByText('Cleaner: Not assigned yet', { exact: true })).toBeVisible();
  await expect(card.getByRole('button', { name: 'Edit request', exact: true })).toBeVisible();
});

test('owner can still edit a request after a cleaner has declined or been unassigned', async ({ page }) => {
  const card = await open(page, { ...base, status: 'Requested', firstAcceptedAtUtc: '2026-11-02T10:00:00Z', acceptedCleanerName: null });
  await expect(card.getByRole('button', { name: 'Edit request', exact: true })).toBeVisible();
});

test('owner cannot edit a visit a cleaner has accepted; a change request is the route instead', async ({ page }) => {
  const card = await open(page, { ...base, status: 'Accepted', firstAcceptedAtUtc: '2026-11-02T10:00:00Z', acceptedCleanerName: 'Maija M.' });
  await expect(card.getByRole('button', { name: 'Edit request', exact: true })).toHaveCount(0);
  await expect(card.getByRole('button', { name: 'Request a change or cancellation', exact: true })).toBeVisible();
});
