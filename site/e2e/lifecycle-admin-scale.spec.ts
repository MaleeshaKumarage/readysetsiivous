import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { buildScaleData, installScaleRoutes, integrityProblems, SCALE } from './admin-scale-data';

// Fixture shape: 60 companies, 2–4 branches each, one shift per branch, one invoice per company.
test('generated tenant data is internally consistent at scale', () => {
  const data = buildScaleData();
  expect(data.companies).toHaveLength(SCALE.companies);
  expect(data.branches.length).toBeGreaterThanOrEqual(SCALE.companies * SCALE.branchesMin);
  expect(data.shifts).toHaveLength(data.branches.length);
  expect(data.invoices).toHaveLength(SCALE.companies);
  expect(integrityProblems(data)).toEqual([]);
});

test('companies list pages through every active company at scale', async ({ page }) => {
  const data = buildScaleData();
  await setupAdminMocks(page);
  await installScaleRoutes(page, data);
  await page.goto('/admin/companies/');
  // Inactive companies are excluded from the default Active view; the first active company is Company 02.
  await expect(page.getByText('Company 02 Ltd', { exact: true })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText('Company 60 Ltd', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Next page', exact: true }).click();
  await expect(page.getByText('Company 60 Ltd', { exact: true })).toBeVisible();
  await expect(page.getByText('Company 02 Ltd', { exact: true })).toHaveCount(0);
});

test('invoices list renders every generated invoice and its table stays reachable', async ({ page }) => {
  const data = buildScaleData();
  await setupAdminMocks(page);
  await installScaleRoutes(page, data);
  await page.goto('/admin/invoices/');
  await expect(page.getByRole('heading', { name: 'Issued invoices', exact: true })).toBeAttached({ timeout: 15000 });
  await expect(page.getByRole('row').filter({ has: page.getByRole('gridcell') })).toHaveCount(SCALE.companies);
  await expect(page.getByRole('row').filter({ has: page.getByRole('gridcell') }).filter({ hasText: data.invoices[0].invoiceNumber })).toHaveCount(1);
  await page.getByRole('heading', { name: 'Issued invoices', exact: true }).scrollIntoViewIfNeeded();
  await expect(page.getByRole('heading', { name: 'Issued invoices', exact: true })).toBeVisible();
});

test('shifts list pages through every generated active shift', async ({ page }) => {
  const data = buildScaleData();
  const active = data.shifts.filter(shift => shift.isActive).length;
  await setupAdminMocks(page);
  await installScaleRoutes(page, data);
  await page.goto('/admin/shifts/');
  const pager = page.getByRole('navigation', { name: 'Shifts pages' });
  await expect(pager.getByText(`Showing 1–25 of ${active}`)).toBeVisible({ timeout: 15000 });
  await expect(pager.getByText(`Page 1 of ${Math.ceil(active / 25)}`)).toBeVisible();
  await pager.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(pager.getByText(`Showing 26–50 of ${active}`)).toBeVisible();
});

test('expanding a company lists exactly its own generated branches', async ({ page }) => {
  const data = buildScaleData();
  const company = data.companies.find(c => c.name === 'Company 02 Ltd')!;
  const branches = data.branches.filter(b => b.companyId === company.id);
  expect(branches.length).toBeGreaterThanOrEqual(SCALE.branchesMin);
  await setupAdminMocks(page);
  await installScaleRoutes(page, data);
  await page.goto('/admin/companies/');
  const row = page.getByRole('row').filter({ has: page.getByRole('gridcell') }).filter({ hasText: company.name }).first();
  await expect(row).toBeVisible({ timeout: 15000 });
  await row.getByRole('button', { name: 'Branches', exact: false }).click();
  // The expansion is the row directly under the company; scope every assertion to it so other companies' branches cannot match.
  const panel = row.locator('xpath=following-sibling::*[1]');
  await expect(panel.getByText('Branches', { exact: true })).toBeVisible();
  await expect(panel.getByText(/^Branch \d+ · Helsinki/)).toHaveCount(branches.length);
  for (const branch of branches) {
    await expect(panel.getByText(new RegExp(`^${branch.name} · Helsinki`))).toBeVisible();
  }
});
