import { test, expect } from '@playwright/test';
import { CORS, PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

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

test('owner sets general instructions once; each new visit request shows that they apply', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await expect(page.getByText('Account details', { exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Billing name')).toHaveCount(0);
  await page.getByLabel('General instructions', { exact: true }).fill('Use the side door. Key safe code 4321.');
  await page.getByRole('button', { name: 'Save photo sections', exact: true }).click();
  await expect(page.getByText('Photo sections saved. New visits will require them.', { exact: true })).toBeVisible();
  expect(backend.generalInstructions).toBe('Use the side door. Key safe code 4321.');

  await page.reload();
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await expect(page.getByRole('textbox', { name: 'General instructions', exact: true })).toHaveValue('Use the side door. Key safe code 4321.');

  await page.getByRole('tab', { name: /^Properties/ }).click();
  await page.getByRole('button', { name: 'Request cleaning', exact: true }).click();
  await expect(page.getByText('The cleaner reads your general instructions first, then the instructions for this visit.', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Cleaning instructions', { exact: true })).toHaveValue('');
});

test('saving photo sections keeps the stored account details', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.account = { billingName: 'Olivia Oy', contactEmail: 'olivia@example.test' };
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await page.getByRole('button', { name: 'Add Keys', exact: true }).click();
  await page.getByRole('button', { name: 'Save photo sections', exact: true }).click();
  await expect(page.getByText('Photo sections saved. New visits will require them.', { exact: true })).toBeVisible();
  expect(backend.photoLabels).toEqual(['Keys']);
  expect(backend.account).toMatchObject({ billingName: 'Olivia Oy', contactEmail: 'olivia@example.test' });
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

test('settings that fail to load block saving, so stored values are not overwritten', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.generalInstructions = 'Stored general text';
  backend.photoLabels = ['Keys'];
  // Fail every settings read until the test lets it through, so the outcome does not depend on how many attempts run.
  await prepareRolePage(page, backend, 'owner');
  let failing = true;
  await page.route(/\/api\/v1\/partner\/settings$/, route => {
    if (failing && route.request().method() === 'GET') return route.fulfill({ status: 500, headers: CORS, json: { detail: 'Settings unavailable' } });
    return route.fallback();
  });

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Your visit photos and general instructions could not be loaded' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save photo sections', exact: true })).toBeDisabled();

  failing = false;
  await page.getByRole('button', { name: 'Refresh partner data', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'General instructions', exact: true })).toHaveValue('Stored general text');
  await expect(page.getByRole('alert').filter({ hasText: 'Your visit photos and general instructions could not be loaded' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Save photo sections', exact: true })).toBeEnabled();
  expect(backend.generalInstructions).toBe('Stored general text');
  expect(backend.photoLabels).toEqual(['Keys']);
});

test('unsaved general instructions survive a refresh of the portal', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.generalInstructions = 'Stored general text';
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await page.getByRole('textbox', { name: 'General instructions', exact: true }).fill('Typed but not saved yet');
  await page.getByRole('button', { name: 'Refresh partner data', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'General instructions', exact: true })).toHaveValue('Typed but not saved yet');
  expect(backend.generalInstructions).toBe('Stored general text');
});

test('owner sets access instructions, and clearing them removes them for the cleaner', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await page.getByRole('textbox', { name: 'Access instructions for the cleaner', exact: true }).fill('Key safe 4321');
  await page.getByRole('button', { name: 'Save photo sections', exact: true }).click();
  await expect(page.getByText('Photo sections saved. New visits will require them.', { exact: true })).toBeVisible();
  expect(backend.account.accessInstructions).toBe('Key safe 4321');

  await page.reload();
  await page.getByRole('tab', { name: /^Settings/ }).click();
  await expect(page.getByRole('textbox', { name: 'Access instructions for the cleaner', exact: true })).toHaveValue('Key safe 4321');

  await page.getByRole('textbox', { name: 'Access instructions for the cleaner', exact: true }).fill('');
  await page.getByRole('button', { name: 'Save photo sections', exact: true }).click();
  await expect(page.getByText('Photo sections saved. New visits will require them.', { exact: true })).toBeVisible();
  expect(backend.account.accessInstructions).toBeFalsy();
});
