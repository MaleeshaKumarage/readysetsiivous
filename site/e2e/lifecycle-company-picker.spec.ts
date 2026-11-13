import { selectDailyScheduleIfEmpty } from './admin-helpers';
import { test, expect, type Page } from '@playwright/test';
import { shiftAction } from './shift-actions';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companies = Array.from({ length: 130 }, (_, index) => ({ id: `company-${index + 1}`, name: `Company ${String(index + 1).padStart(3, '0')}`, businessId: `B-${index + 1}`, isActive: true }));

async function setup(page: Page, options: { inactiveEdit?: boolean; inactiveSearch?: boolean } = {}) {
  await setupAdminMocks(page);
  const searchCompanies = companies.map(company => (options.inactiveSearch && company.id === 'company-130') || (options.inactiveEdit && company.id === 'company-125') ? { ...company, isActive: false } : company);
  const queries: { search: string; skip: number; take: number }[] = [];
  const writes: any[] = [];
  const state = { fail: false, failMore: false, failSelection: false, slow: false, delayCompany: false, release: () => {}, releaseCompany: () => {} };
  const companyGate = new Promise<void>(resolve => { state.releaseCompany = resolve; });
  const slow = new Promise<void>(resolve => { state.release = resolve; });
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/companies')) {
      const search = url.searchParams.get('search') ?? '', skip = Number(url.searchParams.get('skip')), take = Number(url.searchParams.get('take'));
      queries.push({ search, skip, take });
      if (search === 'slow' && state.slow) { await slow; return route.fulfill({ headers, json: { items: [{ ...companies[0], name: 'Stale company' }], total: 1 } }); }
      if (state.fail || (state.failMore && skip > 0)) return route.fulfill({ headers, status: 500, json: { title: 'unavailable' } });
      const matching = searchCompanies.filter(company => `${company.name} ${company.businessId}`.toLowerCase().includes(search.toLowerCase()));
      return route.fulfill({ headers, json: { items: matching.slice(skip, skip + take), total: matching.length } });
    }
    if (/\/companies\/company-\d+$/.test(path)) {
      const id = path.split('/').at(-1)!;
      if (state.failSelection) return route.fulfill({ headers, status: 500, json: {} });
      if (state.delayCompany && id === 'company-1') await companyGate;
      return route.fulfill({ headers, json: { company: searchCompanies.find(company => company.id === id), branches: [{ id: options.inactiveEdit ? `branch-${id}` : 'branch', companyId: id, name: id === 'company-1' ? 'Property A' : id === 'company-2' ? 'Property B' : 'Property', isActive: !(options.inactiveEdit && id === 'company-125') }] } });
    }
    if (path.endsWith('/shifts') && request.method() === 'POST') {
      const body = request.postDataJSON(); writes.push(body);
      return route.fulfill({ headers, json: { id: 'new-shift', ...body } });
    }
    if (path.endsWith('/shifts/old-shift') && request.method() === 'PUT') {
      const body = request.postDataJSON(); writes.push(body);
      return route.fulfill({ headers, json: { id: 'old-shift', ...body } });
    }
    if (path.endsWith('/agreements') && request.method() === 'POST') {
      writes.push(request.postData());
      return route.fulfill({ headers, json: { id: 'agreement', title: 'Agreement', signers: [], signerCount: 0, signedCount: 0, code: 'AGR', isActive: true, status: 'Pending' } });
    }
    return route.fulfill({ headers, json: path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' }
      : isShiftsRoute(path) ? shiftsResponse(options.inactiveEdit ? [{ id: 'old-shift', name: 'Old shift', companyId: 'company-125', branchId: 'branch-company-125', isActive: false, validFrom: '2026-01-01', schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' } }] : [], request.url())
      : path.endsWith('/legacy-repairs') ? { agreements: [], companies: [] } : [] });
  });
  return { state, queries, writes };
}

