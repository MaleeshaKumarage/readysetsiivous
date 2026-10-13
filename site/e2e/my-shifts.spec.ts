import { test, expect, Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const shiftId = '11111111-1111-4111-8111-111111111111';
const formId = '22222222-2222-4222-8222-222222222222';

async function setupWork(page: Page, options: { empty?: boolean; failStart?: boolean; failEnd?: boolean; lostStart?: boolean; lostEnd?: boolean; loadFailure?: boolean; future?: boolean } = {}) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  const start = new Date(Date.now() - 3600000).toISOString();
  const occurrence: any = { shiftId, shiftName: 'AAA1 weekday shift', startUtc: options.future ? new Date(Date.now()+86400000).toISOString() : start,
    endUtc: new Date(Date.now()+(options.future ? 90000000 : 3600000)).toISOString(), form: null };
  let startCalls = 0; let endCalls = 0; let loadCalls = 0; let submitted: any = null;
  await page.route(/\/api\/v1\/me\/shifts$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    loadCalls++;
    return route.fulfill(options.loadFailure && loadCalls === 1 ? { status: 403, headers } : { status: 200, headers, json: [occurrence] });
  });
  await page.route(new RegExp(`/api/v1/me/shifts/${shiftId}/start$`), route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    startCalls++;
    if (options.failStart && startCalls === 1) return route.fulfill({ status: 503, headers });
    occurrence.form = { id: formId, shiftId, employeeId: 'employee-a', templateId: options.empty ? '00000000-0000-0000-0000-000000000000' : formId,
      templateTitle: 'Daily activities', items: options.empty ? [] : [{ itemText: 'Corridor', isChecked: false }, { itemText: 'Gym', isChecked: false }],
      photoUrls: [], cleanerNotes: '', isSubmitted: false, startedAtUtc: new Date().toISOString(), submittedUtc: null };
    return route.fulfill(options.lostStart ? { status: 503, headers } : { status: 200, headers, json: occurrence.form });
  });
  await page.route(new RegExp(`/api/v1/me/quality-cycle/${formId}/end$`), route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    endCalls++;
    if (options.failEnd && endCalls === 1) return route.fulfill({ status: 503, headers });
    submitted = route.request().postDataJSON();
    occurrence.form = { ...occurrence.form, ...submitted, isSubmitted: true, submittedUtc: new Date().toISOString(), endedAtUtc: new Date().toISOString() };
    return route.fulfill(options.lostEnd ? { status: 503, headers } : { status: 200, headers, json: occurrence.form });
  });
  return { get submitted() { return submitted; }, get startCalls() { return startCalls; }, get endCalls() { return endCalls; } };
}

for (const selected of ['none', 'subset', 'all']) {
  test(`employee starts shift and ends with ${selected} activities and no required notes`, async ({ page }) => {
    const work = await setupWork(page);
    await page.goto('/me/shifts/');
    await page.getByRole('button', { name: 'Start', exact: true }).click();
    await page.getByRole('button', { name: 'Record activities & end', exact: true }).click();
    if (selected !== 'none') await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
    if (selected === 'all') await page.getByRole('checkbox', { name: 'Gym', exact: true }).check();
    await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked({ checked: selected !== 'none' });
    await page.getByRole('button', { name: 'End shift', exact: true }).click();
    await expect(page.getByText('Done', { exact: true })).toBeVisible();
    expect(work.submitted.items).toEqual([{ itemText: 'Corridor', isChecked: selected !== 'none' }, { itemText: 'Gym', isChecked: selected === 'all' }]);
    expect(work.submitted.cleanerNotes).toBe(''); expect(work.endCalls).toBe(1);
  });
}

test('employee can start and end a shift without an assigned checklist', async ({ page }) => {
  const work = await setupWork(page, { empty: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await expect(page.getByText('No checklist assigned. You can end this shift without recording activities.')).toBeVisible();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible(); expect(work.submitted.items).toEqual([]);
});

test('failed start and end retries retain selected activities and notes', async ({ page }) => {
  const work = await setupWork(page, { failStart: true, failEnd: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText('Could not start shift. Please retry.')).toBeVisible();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await page.getByPlaceholder('Notes (optional)').fill('Gym was not done today.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Could not end shift. Your selections are kept; please retry.')).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked();
  await expect(page.getByPlaceholder('Notes (optional)')).toHaveValue('Gym was not done today.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible(); expect(work.startCalls).toBe(2); expect(work.endCalls).toBe(2);
});

test('lost start and end responses recover the already saved shift state', async ({ page }) => {
  const work = await setupWork(page, { lostStart: true, lostEnd: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Gym', exact: true }).check();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible();
  expect(work.startCalls).toBe(1); expect(work.endCalls).toBe(1); expect(work.submitted.items[1].isChecked).toBe(true);
});

test('failed work read is an error, not an empty schedule, and can be retried', async ({ page }) => {
  await setupWork(page, { loadFailure: true }); await page.goto('/me/shifts/');
  await expect(page.getByText('Failed to load your shifts.')).toBeVisible();
  await expect(page.getByText('No shifts scheduled.')).toHaveCount(0);
  await page.getByRole('button', { name: 'Retry loading shifts' }).click();
  await expect(page.getByRole('heading', { name: 'AAA1 weekday shift' })).toBeVisible();
});

test('anonymous employee must sign in before loading shifts', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: false }); let requests = 0;
  page.on('request', request => { if (request.url().includes('/api/v1/me/shifts')) requests++; });
  await page.goto('/me/shifts/'); await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible(); expect(requests).toBe(0);
});

test('a future shift becomes startable at its actual scheduled instant', async ({ page }) => {
  const now = Date.now(); await page.clock.install({ time: new Date(now) });
  const work = await setupWork(page, { future: true }); await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeDisabled();
  await expect(page.getByText('Available at the scheduled start time.')).toBeVisible(); expect(work.startCalls).toBe(0);
  await page.clock.fastForward(86405000);
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Record activities & end' })).toBeVisible(); expect(work.startCalls).toBe(1);
});

test('clock start displays the actual API rejection reason', async ({ page }) => {
  await setupWork(page);
  await page.route(new RegExp(`/api/v1/me/shifts/${shiftId}/start$`), route => route.fulfill({ status: 409, headers, json: { detail: 'Choose an actual scheduled shift occurrence.' } }));
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText('Could not start shift. Choose an actual scheduled shift occurrence.')).toBeVisible();
});
