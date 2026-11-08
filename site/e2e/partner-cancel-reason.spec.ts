import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleaningRequest, PartnerIdentity } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', isActive: true, street: 'Lake road 1', city: 'Tampere', roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const pending: CleaningRequest = { id: 'pending-request', version: 3, companyId, branchId: branch.id, branchName: branch.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: 'Original instructions', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null };
const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' };

async function fixture(page: Page, options: { staleOnce?: boolean; rejectWith?: { status: number; detail: string } } = {}) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; });
  const posts: { path: string; body: unknown }[] = [];
  const others: string[] = [];
  let version = pending.version, stale = false;
  await page.route(/\/api\/v1\/(?:admin|partner)\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'POST' && path.endsWith(`/requests/${pending.id}/cancel`)) {
      posts.push({ path, body: req.postDataJSON() });
      if (options.rejectWith) return route.fulfill({ status: options.rejectWith.status, headers, json: { detail: options.rejectWith.detail } });
      if (options.staleOnce && !stale) { stale = true; version = pending.version + 1; return route.fulfill({ status: 409, headers, json: { detail: 'This request changed. Reload it before making another change.' } }); }
      return route.fulfill({ headers, json: { ...pending, version: version + 1, status: 'Cancelled' } });
    }
    if (req.method() !== 'GET') { others.push(`${req.method()} ${path}`); return route.fulfill({ status: 409, headers, json: { detail: 'Unexpected mutation.' } }); }
    const suffix = path.split('/partner')[1];
    const json = path === '/api/v1/partner/me' ? identity : path.endsWith('/employees') ? []
      : path.endsWith('/companies') ? { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 }
      : suffix === '/branches' || suffix === '/quotes' ? [branch]
      : suffix === '/requests' ? [{ ...pending, version }]
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ headers, json });
  });
  return { posts, others };
}

test('owner must write a reason before confirming a cleaning request cancellation, and the trimmed reason is sent', async ({ page }) => {
  const state = await fixture(page);
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();

  const group = page.getByRole('group', { name: 'Cancel this cleaning request?', exact: true });
  const reason = group.getByLabel('Reason for cancellation', { exact: true });
  const confirm = group.getByRole('button', { name: 'Yes, cancel request', exact: true });
  await expect(confirm).toBeDisabled();
  await reason.fill('   ');
  await expect(confirm).toBeDisabled();
  expect(state.posts).toEqual([]);

  await reason.fill('  Owner travel  ');
  await expect(confirm).toBeEnabled();
  await confirm.click();
  await expect(page.getByText('Request cancelled.', { exact: true })).toBeVisible();
  expect(state.posts).toEqual([{ path: `/api/v1/partner/requests/${pending.id}/cancel`, body: { version: pending.version, reason: 'Owner travel' } }]);
  expect(state.others).toEqual([]);
});

test('a cancellation that hits a changed request reloads it, keeps the reason and retries with the new version', async ({ page }) => {
  const state = await fixture(page, { staleOnce: true });
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();

  const group = page.getByRole('group', { name: 'Cancel this cleaning request?', exact: true });
  await group.getByLabel('Reason for cancellation', { exact: true }).fill('Owner travel');
  await group.getByRole('button', { name: 'Yes, cancel request', exact: true }).click();
  await expect.poll(() => state.posts.length).toBe(1);
  await expect(group.getByRole('textbox', { name: 'Reason for cancellation', exact: true })).toHaveValue('Owner travel');

  await group.getByRole('button', { name: 'Yes, cancel request', exact: true }).click();
  await expect(page.getByText('Request cancelled.', { exact: true })).toBeVisible();
  expect(state.posts).toEqual([
    { path: `/api/v1/partner/requests/${pending.id}/cancel`, body: { version: pending.version, reason: 'Owner travel' } },
    { path: `/api/v1/partner/requests/${pending.id}/cancel`, body: { version: pending.version + 1, reason: 'Owner travel' } },
  ]);
});

test('a server rejection of the cancellation shows its message, keeps the reason and cancels nothing', async ({ page }) => {
  const state = await fixture(page, { rejectWith: { status: 400, detail: 'Cancellation reason is required and must be at most 1000 characters.' } });
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();

  const group = page.getByRole('group', { name: 'Cancel this cleaning request?', exact: true });
  await group.getByRole('textbox', { name: 'Reason for cancellation', exact: true }).fill('Owner travel');
  await group.getByRole('button', { name: 'Yes, cancel request', exact: true }).click();
  await expect(page.getByText('Cancellation reason is required and must be at most 1000 characters.', { exact: true })).toBeVisible();
  await expect(group.getByRole('textbox', { name: 'Reason for cancellation', exact: true })).toHaveValue('Owner travel');
  await expect(page.getByText('Request cancelled.', { exact: true })).toHaveCount(0);
  expect(state.posts).toHaveLength(1);
  expect(state.posts[0].body).toEqual({ version: pending.version, reason: 'Owner travel' });
});

test('keeping a cleaning request sends nothing, and reopening the cancellation starts with an empty reason', async ({ page }) => {
  const state = await fixture(page);
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();

  const group = page.getByRole('group', { name: 'Cancel this cleaning request?', exact: true });
  await group.getByLabel('Reason for cancellation', { exact: true }).fill('Half typed reason');
  await group.getByRole('button', { name: 'Keep request', exact: true }).click();
  await expect(group).toHaveCount(0);
  expect(state.posts).toEqual([]);

  await page.getByRole('button', { name: 'Cancel request', exact: true }).click();
  await expect(page.getByRole('group', { name: 'Cancel this cleaning request?', exact: true }).getByLabel('Reason for cancellation', { exact: true })).toHaveValue('');
  expect(state.posts).toEqual([]);
});
