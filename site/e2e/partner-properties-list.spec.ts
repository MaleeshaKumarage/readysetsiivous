import { test, expect, type Page } from '@playwright/test';
import { OWNER_COMPANY, PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

// Properties are listed in rows. The city filter offers every city the owner's properties are in.

const cityFilter = (page: Page) => page.getByRole('combobox', { name: 'Filter by city', exact: true });

function seedProperties(backend: PartnerFlowBackend) {
  backend.addBranch({ name: 'Lake apartment', companyId: OWNER_COMPANY.id, city: 'Lahti', priceNet: 85, estimatedDurationMinutes: 90 });
  backend.addBranch({ name: 'Harbour flat', companyId: OWNER_COMPANY.id, city: 'Lappeenranta' });
  backend.addBranch({ name: 'Station studio', companyId: OWNER_COMPANY.id, city: 'Lahti' });
  backend.addBranch({ name: 'Canal house', companyId: OWNER_COMPANY.id, city: 'Imatra' });
}

test('properties are listed in rows with a city filter built from their cities', async ({ page }) => {
  const backend = new PartnerFlowBackend();
  seedProperties(backend);
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await expect(page.getByRole('article', { name: 'Lake apartment', exact: true })).toBeVisible();
  await expect(page.locator('ul.partner-property-list > li')).toHaveCount(4);
  await expect(cityFilter(page).locator('option')).toHaveText(['All cities', 'Imatra', 'Lahti', 'Lappeenranta']);
});

test('choosing a city lists only its properties, and All cities restores the full list', async ({ page }) => {
  const backend = new PartnerFlowBackend();
  seedProperties(backend);
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await cityFilter(page).selectOption('Lahti');
  await expect(page.locator('ul.partner-property-list > li')).toHaveCount(2);
  await expect(page.getByRole('article', { name: 'Lake apartment', exact: true })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Station studio', exact: true })).toBeVisible();
  await expect(page.getByRole('article', { name: 'Harbour flat', exact: true })).toHaveCount(0);

  // Each row keeps its actions: a priced property can still start a cleaning request.
  await expect(page.getByRole('article', { name: 'Lake apartment', exact: true }).getByRole('button', { name: 'Request cleaning' })).toBeEnabled();

  await cityFilter(page).selectOption('');
  await expect(page.locator('ul.partner-property-list > li')).toHaveCount(4);
});
