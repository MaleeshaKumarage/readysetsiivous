import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleaningRequest } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branchFor = (index: number): Branch => ({
  id: `22222222-2222-4222-8222-${String(index).padStart(12, '0')}`, companyId, name: `Property ${index}`, street: 'Lake road 1', postalCode: '33100', city: 'Tampere', country: 'FI',
  contactPhone: '', contactEmail: null, isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120,
  pricingVersion: 1, pricingReady: true, quoteRequestedUtc: null,
} as unknown as Branch);
const requestFor = (index: number, status = 'Requested'): CleaningRequest => ({
  id: `33333333-3333-4333-8333-${String(index).padStart(12, '0')}`, version: 7, companyId, branchId: branchFor(index).id, branchName: `Property ${index}`,
  fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: 'Original instructions', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120,
  status, firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null,
  offeredEmployeeId: null,
} as unknown as CleaningRequest);

type Write = { method: string; path: string; body: Record<string, unknown> };

/** Admin partner operations. Requests are served from `requests`; writes are recorded in `writes`. */
async function setup(page: Page, requests: CleaningRequest[]) {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/employees$/, route => route.fulfill({ headers, json: [
    { id: 'auto', firstName: 'Auto', lastName: 'Cleaner', isActive: true, role: 'employee', autoAcceptWork: true, keycloakUserId: 'k-1' },
    { id: 'manual', firstName: 'Manual', lastName: 'Cleaner', isActive: true, role: 'employee', autoAcceptWork: false },
  ] }));
  const branches = Array.from({ length: 40 }, (_, index) => branchFor(index));
  let current = [...requests];
  const writes: Write[] = [];
  await page.route(/\/api\/v1\/admin\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path === '/requests' ? current : path === '/branches' ? branches : [] });
    const body = req.postDataJSON() as Record<string, unknown>;
    writes.push({ method: req.method(), path, body });
    const index = current.findIndex(item => path.startsWith(`/requests/${item.id}`));
    if (index < 0) return route.fulfill({ status: 404, headers });
    const item = current[index];
    if (path.endsWith('/offer')) current[index] = { ...item, version: item.version + 1, status: body.employeeId === 'auto' ? 'Accepted' : 'Requested', offeredEmployeeId: body.employeeId === 'manual' ? 'manual' : null } as CleaningRequest;
    else if (path.endsWith('/cancel')) current[index] = { ...item, version: item.version + 1, status: 'Cancelled' };
    else if (req.method() === 'PUT') current[index] = { ...item, version: item.version + 1, fromDate: String(body.fromDate), toDate: String(body.toDate), exactStartLocalTime: (body.exactStartLocalTime as string | null) ?? null, notes: String(body.notes ?? '') };
    return route.fulfill({ headers, json: current[index] });
  });
  return { writes };
}

