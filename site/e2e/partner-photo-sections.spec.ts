import { test, expect } from '@playwright/test';
import { PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

// The owner sets the photo sections once, in Settings. Every visit requested afterwards shows them.

test('owner sets the photo sections once, and each new visit request shows them', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await page.getByRole('button', { name: 'Add Before', exact: true }).click();
  await page.getByRole('button', { name: 'Add Keys', exact: true }).click();
  await page.getByLabel('Photo section name', { exact: true }).fill('Balcony');
  await page.getByRole('button', { name: 'Add section', exact: true }).click();
  await page.getByRole('button', { name: 'Save photo sections', exact: true }).click();
  await expect(page.getByText('Photo sections saved. New visits will require them.', { exact: true })).toBeVisible();
  expect(backend.photoLabels).toEqual(['Before', 'Keys', 'Balcony']);

  await page.getByRole('tab', { name: /^Properties/ }).click();
  await page.getByRole('button', { name: 'Request cleaning', exact: true }).click();
  await expect(page.getByText('Photos the cleaner must attach: Before, Keys, Balcony', { exact: true })).toBeVisible();
});

test('a photo section cannot be listed twice, even with different capitals', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await page.getByRole('button', { name: 'Add Before', exact: true }).click();
  await page.getByLabel('Photo section name', { exact: true }).fill('before');
  await expect(page.getByRole('button', { name: 'Add section', exact: true })).toBeDisabled();
});
