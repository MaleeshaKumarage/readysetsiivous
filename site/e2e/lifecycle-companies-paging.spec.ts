import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET, POST, PUT, OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type, Authorization' };
const companies = Array.from({ length: 101 }, (_, index) => ({ id: `company-${index}`, name: `Company ${index}`, businessId: `ID-${index}`, contactName: null, isActive: index < 100 }));

/** Serves the companies contract: server status filter, search, skip/take paging, summary counts and detail. */
async function mockCompanies(page: Page, options: { list?: typeof companies; failAll?: { value: boolean }; failSummary?: boolean; summaryGate?: Promise<void> } = {}) {
  const list = options.list ?? companies;
  const requests: URL[] = [];
  await page.route('**/api/v1/admin/companies**', async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    if (options.failAll?.value) return route.fulfill({ status: 503, headers, json: {} });
    if (route.request().method() === 'POST') {
      const body = route.request().postDataJSON();
      const created = { id: `created-${list.length}`, name: body.name, businessId: body.businessId, contactName: null, isActive: true };
      list.unshift(created); // the mock does not sort; a new name sorts first among these fixtures
      return route.fulfill({ status: 201, headers, json: created });
    }
    if (url.pathname.endsWith('/summary')) {
      if (options.summaryGate) await options.summaryGate;
      if (options.failSummary) return route.fulfill({ status: 503, headers, json: {} });
      return route.fulfill({ headers, json: { all: list.length, active: list.filter(c => c.isActive).length, inactive: list.filter(c => !c.isActive).length } });
    }
    const detail = url.pathname.match(/\/admin\/companies\/([^/]+)$/);
    if (detail) {
      const company = list.find(c => c.id === detail[1]);
      return route.fulfill({ headers, json: { company, branches: [
        { id: 'branch-active', companyId: detail[1], name: 'Active branch', isActive: true },
        { id: 'branch-inactive', companyId: detail[1], name: 'Inactive branch', isActive: false },
      ] } });
    }
    requests.push(url);
    const status = url.searchParams.get('status') ?? 'all';
    const q = (url.searchParams.get('search') ?? '').trim().toLowerCase();
    const skip = Number(url.searchParams.get('skip') ?? 0);
    const take = Number(url.searchParams.get('take') ?? 50);
    const filtered = list.filter(c => (status === 'all' || (status === 'active') === c.isActive)
      && (!q || c.name.toLowerCase().includes(q) || c.businessId.toLowerCase().includes(q)));
    return route.fulfill({ headers, json: { items: filtered.slice(skip, skip + take), total: filtered.length } });
  });
  return requests;
}

test('lifecycle tabs show full-tenant counts and the server status filter', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const requests = await mockCompanies(page);
  await page.goto('/admin/companies/');
  const tabs = page.getByRole('tablist', { name: 'Company activity' });
  await expect(tabs.getByRole('tab', { name: 'Active (100)', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Company 100', exact: true })).not.toBeVisible();
  await tabs.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Company 100', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
  expect(requests.at(-1)?.searchParams.get('status')).toBe('inactive');
});

test('pages and page size are served by the server and a size change returns to page one', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/');
  await expect(page.getByRole('cell', { name: 'Company 24', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Company 25', exact: true })).not.toBeVisible();
  await page.getByRole('button', { name: '2', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Company 25', exact: true })).toBeVisible();
  await expect(page).toHaveURL(/page=2/);
  await page.getByRole('textbox', { name: 'Companies per page' }).click();
  await page.getByRole('option', { name: '50', exact: true }).click();
  await expect(page).toHaveURL(/size=50/);
  await expect(page).toHaveURL(/page=1/);
  await expect(page.getByRole('cell', { name: 'Company 49', exact: true })).toBeVisible();
});

test('search filters by name or business ID and resets to page one', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: '2', exact: true }).click();
  await page.getByRole('textbox', { name: 'Search companies' }).fill('Company 10');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Enter');
  await expect(page.getByRole('cell', { name: 'Company 10', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
  await expect(page).toHaveURL(/q=Company(\+|%20)10/);
  await expect(page).toHaveURL(/page=1/);
  await page.getByRole('textbox', { name: 'Search companies' }).fill('ID-10');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Enter');
  await expect(page.getByRole('cell', { name: 'Company 10', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
  await page.getByRole('textbox', { name: 'Search companies' }).fill('no-such-company');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Enter');
  await expect(page.getByText('No companies match this search.', { exact: true })).toBeVisible();
});

test('Back restores the previous page and status after a filter change', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: '2', exact: true }).click();
  await expect(page).toHaveURL(/page=2/);
  await page.getByRole('tablist', { name: 'Company activity' }).getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page).toHaveURL(/status=inactive/);
  await page.goBack();
  await expect(page).toHaveURL(/page=2/);
  await expect(page).toHaveURL(/status=active/);
  await expect(page.getByRole('cell', { name: 'Company 25', exact: true })).toBeVisible();
});

test('a page past the last populated page moves to the last page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/?status=active&page=9&size=25');
  await expect(page).toHaveURL(/page=4/);
  await expect(page.getByRole('cell', { name: 'Company 75', exact: true })).toBeVisible();
});

test('list failure shows a retryable error and refresh loads the current page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const failure = { value: true };
  await mockCompanies(page, { failAll: failure });
  await page.goto('/admin/companies/');
  await expect(page.getByRole('alert').filter({ hasText: 'Companies could not be loaded. Refresh to retry.' })).toBeVisible();
  failure.value = false;
  await page.getByRole('button', { name: 'Refresh companies' }).click();
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).toBeVisible();
});

test('creating a company from a later page returns to the Active tab, page one, and shows it', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const list = [...companies];
  await mockCompanies(page, { list });
  await page.goto('/admin/companies/?status=inactive&page=1&size=25');
  await page.getByRole('button', { name: 'Add company', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add company', exact: true });
  await dialog.getByRole('textbox', { name: 'Business ID', exact: true }).fill('NEW-ID');
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Brand New Oy');
  await dialog.getByRole('button', { name: 'Create company', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page).toHaveURL(/status=active/);
  await expect(page).toHaveURL(/page=1/);
  await expect(page.getByRole('cell', { name: 'Brand New Oy', exact: true })).toBeVisible();
});

test('deep link to a company on a later page still shows its branch list', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/?company=company-60');
  await expect(page.getByRole('cell', { name: 'Company 60', exact: true })).toBeVisible();
  await expect(page.getByText('Active branch', { exact: true })).toBeVisible();
});

test('company detail deep link opens its activity tab and branch list', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/?company=company-100');
  await expect(page.getByRole('tablist', { name: 'Company activity' }).getByRole('tab', { name: /^Inactive/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('Active branch', { exact: true })).toBeVisible();
});

test('empty states distinguish no companies, an empty tab and no search match', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page, { list: [] });
  await page.goto('/admin/companies/');
  await expect(page.getByText('No companies yet. Add one to get started.', { exact: true })).toBeVisible();
});

test('an empty tab names its status while the other status still has companies', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page, { list: companies.filter(c => c.isActive) });
  await page.goto('/admin/companies/?status=inactive');
  await expect(page.getByText('No inactive companies.', { exact: true })).toBeVisible();
  await expect(page.getByText('No companies yet.', { exact: false })).not.toBeVisible();
});