test('agreement company picker reaches later pages and preserves the document and title', async ({ page }) => {
  const { state, queries, writes } = await setup(page);
  await page.goto('/admin/agreements/');
  await page.getByRole('button', { name: 'New agreement', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'New agreement', exact: true });
  await dialog.getByRole('textbox', { name: 'Title', exact: true }).fill('Agreement');
  await dialog.locator('input[type=file]').setInputFiles({ name: 'agreement.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') });
  await dialog.getByRole('textbox', { name: 'Company', exact: true }).click();
  await expect(page.getByText('50 of 130 companies', { exact: true })).toBeVisible();
  state.failMore = true;
  await page.getByRole('button', { name: 'Load more companies' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load companies' })).toBeVisible();
  state.failMore = false;
  await page.getByRole('button', { name: 'Retry companies' }).click();
  await expect(page.getByText('100 of 130 companies', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Load more companies' }).click();
  await page.getByRole('option', { name: 'Company 125', exact: true }).click();
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveValue('Company 125');
  await expect(dialog.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue('Agreement');
  expect(await dialog.locator('input[type=file]').evaluate((input: HTMLInputElement) => input.files?.length)).toBe(1);
  await dialog.getByPlaceholder('Name', { exact: true }).fill('Signer');
  await dialog.getByPlaceholder('Email', { exact: true }).fill('signer@example.test');
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes[0]).toContain('company-125');
  expect(queries.map(query => query.skip)).toEqual([0, 50, 50, 100]);
});

test('shift company search finds a company beyond 100 and uses it for the new shift', async ({ page }) => {
  const { state, queries, writes } = await setup(page);
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  await dialog.getByRole('textbox', { name: 'Company', exact: true }).click();
  await expect(page.getByText('50 of 130 companies', { exact: true })).toBeVisible();
  state.failMore = true;
  await page.getByRole('button', { name: 'Load more companies' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load companies' })).toBeVisible();
  state.failMore = false;
  await page.getByRole('button', { name: 'Retry companies' }).click();
  await expect(page.getByText('100 of 130 companies', { exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Company 075', exact: true })).toHaveCount(1);
  await page.getByRole('textbox', { name: 'Search companies' }).fill('B-125');
  await expect(page.getByRole('option', { name: 'Company 125', exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'Search companies' }).press('ArrowDown');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Enter');
  await dialog.getByLabel('Branch', { exact: true }).click();
  await page.getByRole('option', { name: 'Property', exact: true }).click();
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes[0].companyId).toBe('company-125');
  expect(queries.some(query => query.search === 'B-125' && query.take === 50)).toBe(true);
  expect(queries.filter(query => !query.search && query.take === 50).map(query => query.skip)).toEqual([0, 50, 50]);
});

test('search failures retry, empty results are distinct, and stale responses cannot replace the latest query', async ({ page }) => {
  const { state, queries } = await setup(page);
  state.fail = true; state.slow = true;
  await page.goto('/admin/agreements/'); await page.getByRole('button', { name: 'New agreement', exact: true }).click();
  const company = page.getByRole('dialog').getByRole('textbox', { name: 'Company', exact: true });
  await company.click();
  await expect(page.getByRole('alert').filter({ hasText: 'Could not load companies' })).toBeVisible();
  await expect(page.getByText('No companies found.', { exact: true })).toHaveCount(0);
  state.fail = false; await page.getByRole('button', { name: 'Retry companies' }).click();
  await page.getByRole('option', { name: 'Company 001', exact: true }).click();
  await company.click();
  const search = page.getByRole('textbox', { name: 'Search companies' });
  await search.fill('slow');
  await expect.poll(() => queries.some(query => query.search === 'slow')).toBe(true);
  await search.fill('Company 125');
  await expect(page.getByRole('option', { name: 'Company 125', exact: true })).toBeVisible();
  const staleResponse = page.waitForResponse(response => response.url().includes('search=slow'));
  state.release();
  await (await staleResponse).finished();
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
  await expect(page.getByRole('option', { name: 'Stale company', exact: true })).toHaveCount(0);
  await search.fill('does-not-exist');
  await expect(page.getByText('No companies found.', { exact: true })).toBeVisible();
  await search.press('Escape');
  await expect(company).toHaveValue('Company 001');
});

test('editing an inactive shift hydrates its current company outside the first page', async ({ page }) => {
  const { queries } = await setup(page, { inactiveEdit: true });
  await page.goto('/admin/shifts/'); await page.getByRole('tab', { name: /^Inactive/ }).click();
  await shiftAction(page, 'Old shift', 'Edit schedule');
  const dialog = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveValue('Company 125 (inactive)');
  expect(queries.filter(query => query.take === 50)).toHaveLength(0);
  await expect(dialog.getByLabel('Branch', { exact: true })).toHaveValue('Property (inactive)');
});

test('editing a shift can return to its original inactive company after selecting an active company', async ({ page }) => {
  const { writes } = await setup(page, { inactiveEdit: true });
  await page.goto('/admin/shifts/'); await page.getByRole('tab', { name: /^Inactive/ }).click();
  await shiftAction(page, 'Old shift', 'Edit schedule');
  const dialog = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  const company = dialog.getByRole('textbox', { name: 'Company', exact: true });
  const branch = dialog.getByLabel('Branch', { exact: true });
  await expect(company).toHaveValue('Company 125 (inactive)');
  await expect(branch).toHaveValue('Property (inactive)');
  await company.click();
  await page.getByRole('textbox', { name: 'Search companies' }).fill('Company 002');
  await page.getByRole('option', { name: 'Company 002', exact: true }).click();
  await expect(branch).toHaveValue('');
  await branch.click();
  await page.getByRole('option', { name: 'Property B', exact: true }).click();
  await company.click();
  await page.getByRole('textbox', { name: 'Search companies' }).fill('Company 125');
  const original = page.getByRole('option', { name: 'Company 125 (inactive)', exact: true });
  await expect(original).toBeEnabled();
  await expect(original).toHaveAttribute('aria-disabled', 'false');
  await original.click();
  await expect(company).toHaveValue('Company 125 (inactive)');
  await expect(branch).toHaveValue('');
  await branch.click();
  await expect(page.getByRole('option', { name: 'Property B', exact: true })).toHaveCount(0);
  await page.getByRole('option', { name: 'Property (inactive)', exact: true }).click();
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ companyId: 'company-125', branchId: 'branch-company-125', isActive: false });
});


test('company changes cannot restore branches from a late previous-company response', async ({ page }) => {
  const { state, writes } = await setup(page); state.delayCompany = true;
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  const company = dialog.getByRole('textbox', { name: 'Company', exact: true });
  await company.click();
  const firstRequest = page.waitForRequest(request => request.url().endsWith('/companies/company-1'));
  await page.getByRole('option', { name: 'Company 001', exact: true }).click();
  await firstRequest;
  await company.click(); await page.getByRole('option', { name: 'Company 002', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click();
  await expect(page.getByRole('option', { name: 'Property B', exact: true })).toBeVisible();
  const staleResponse = page.waitForResponse(response => response.url().endsWith('/companies/company-1'));
  state.releaseCompany(); await (await staleResponse).finished();
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
  await expect(page.getByRole('option', { name: 'Property A', exact: true })).toHaveCount(0);
  await page.getByRole('option', { name: 'Property B', exact: true }).click();
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes[0].companyId).toBe('company-2');
  expect(writes[0].branchId).toBe('branch');
});

test('selected-company load failure retains the edit identity and can be retried', async ({ page }) => {
  const { state } = await setup(page, { inactiveEdit: true }); state.failSelection = true;
  await page.goto('/admin/shifts/'); await page.getByRole('tab', { name: /^Inactive/ }).click();
  await shiftAction(page, 'Old shift', 'Edit schedule');
  const dialog = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(dialog.getByRole('alert').filter({ hasText: 'Your selection is kept' })).toBeVisible();
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveValue('Selected company (unavailable)');
  state.failSelection = false; await dialog.getByRole('button', { name: 'Retry selected company', exact: true }).click();
  await expect(dialog.getByRole('textbox', { name: 'Company', exact: true })).toHaveValue('Company 125 (inactive)');
});


test('a new shift cannot select an inactive company returned by server search', async ({ page }) => {
  const { writes } = await setup(page, { inactiveSearch: true });
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add shift', exact: true });
  const company = dialog.getByRole('textbox', { name: 'Company', exact: true });
  await company.click();
  await page.getByRole('textbox', { name: 'Search companies' }).fill('Company 130');
  const inactive = page.getByRole('option', { name: 'Company 130 (inactive)', exact: true });
  await expect(inactive).toBeVisible();
  await expect(inactive).toBeDisabled();
  // Even a direct pointer attempt must leave the selection unchanged.
  await inactive.click({ force: true });
  await page.getByRole('textbox', { name: 'Search companies' }).press('ArrowDown');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Enter');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Escape');
  await expect(company).toHaveValue('');
  await expect(dialog.getByLabel('Branch', { exact: true })).toBeDisabled();
  await selectDailyScheduleIfEmpty(page); await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('Company and branch are required.', { exact: true })).toBeVisible();
  expect(writes).toHaveLength(0);
});

test('agreement creation can select an inactive company and shows its inactive label', async ({ page }) => {
  const { writes } = await setup(page, { inactiveSearch: true });
  await page.goto('/admin/agreements/');
  await page.getByRole('button', { name: 'New agreement', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'New agreement', exact: true });
  const company = dialog.getByRole('textbox', { name: 'Company', exact: true });
  await company.click();
  await page.getByRole('textbox', { name: 'Search companies' }).fill('Company 130');
  const inactive = page.getByRole('option', { name: 'Company 130 (inactive)', exact: true });
  await expect(inactive).toBeEnabled();
  await inactive.click();
  await expect(company).toHaveValue('Company 130 (inactive)');
  await dialog.getByRole('textbox', { name: 'Title', exact: true }).fill('Historical company agreement');
  await dialog.locator('input[type=file]').setInputFiles({ name: 'agreement.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') });
  await dialog.getByPlaceholder('Name', { exact: true }).fill('Signer');
  await dialog.getByPlaceholder('Email', { exact: true }).fill('signer@example.test');
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes[0]).toContain('company-130');
});
