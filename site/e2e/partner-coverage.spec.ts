import { test, expect, type Page } from '@playwright/test';
import { chooseEmployeeSection } from './employee-section';
import { PartnerFlowBackend, openActor, prepareRolePage } from './partner-flow-backend';

// Coverage for the partner portal states that the flow does not visit: empty lists,
// in-progress saves, validation the browser enforces, error fallbacks and Finnish labels.

const WINDOW = { fromDate: '2026-12-01', toDate: '2026-12-02' };
const PROPERTY = 'Lake apartment';

async function ownerTab(page: Page, name: string) {
  await page.goto('/partner/');
  await page.getByRole('tab', { name, exact: true }).click();
}
async function staffTab(page: Page, name: string) {
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name, exact: true }).click();
}

// ----- owner empty states -----

test('owner sees a helpful empty state on every section before anything is booked', async ({ page }) => {
  const backend = new PartnerFlowBackend();
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await expect(page.getByText('No properties yet. Add a property and request a quote.', { exact: true })).toBeVisible();
  await ownerTab(page, 'Cleaning requests');
  await expect(page.getByText('No cleaning requests yet.', { exact: true })).toBeVisible();
  await ownerTab(page, 'Company shifts');
  await expect(page.getByText('No company shifts.', { exact: true })).toBeVisible();
  await ownerTab(page, 'Change requests');
  await expect(page.getByText('No change requests yet.', { exact: true })).toBeVisible();
  expect(backend.writes).toHaveLength(0);
});

test('owner property change request is sent to staff and the reply comes back on the change tab', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('article', { name: PROPERTY, exact: true }).getByRole('button', { name: 'Request property changes', exact: true }).click();
  await page.getByLabel('Describe the requested change', { exact: true }).fill('The bathroom needs a new mirror.');
  await page.getByRole('button', { name: 'Send change request', exact: true }).click();
  await expect(page.getByText('Change request sent to staff.', { exact: true })).toBeVisible();
  expect(backend.writes.at(-1)).toMatchObject({ actor: 'owner', path: `partner/branches/${backend.branches[0].id}/change-requests`, body: { message: 'The bathroom needs a new mirror.' } });

  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffTab(staff.page, 'Changes');
  await expect(staff.page.getByText('The bathroom needs a new mirror.', { exact: true })).toBeVisible();
  await staff.context.close();
});

test('owner portal shows that a save is in progress and blocks a second submission', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.writeDelayMs = 800;
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('article', { name: PROPERTY, exact: true }).getByRole('button', { name: 'Request property changes', exact: true }).click();
  await page.getByLabel('Describe the requested change', { exact: true }).fill('Please check the lock.');
  await page.getByRole('button', { name: 'Send change request', exact: true }).click();
  await expect(page.getByText('Saving changes…', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Refresh partner data', exact: true })).toBeDisabled();
  await expect(page.getByText('Change request sent to staff.', { exact: true })).toBeVisible();
  expect(backend.writes.filter(write => write.path.endsWith('/change-requests'))).toHaveLength(1);
});

test('owner and cleaner see a readable message when the server gives no detail', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('article', { name: PROPERTY, exact: true }).getByRole('button', { name: 'Request property changes', exact: true }).click();
  await page.getByLabel('Describe the requested change', { exact: true }).fill('Check the window.');
  backend.failNext('POST', /^partner\/branches\/.+\/change-requests$/, 500, '');
  await page.getByRole('button', { name: 'Send change request', exact: true }).click();
  await expect(page.locator('p[role="alert"]').filter({ hasText: 'Request failed (500). Refresh to check whether your change was saved before retrying.' })).toContainText('Request failed (500). Refresh to check whether your change was saved before retrying.');

  backend.seedOffer(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleaner.page.goto('/me/shifts/');
  await chooseEmployeeSection(cleaner.page, 'Offers');
  await expect(cleaner.page.getByRole('article', { name: `${PROPERTY} offer`, exact: true })).toBeVisible();
  backend.failNext('POST', /^me\/partner-offers\/.+\/decline$/, 500, '');
  await cleaner.page.getByRole('article', { name: `${PROPERTY} offer`, exact: true }).getByRole('button', { name: 'Decline cleaning', exact: true }).click();
  await expect(cleaner.page.locator('p[role="alert"]').filter({ hasText: 'Request failed (500).' })).toContainText('Request failed (500).');
  await cleaner.context.close();
});

// ----- staff empty states and in-progress saves -----

test('staff sees a clear empty state on every partner operations tab', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend();
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffTab(staff.page, 'Requests');
  await expect(staff.page.getByText('No requests waiting for a cleaner.', { exact: true })).toBeVisible();
  await staffTab(staff.page, 'Assigned');
  await expect(staff.page.getByText('No assigned visits.', { exact: true })).toBeVisible();
  await staffTab(staff.page, 'Quote requests');
  await expect(staff.page.getByText('No quotes awaiting pricing.', { exact: true })).toBeVisible();
  await staffTab(staff.page, 'Changes');
  await expect(staff.page.getByText('No change requests.', { exact: true })).toBeVisible();
  await staffTab(staff.page, 'Properties');
  await expect(staff.page.getByText('No partner properties.', { exact: true })).toBeVisible();
  await staff.context.close();
});