test('summary failure keeps the list and hides only the tab counts', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page, { failSummary: true });
  await page.goto('/admin/companies/');
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).toBeVisible();
  const tabs = page.getByRole('tablist', { name: 'Company activity' });
  await expect(tabs.getByRole('tab', { name: 'Active', exact: true })).toBeVisible();
  await expect(tabs.getByRole('tab', { name: /Active \(/ })).not.toBeVisible();
});

test('changing the status tab from a later page returns to page one', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: '2', exact: true }).click();
  await expect(page).toHaveURL(/page=2/);
  await page.getByRole('tablist', { name: 'Company activity' }).getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page).toHaveURL(/status=inactive/);
  await expect(page).toHaveURL(/page=1/);
});

test('branch tabs stay scoped to the open company across company tab changes', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/?company=company-100');
  const branchTabs = page.getByRole('tablist', { name: 'Branch activity' });
  await expect(page.getByText('Active branch', { exact: true })).toBeVisible();
  await branchTabs.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByText('Inactive branch', { exact: true })).toBeVisible();
  await expect(page.getByText('Active branch', { exact: true })).not.toBeVisible();
  await page.getByRole('tablist', { name: 'Company activity' }).getByRole('tab', { name: /^Active/ }).click();
  await expect(branchTabs).not.toBeVisible();
});

test('a direct URL restores the search text and filtered results', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/?status=active&page=1&size=25&q=Company%2010');
  await expect(page.getByRole('textbox', { name: 'Search companies' })).toHaveValue('Company 10');
  await expect(page.getByRole('cell', { name: 'Company 10', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
});

test('Back after a search restores the unfiltered list and empty search box', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page);
  await page.goto('/admin/companies/');
  await page.getByRole('textbox', { name: 'Search companies' }).fill('Company 10');
  await page.getByRole('textbox', { name: 'Search companies' }).press('Enter');
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).not.toBeVisible();
  await page.goBack();
  await expect(page.getByRole('textbox', { name: 'Search companies' })).toHaveValue('');
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).toBeVisible();
});

test('a slow summary never holds the company rows and counts appear when it resolves', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await mockCompanies(page, { summaryGate: gate });
  await page.goto('/admin/companies/');
  await expect(page.getByRole('cell', { name: 'Company 0', exact: true })).toBeVisible();
  await expect(page.getByRole('tablist', { name: 'Company activity' }).getByRole('tab', { name: 'Active (100)', exact: true })).not.toBeVisible();
  release();
  await expect(page.getByRole('tablist', { name: 'Company activity' }).getByRole('tab', { name: 'Active (100)', exact: true })).toBeVisible();
});

test('with the summary unavailable an empty tab does not claim there are no companies', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockCompanies(page, { list: [], failSummary: true });
  await page.goto('/admin/companies/');
  await expect(page.getByText('No active companies.', { exact: true })).toBeVisible();
  await expect(page.getByText('No companies yet.', { exact: false })).not.toBeVisible();
});
