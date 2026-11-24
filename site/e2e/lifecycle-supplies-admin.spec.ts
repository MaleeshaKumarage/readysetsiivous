import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Admin and manager review of supply requests: filter by status, approve, decline, and fulfil.
// Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function request(id: string, status: string, version: string) {
  return {
    id, employeeId: 'employee-a', employeeName: 'Ana Keeper', note: '',
    lines: [{ itemId: 'cloths', itemName: 'Microfibre cloths', unit: 'pcs', quantity: 3 }],
    status, decisionNote: null, decidedByName: null, decidedUtc: null, createdUtc: '2026-11-23T08:00:00Z', version,
  };
}

async function setup(page: Page, options: { decideStatus?: number } = {}) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/employees(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/inventory\/(locations|items)(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  const state = { requests: [request('req-1', 'Requested', 'v1'), request('req-2', 'Approved', 'v2')], decisions: [] as any[], filters: [] as string[] };
  await page.route(/\/api\/v1\/admin\/supply-requests(\/[^?]*)?(\?.*)?$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'POST') {
      const id = new URL(route.request().url()).pathname.split('/')[5];
      state.decisions.push({ id, ...route.request().postDataJSON() });
      if (options.decideStatus) return route.fulfill({ status: options.decideStatus, headers, json: { detail: 'This was changed by someone else. Refresh the page and try again.' } });
      const body = route.request().postDataJSON();
      state.requests = state.requests.map(r => r.id === id ? { ...r, status: body.decision === 'approve' ? 'Approved' : body.decision === 'decline' ? 'Declined' : 'Fulfilled', version: 'v9' } : r);
      return route.fulfill({ headers, json: state.requests.find(r => r.id === id) });
    }
    const status = new URL(route.request().url()).searchParams.get('status');
    state.filters.push(status ?? 'All');
    return route.fulfill({ headers, json: status ? state.requests.filter(r => r.status === status) : state.requests });
  });
  return state;
}

async function openSupplies(page: Page) {
  await page.goto('/admin/inventory/');
  await page.getByRole('tab', { name: 'Supply requests', exact: true }).click();
}

test('Requested is the default filter and lists only requested entries', async ({ page }) => {
  const state = await setup(page);
  await openSupplies(page);
  await expect(page.getByRole('cell', { name: 'Ana Keeper' })).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Approve', exact: true })).toBeVisible();
  expect(state.filters[0]).toBe('Requested');
});

test('Approve sends the decision with the request version and refreshes the list', async ({ page }) => {
  const state = await setup(page);
  await openSupplies(page);
  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  await expect.poll(() => state.decisions.length).toBe(1);
  expect(state.decisions[0]).toEqual({ id: 'req-1', decision: 'approve', version: 'v1', note: null });
  await expect(page.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0);
});

test('Decline sends a decline decision', async ({ page }) => {
  const state = await setup(page);
  await openSupplies(page);
  await page.getByRole('button', { name: 'Decline', exact: true }).click();
  await expect.poll(() => state.decisions.length).toBe(1);
  expect(state.decisions[0]).toMatchObject({ id: 'req-1', decision: 'decline', version: 'v1' });
});

test('an approved request can be marked fulfilled', async ({ page }) => {
  const state = await setup(page);
  await openSupplies(page);
  await page.getByRole('tab', { name: 'Approved', exact: true }).click();
  await page.getByRole('button', { name: 'Mark fulfilled', exact: true }).click();
  await expect.poll(() => state.decisions.length).toBe(1);
  expect(state.decisions[0]).toMatchObject({ id: 'req-2', decision: 'fulfil', version: 'v2' });
});

test('a stale decision shows the reason and keeps the request for review', async ({ page }) => {
  await setup(page, { decideStatus: 409 });
  await openSupplies(page);
  await page.getByRole('button', { name: 'Approve', exact: true }).click();
  await expect(page.getByText('This was changed by someone else. Refresh the page and try again.')).toBeVisible();
});