test('staff sees that a price change is saving and cannot submit it twice', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.writeDelayMs = 800;
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffTab(staff.page, 'Properties');
  await staff.page.getByRole('article').filter({ has: staff.page.getByRole('heading', { name: `${PROPERTY} · Owner company` }) })
    .getByRole('button', { name: 'Set cleaning price', exact: true }).click();
  await staff.page.getByLabel('Fixed cleaning price excl. VAT (€)', { exact: true }).fill('99');
  await staff.page.getByRole('button', { name: 'Save pricing', exact: true }).click();
  await expect(staff.page.getByText('Saving changes…', { exact: true })).toBeVisible();
  await expect(staff.page.getByRole('button', { name: 'Save pricing', exact: true })).toBeDisabled();
  await expect(staff.page.getByText('Pricing saved.', { exact: true })).toBeVisible();
  expect(backend.writes.filter(write => write.path.endsWith('/pricing'))).toHaveLength(1);
  await staff.context.close();
});

test('staff pricing rejects a negative price, a VAT rate above 100 and a zero duration before anything is sent', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.addBranch({ name: 'Forest flat', companyId: 'company-one', quoteRequestedUtc: '2026-11-02T09:00:00Z' });
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffTab(staff.page, 'Quote requests');
  await staff.page.getByRole('button', { name: 'Set cleaning price', exact: true }).click();
  const price = staff.page.getByLabel('Fixed cleaning price excl. VAT (€)', { exact: true });
  const vat = staff.page.getByLabel('VAT (%)', { exact: true });
  const duration = staff.page.getByLabel('Estimated duration (minutes)', { exact: true });
  for (const [field, value] of [[price, '-5'], [vat, '101'], [duration, '0']] as const) {
    await field.fill(value);
    expect(await field.evaluate(element => (element as HTMLInputElement).checkValidity())).toBe(false);
  }
  await staff.page.getByRole('button', { name: 'Save pricing', exact: true }).click({ force: true });
  expect(backend.writes.filter(write => write.path.endsWith('/pricing'))).toHaveLength(0);
  await staff.context.close();
});

test('staff edit refuses a window that ends before it starts with a readable message', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffTab(staff.page, 'Requests');
  await staff.page.getByRole('article', { name: `${PROPERTY} request`, exact: true }).getByRole('button', { name: 'Edit / reschedule request', exact: true }).click();
  const editor = staff.page.getByRole('dialog', { name: /^Edit \/ reschedule/ });
  await editor.getByLabel('From date', { exact: true }).fill('2026-12-05');
  await editor.getByLabel('To date', { exact: true }).fill('2026-12-03');
  await editor.getByRole('button', { name: 'Save request changes', exact: true }).click();
  await expect(staff.page.locator('p[role="alert"]').filter({ hasText: 'The end date must be on or after the start date.' })).toContainText('The end date must be on or after the start date.');
  expect(backend.writes.filter(write => write.path.endsWith(backend.requests[0].id))).toHaveLength(0);
  await expect(editor).toBeVisible();
  await staff.context.close();
});

// ----- cleaner: accepted flexible visit and the Finnish partner portal -----

test('owner in Finnish sees a flexible visit described in Finnish', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', null);
  await page.addInitScript(() => localStorage.setItem('readyset:partner-language', 'fi'));
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Siivouspyynnöt', exact: true }).click();
  await expect(page.getByText('Suunniteltu: Joustava, päivien sisällä', { exact: true })).toBeVisible();
});
