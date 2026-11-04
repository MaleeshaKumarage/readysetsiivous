import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleaningRequest } from '../src/lib/partnerApi';

// Staff Assigned, Completed and Cancelled lists: empty states and independent pagers (R10).
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branchFor = (index: number) => ({ id: `22222222-2222-4222-8222-${String(index).padStart(12, '0')}`, companyId, name: `Property ${index}`, street: 'Lake road 1', postalCode: '33100', city: 'Tampere', country: 'FI', contactPhone: '', contactEmail: null, isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingVersion: 1, pricingReady: true, quoteRequestedUtc: null }) as unknown as Branch;
const requestFor = (index: number, status: string): CleaningRequest => ({
  id: `33333333-3333-4333-8333-${String(index).padStart(12, '0')}`, version: 3, companyId, branchId: branchFor(index).id, branchName: `Property ${index}`,
  fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: null, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120,
  status, firstAcceptedAtUtc: '2026-11-20T10:00:00Z', linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: 'Test Cleaner', actualStartUtc: null, actualEndUtc: null, offeredEmployeeId: null,
} as unknown as CleaningRequest);

async function setup(page: Page, requests: CleaningRequest[]) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    const json = path === '/requests' ? requests : path === '/branches' ? Array.from({ length: 40 }, (_, index) => branchFor(index)) : [];
    return route.fulfill({ headers, json });
  });
}

test('Assigned, Completed and Cancelled tabs say what is empty', async ({ page }) => {
  await setup(page, []);
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Assigned', exact: true }).click();
  await expect(page.getByText('No assigned visits.', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Completed requests', exact: true }).click();
  await expect(page.getByText('No completed requests.', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Cancelled requests', exact: true }).click();
  await expect(page.getByText('No cancelled requests.', { exact: true })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Cancelled requests pages' })).toHaveCount(0);
});

test('Assigned and Cancelled tabs page 25 rows at a time, each keeping its own page', async ({ page }) => {
  const assigned = Array.from({ length: 27 }, (_, index) => requestFor(index, 'Accepted'));
  const cancelled = Array.from({ length: 28 }, (_, index) => requestFor(100 + index, 'Cancelled'));
  await setup(page, [...assigned, ...cancelled]);
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Assigned', exact: true }).click();
  const assignedPages = page.getByRole('navigation', { name: 'Assigned pages' });
  await expect(assignedPages.getByText('Page 1 of 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(25);
  await expect(assignedPages.getByRole('button', { name: 'Previous', exact: true })).toBeDisabled();
  await assignedPages.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(assignedPages.getByText('Page 2 of 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(2);
  await expect(assignedPages.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
  // Cancelled starts on its own first page while Assigned stays on page 2.
  await page.getByRole('tab', { name: 'Cancelled requests', exact: true }).click();
  const cancelledPages = page.getByRole('navigation', { name: 'Cancelled requests pages' });
  await expect(cancelledPages.getByText('Page 1 of 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(25);
  await cancelledPages.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(cancelledPages.getByText('Page 2 of 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(3);
  await page.getByRole('tab', { name: 'Assigned', exact: true }).click();
  await expect(assignedPages.getByText('Page 2 of 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(2);
});
