import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };

// Sixty active cleaners and five inactive ones, so the active tab spans three pages.
const employees = [
  ...Array.from({ length: 60 }, (_, i) => ({ id: `active-${i}`, firstName: `Active${String(i).padStart(2, '0')}`, lastName: 'Cleaner', email: `active${i}@example.test`, role: 'employee', isActive: true, identitySyncPending: false, skills: [], serviceAreas: [], defaultHours: {} })),
  ...Array.from({ length: 5 }, (_, i) => ({ id: `inactive-${i}`, firstName: `Former${i}`, lastName: 'Cleaner', email: `former${i}@example.test`, role: 'employee', isActive: false, identitySyncPending: false, skills: [], serviceAreas: [], defaultHours: {} })),
];

async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/employees**', route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ headers, json: employees });
  });
  await page.goto('/admin/employees/');
  await expect(page.getByText('Showing 1–25 of 60 employees')).toBeVisible();
}

test('the active list shows 25 employees a page with Previous and Next controls', async ({ page }) => {
  await setup(page);
  const nav = page.getByRole('navigation', { name: 'Employee pages' });
  await expect(page.getByRole('gridcell', { name: 'Active00', exact: false }).first()).toBeVisible();
  await expect(page.getByText('Active24', { exact: false }).first()).toBeVisible();
  await expect(page.getByText('Active25', { exact: false })).toHaveCount(0);
  await expect(nav.getByRole('button', { name: 'Previous page' })).toBeDisabled();
  await nav.getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByText('Showing 26–50 of 60 employees')).toBeVisible();
  await expect(page.getByText('Page 2 of 3')).toBeVisible();
  await expect(page.getByText('Active25', { exact: false }).first()).toBeVisible();
  await nav.getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByText('Showing 51–60 of 60 employees')).toBeVisible();
  await expect(nav.getByRole('button', { name: 'Next page' })).toBeDisabled();
  await nav.getByRole('button', { name: 'Previous page' }).click();
  await expect(page.getByText('Page 2 of 3')).toBeVisible();
});

test('switching tabs returns to the first page of the new list', async ({ page }) => {
  await setup(page);
  await page.getByRole('navigation', { name: 'Employee pages' }).getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByText('Showing 26–50 of 60 employees')).toBeVisible();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  // Five inactive employees fit on one page, so the pager is hidden and the list starts at the top.
  await expect(page.getByRole('navigation', { name: 'Employee pages' })).toHaveCount(0);
  await expect(page.getByText('Former0', { exact: false }).first()).toBeVisible();
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByText('Showing 1–25 of 60 employees')).toBeVisible();
});
