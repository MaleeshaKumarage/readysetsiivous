import { test, expect } from '@playwright/test';
import { OWNER_COMPANY, PartnerFlowBackend, openActor, prepareRolePage } from './partner-flow-backend';

// Deep cleaning is visible to every role that books, staffs or does the visit.

const WINDOW = { fromDate: '2026-12-01', toDate: '2026-12-02' };

function pricedBackend() {
  const backend = new PartnerFlowBackend();
  const branch = backend.addBranch({ name: 'Lake apartment', companyId: OWNER_COMPANY.id, priceNet: 85, estimatedDurationMinutes: 90 });
  return { backend, branch };
}

test('staff see Deep cleaning and the surcharge on a partner request', async ({ page, browser }, testInfo) => {
  const { backend, branch } = pricedBackend();
  const request = backend.seedRequest(branch, WINDOW);
  request.cleaningType = 'Deep';
  const staff = await openActor(browser, testInfo, backend, 'admin');
  try {
    await staff.page.goto('/admin/partner-requests/');
    const card = staff.page.getByRole('article', { name: 'Lake apartment request', exact: true });
    await expect(card.getByTestId('request-cleaning-type')).toHaveText('Deep cleaning · adds €15.00 excl. VAT');
  } finally { await staff.context.close(); }
  void page;
});

test('a Normal request says Normal cleaning and adds no surcharge', async ({ browser }, testInfo) => {
  const { backend, branch } = pricedBackend();
  backend.seedRequest(branch, WINDOW);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  try {
    await staff.page.goto('/admin/partner-requests/');
    const card = staff.page.getByRole('article', { name: 'Lake apartment request', exact: true });
    await expect(card.getByTestId('request-cleaning-type')).toHaveText('Normal cleaning');
  } finally { await staff.context.close(); }
});

test('the cleaner sees Deep cleaning on the visit offer', async ({ page, browser }, testInfo) => {
  const { backend, branch } = pricedBackend();
  const request = backend.seedRequest(branch, WINDOW);
  request.cleaningType = 'Deep';
  backend.seedOffer(request, 'cleaner', null);
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  try {
    await cleaner.page.goto('/me/shifts/');
    await cleaner.page.getByRole('tab', { name: 'Offers', exact: true }).click();
    const offer = cleaner.page.getByRole('article', { name: 'Lake apartment offer', exact: true });
    await expect(offer.getByTestId('offer-cleaning-type')).toHaveText('Deep cleaning');
  } finally { await cleaner.context.close(); }
  void page;
});

test('two employees with the same name are told apart by email in the staff Arrange dialog', async ({ browser }, testInfo) => {
  const { backend, branch } = pricedBackend();
  backend.seedRequest(branch, WINDOW);
  const people = backend.people as Record<string, any>;
  people['dup-cleaner'] = { id: 'dup-cleaner', firstName: 'Test', lastName: 'Cleaner', role: 'employee', isActive: true, autoAcceptWork: false, keycloakUserId: 'kc-dup' };
  try {
    const staff = await openActor(browser, testInfo, backend, 'admin');
    try {
      await staff.page.goto('/admin/partner-requests/');
      await staff.page.getByRole('article', { name: 'Lake apartment request', exact: true }).getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
      const select = staff.page.getByLabel('Cleaner', { exact: true });
      await expect(select.locator('option', { hasText: 'Test Cleaner (cleaner@example.invalid)' })).toHaveCount(1);
      await expect(select.locator('option', { hasText: 'Test Cleaner (dup-cleaner@example.invalid)' })).toHaveCount(1);
    } finally { await staff.context.close(); }
  } finally { delete people['dup-cleaner']; }
});

test('the owner request form explains how to book one exact time', async ({ page }) => {
  const backend = new PartnerFlowBackend();
  backend.addBranch({ name: 'Lake apartment', companyId: OWNER_COMPANY.id, priceNet: 85, estimatedDurationMinutes: 90 });
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('article', { name: 'Lake apartment', exact: true }).getByRole('button', { name: 'Request cleaning', exact: true }).click();
  await expect(page.getByText('Choose the first and last day the cleaning can happen. To book one exact time, tick Fixed appointment, pick that day and enter its start time.', { exact: true })).toBeVisible();
});

test('the owner sees the Deep surcharge and the total under the agreed price', async ({ page }) => {
  const { backend, branch } = pricedBackend();
  const deep = backend.seedRequest(branch, WINDOW);
  deep.cleaningType = 'Deep';
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await expect(page.getByTestId('owner-deep-breakdown')).toHaveText('Deep cleaning surcharge €15.00 excl. VAT · Total €100.00 excl. VAT');
});

test('a Normal request on the owner card shows no surcharge line', async ({ page }) => {
  const { backend, branch } = pricedBackend();
  backend.seedRequest(branch, WINDOW);
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await expect(page.getByText('Agreed price').first()).toBeVisible();
  await expect(page.getByTestId('owner-deep-breakdown')).toHaveCount(0);
});

test('a cleaner offer with a set time says it is fixed and how to decline', async ({ page, browser }, testInfo) => {
  const { backend, branch } = pricedBackend();
  const request = backend.seedRequest(branch, WINDOW);
  backend.seedOffer(request, 'cleaner', '2026-12-01T10:00');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  try {
    await cleaner.page.goto('/me/shifts/');
    await cleaner.page.getByRole('tab', { name: 'Offers', exact: true }).click();
    const offer = cleaner.page.getByRole('article', { name: 'Lake apartment offer', exact: true });
    await expect(offer.getByTestId('offer-fixed-time')).toContainText('Fixed time:');
    await expect(offer.getByText('If you cannot attend, decline with a reason', { exact: false })).toBeVisible();
    await expect(offer.getByLabel(/^Planned start for/)).toHaveCount(0);
  } finally { await cleaner.context.close(); }
  void page;
});