test.describe('partner operations actions open as modal dialogs', () => {
  test('Arrange cleaner opens a dialog in view and sends the offer', async ({ page }) => {
    const { writes } = await setup(page, Array.from({ length: 8 }, (_, index) => requestFor(index)));
    await page.goto('/admin/partner-requests/');
    const card = page.getByRole('article', { name: 'Property 7 request' });
    await card.getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Arrange Property 7', exact: true });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Cleaner', exact: true })).toBeInViewport();
    await dialog.getByRole('combobox', { name: 'Cleaner', exact: true }).selectOption('manual');
    await dialog.getByRole('button', { name: 'Send cleaning offer', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();
    expect(writes[0]).toMatchObject({ method: 'POST', path: `/requests/${requestFor(7).id}/offer`, body: { employeeId: 'manual', version: 7 } });
  });

  test('Edit / reschedule dialog saves the new window and Cancel discards the draft', async ({ page }) => {
    const { writes } = await setup(page, [requestFor(0)]);
    await page.goto('/admin/partner-requests/');
    const card = page.getByRole('article', { name: 'Property 0 request' });
    await card.getByRole('button', { name: 'Edit / reschedule request', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: /^Edit \/ reschedule / });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('From date', { exact: true }).fill('2026-12-05');
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    expect(writes).toHaveLength(0);
    await card.getByRole('button', { name: 'Edit / reschedule request', exact: true }).click();
    await expect(dialog.getByLabel('From date', { exact: true })).toHaveValue('2026-12-01');
    await dialog.getByLabel('From date', { exact: true }).fill('2026-12-05');
    await dialog.getByRole('button', { name: 'Save request changes', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(card.getByText('2026-12-05 – 2026-12-02', { exact: true })).toBeVisible();
    expect(writes[0]).toMatchObject({ method: 'PUT', path: `/requests/${requestFor(0).id}`, body: { fromDate: '2026-12-05', version: 7 } });
  });

  test('Cancel request needs a reason in a dialog; Keep request sends nothing', async ({ page }) => {
    const { writes } = await setup(page, [requestFor(0)]);
    await page.goto('/admin/partner-requests/');
    await page.getByRole('button', { name: 'Cancel request', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: /^Cancel / });
    const confirm = dialog.getByRole('button', { name: 'Confirm cancellation', exact: true });
    await expect(confirm).toBeDisabled();
    await dialog.getByRole('button', { name: 'Keep request', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    expect(writes).toHaveLength(0);
    await page.getByRole('button', { name: 'Cancel request', exact: true }).click();
    await dialog.getByLabel('Reason for cancellation', { exact: true }).fill('Owner cancelled travel.');
    await confirm.click();
    await expect(page.getByText('Request cancelled.', { exact: true })).toBeVisible();
    expect(writes[0]).toEqual({ method: 'POST', path: `/requests/${requestFor(0).id}/cancel`, body: { version: 7, reason: 'Owner cancelled travel.' } });
  });
});

test.describe('partner operations completed work and large lists', () => {
  test('completed and cancelled requests leave Requests and appear under Completed requests without actions', async ({ page }) => {
    await setup(page, [requestFor(0), requestFor(1, 'Completed'), requestFor(2, 'Cancelled')]);
    await page.goto('/admin/partner-requests/');
    await expect(page.getByRole('article', { name: 'Property 0 request' })).toBeVisible();
    await expect(page.getByRole('article', { name: 'Property 1 request' })).toHaveCount(0);
    await expect(page.getByRole('article', { name: 'Property 2 request' })).toHaveCount(0);
    await page.getByRole('tab', { name: 'Completed requests', exact: true }).click();
    await expect(page.getByRole('article', { name: 'Property 1 request' })).toBeVisible();
    await expect(page.getByRole('article', { name: 'Property 2 request' })).toBeVisible();
    await expect(page.getByRole('article', { name: 'Property 1 request' }).getByRole('button', { name: 'Cancel request', exact: true })).toHaveCount(0);
    await expect(page.getByRole('article', { name: 'Property 1 request' }).getByRole('button', { name: 'Arrange cleaner / time', exact: true })).toHaveCount(0);
  });

  test('large request lists page 25 at a time with Previous and Next', async ({ page }) => {
    await setup(page, Array.from({ length: 30 }, (_, index) => requestFor(index)));
    await page.goto('/admin/partner-requests/');
    await expect(page.getByRole('article')).toHaveCount(25);
    await expect(page.getByText('Showing 1–25 of 30', { exact: true })).toBeVisible();
    await page.getByRole('navigation', { name: 'Requests pages' }).getByRole('button', { name: 'Next', exact: true }).click();
    await expect(page.getByRole('article')).toHaveCount(5);
    await expect(page.getByText('Page 2 of 2', { exact: true })).toBeVisible();
    await page.getByRole('navigation', { name: 'Requests pages' }).getByRole('button', { name: 'Previous', exact: true }).click();
    await expect(page.getByRole('article')).toHaveCount(25);
  });

  test('Completed requests has its own pager that keeps its page separately from Requests', async ({ page }) => {
    const closed = Array.from({ length: 28 }, (_, index) => requestFor(index + 2, index % 2 ? 'Cancelled' : 'Completed'));
    await setup(page, [requestFor(0), requestFor(1), ...closed]);
    await page.goto('/admin/partner-requests/');
    await expect(page.getByRole('article')).toHaveCount(2);
    await expect(page.getByRole('navigation', { name: 'Requests pages' })).toHaveCount(0);
    await page.getByRole('tab', { name: 'Completed requests', exact: true }).click();
    const pager = page.getByRole('navigation', { name: 'Completed requests pages' });
    await expect(page.getByRole('article')).toHaveCount(25);
    await expect(pager.getByText('Showing 1–25 of 28', { exact: true })).toBeVisible();
    await expect(pager.getByRole('button', { name: 'Previous', exact: true })).toBeDisabled();
    await pager.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(page.getByRole('article')).toHaveCount(3);
    await expect(pager.getByText('Page 2 of 2', { exact: true })).toBeVisible();
    await expect(pager.getByText('Showing 26–28 of 28', { exact: true })).toBeVisible();
    await expect(pager.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
    await expect(page.getByRole('article', { name: 'Property 29 request' })).toBeVisible();
    // Switching tabs keeps each list's own page.
    await page.getByRole('tab', { name: 'Requests', exact: true }).click();
    await expect(page.getByRole('article')).toHaveCount(2);
    await page.getByRole('tab', { name: 'Completed requests', exact: true }).click();
    await expect(pager.getByText('Page 2 of 2', { exact: true })).toBeVisible();
    await pager.getByRole('button', { name: 'Previous', exact: true }).click();
    await expect(page.getByRole('article')).toHaveCount(25);
    await expect(pager.getByText('Page 1 of 2', { exact: true })).toBeVisible();
  });

  test('the modal close control and Escape both close Arrange without sending anything', async ({ page }) => {
    const { writes } = await setup(page, [requestFor(0)]);
    await page.goto('/admin/partner-requests/');
    const card = page.getByRole('article', { name: 'Property 0 request' });
    const opener = card.getByRole('button', { name: 'Arrange cleaner / time', exact: true });
    const dialog = page.getByRole('dialog', { name: 'Arrange Property 0', exact: true });
    await opener.click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('combobox', { name: 'Cleaner', exact: true }).selectOption('manual');
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await opener.click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Close dialog', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    expect(writes).toHaveLength(0);
  });
});
