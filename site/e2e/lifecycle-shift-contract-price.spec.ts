import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, shiftsResponse } from './admin-helpers';
import { selectDailyScheduleIfEmpty } from './admin-helpers';

// A shift can carry its own contract price: a monthly fee, a price per completed visit, or the property price.
// Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const company = { id: 'company', name: 'Methehe', isActive: true, businessId: '123' };
const branch = { id: 'branch', companyId: 'company', name: 'Main', isActive: true, city: 'Helsinki', priceNet: 120, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true,
  priceVersions: [{ effectiveFrom: '2020-01-01', mode: 'Monthly', priceNet: 120, vatRatePercent: 25.5, durationMinutes: 120 }] };

async function openCreate(page: Page) {
  await setupAdminMocks(page);
  const created: any[] = [];
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [company], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/companies\/company$/, route => route.fulfill({ headers, json: { company, branches: [branch] } }));
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts(?:\/page)?(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'POST') { const body = route.request().postDataJSON(); created.push(body); return route.fulfill({ status: 201, headers, json: { ...body, id: 'created', isActive: true } }); }
    return route.fulfill({ headers, json: shiftsResponse([], route.request().url()) });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'New work order', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'New work order' });
  await modal.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'Methehe', exact: true }).click();
  await modal.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'Main', exact: true }).click();
  await selectDailyScheduleIfEmpty(page);
  return { modal, created };
}

test('a shift with a monthly fee sends the fee, its VAT and the monthly mode', async ({ page }) => {
  const { modal, created } = await openCreate(page);
  await modal.getByRole('textbox', { name: 'Price for this shift', exact: true }).click();
  await page.getByRole('option', { name: 'Monthly fee', exact: true }).click();
  await modal.getByLabel('Monthly fee excl. VAT (€)', { exact: true }).fill('300');
  await modal.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('dialog', { name: 'New work order' })).toHaveCount(0);
  expect(created).toEqual([expect.objectContaining({ pricingMode: 0, rateNet: 300, vatRatePercent: 25.5 })]);
});

test('a shift priced per completed visit sends the per-visit mode', async ({ page }) => {
  const { modal, created } = await openCreate(page);
  await modal.getByRole('textbox', { name: 'Price for this shift', exact: true }).click();
  await page.getByRole('option', { name: 'Price per completed visit', exact: true }).click();
  await modal.getByLabel('Price per visit excl. VAT (€)', { exact: true }).fill('40');
  await modal.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('dialog', { name: 'New work order' })).toHaveCount(0);
  expect(created).toEqual([expect.objectContaining({ pricingMode: 1, rateNet: 40 })]);
});

test('a shift with the property price sends no shift price', async ({ page }) => {
  const { modal, created } = await openCreate(page);
  await modal.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('dialog', { name: 'New work order' })).toHaveCount(0);
  expect(created).toEqual([expect.objectContaining({ pricingMode: null, rateNet: null, vatRatePercent: null })]);
});

test('a shift price needs an amount above zero before anything is saved', async ({ page }) => {
  const { modal, created } = await openCreate(page);
  await modal.getByRole('textbox', { name: 'Price for this shift', exact: true }).click();
  await page.getByRole('option', { name: 'Monthly fee', exact: true }).click();
  await modal.getByLabel('Monthly fee excl. VAT (€)', { exact: true }).fill('0');
  await modal.getByRole('button', { name: 'Create shift' }).click();
  await expect(modal.getByText('Enter a price above zero and a VAT rate from 0 to 100.')).toBeVisible();
  expect(created).toEqual([]);
});
