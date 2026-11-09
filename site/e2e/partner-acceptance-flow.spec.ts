import { test, expect, type Page } from '@playwright/test';
import { OTHER_COMPANY, PartnerFlowBackend, openActor, prepareRolePage, type WriteRecord } from './partner-flow-backend';

// One visit travels through every role: owner portal → staff partner operations → cleaner offers.
// Each test owns a fresh fake backend, so the flow test and the edge cases never share state.

const PROPERTY = 'Lake apartment';
const WINDOW = { fromDate: '2026-12-01', toDate: '2026-12-02' };
const PLANNED = '2026-12-01T10:00';
const OVERRUN = '2026-12-02T23:30';

const ownerCard = (page: Page) => page.getByRole('article', { name: `${PROPERTY} ${WINDOW.fromDate}`, exact: true });
const staffCard = (page: Page) => page.getByRole('article', { name: `${PROPERTY} request`, exact: true });
const cleanerOffer = (page: Page) => page.getByRole('article', { name: `${PROPERTY} offer`, exact: true });

async function ownerOpenRequests(page: Page) {
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
}
async function staffOpenRequests(page: Page) {
  await page.goto('/admin/partner-requests/');
}
// Cancelled owner bookings leave the active list and open from the collapsed section.
async function ownerOpenCancelled(page: Page) {
  await page.getByRole('tab', { name: /^Cancelled requests/ }).click();
}
async function staffOpenAssigned(page: Page) {
  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Assigned', exact: true }).click();
}
async function cleanerOpenOffers(page: Page) {
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Offers', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Cleaning offers' })).toBeVisible();
}
async function staffArrange(page: Page, employeeId: string, plannedStartLocal?: string) {
  await staffCard(page).getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
  await page.getByLabel('Cleaner', { exact: true }).selectOption(employeeId);
  if (plannedStartLocal) await page.getByLabel(/^Planned start \(tenant local time/).fill(plannedStartLocal);
  await page.getByRole('button', { name: 'Send cleaning offer', exact: true }).click();
}
// Write paths with server-generated identifiers replaced, so sequences can be compared exactly.
const shape = (writes: WriteRecord[]) => writes.map(write => `${write.actor} ${write.method} ${write.path.replace(/[a-z]+-\d+(-\d+)?/g, '<id>')}`);

// ----- the flow -----

test('one visit from owner request to cleaner acceptance and back to the owner', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend();
  await prepareRolePage(page, backend, 'owner');
  const staff = await openActor(browser, testInfo, backend, 'admin');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  try {
    // 1. Owner adds a property and asks staff for a quote.
    await page.goto('/partner/');
    await page.getByRole('button', { name: 'Add property', exact: true }).click();
    await page.getByLabel('Property name', { exact: true }).fill(PROPERTY);
    await page.getByLabel('Street', { exact: true }).fill('Lake road 1');
    await page.getByLabel('Postal code', { exact: true }).fill('33100');
    await page.getByLabel('City', { exact: true }).fill('Tampere');
    await page.getByLabel('Rooms (optional)', { exact: true }).fill('2');
    await page.getByLabel('Beds (optional)', { exact: true }).fill('3');
    await page.getByRole('button', { name: 'Save property', exact: true }).click();
    await expect(page.getByText('Property created. Request a quote to enable cleaning requests.', { exact: true })).toBeVisible();
    const property = page.getByRole('article', { name: PROPERTY, exact: true });
    await property.getByRole('button', { name: 'Request quote', exact: true }).click();
    await expect(page.getByText('Quote requested.', { exact: true })).toBeVisible();
    await expect(property.getByText('Quote requested — awaiting staff pricing', { exact: true })).toBeVisible();
    await expect(property.getByRole('button', { name: 'Request cleaning', exact: true })).toBeDisabled();

    // 2. Staff sets the fixed price from the quote queue.
    await staffOpenRequests(staff.page);
    await expect(staff.page.getByText('1 branch awaiting a quote', { exact: true })).toBeVisible();
    await staff.page.getByRole('link', { name: 'Review quotes', exact: true }).click();
    await expect(staff.page).toHaveURL(/tab=quotes/);
    await expect(staff.page.getByRole('tab', { name: 'Quote requests', exact: true })).toHaveAttribute('aria-selected', 'true');
    await expect(staff.page.getByText('Rooms: 2 · Beds: 3')).toBeVisible();
    await staff.page.getByRole('button', { name: 'Set cleaning price', exact: true }).click();
    await staff.page.getByLabel('Fixed cleaning price excl. VAT (€)', { exact: true }).fill('85');
    await staff.page.getByLabel('Estimated duration (minutes)', { exact: true }).fill('90');
    await staff.page.getByRole('button', { name: 'Save pricing', exact: true }).click();
    await expect(staff.page.getByText('Pricing saved.', { exact: true })).toBeVisible();
    await expect(staff.page.getByText('branch awaiting a quote', { exact: false })).toHaveCount(0);

    // 3. Owner sees the price and books one visit inside the date window.
    await ownerOpenRequests(page);
    await page.getByRole('tab', { name: 'Properties', exact: true }).click();
    await expect(property.getByText('€85.00', { exact: false })).toBeVisible();
    await property.getByRole('button', { name: 'Request cleaning', exact: true }).click();
    await page.getByLabel('From date', { exact: true }).fill(WINDOW.fromDate);
    await page.getByLabel('To date', { exact: true }).fill(WINDOW.toDate);
    await page.getByLabel('Cleaning instructions', { exact: true }).fill('Clean after checkout.');
    await page.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
    await expect(page.getByText('Cleaning request saved. Staff will arrange a cleaner.', { exact: true })).toBeVisible();
    await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
    await expect(ownerCard(page).getByText('Requested', { exact: true })).toBeVisible();
    await expect(ownerCard(page).getByText('Cleaner:', { exact: false })).toHaveCount(0); // partners never see the cleaner

    // 4. Staff arranges an offer to a cleaner who chooses the time themselves.
    await staffOpenRequests(staff.page);
    await expect(staffCard(staff.page).getByText(`${PROPERTY} · Requested`, { exact: true })).toBeVisible();
    await expect(staffCard(staff.page).getByText('Cleaner: Not accepted')).toBeVisible();
    await staffArrange(staff.page, 'cleaner');
    await expect(staff.page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();
    await expect(staffCard(staff.page).getByText(`${PROPERTY} · Offered`, { exact: true })).toBeVisible();

    // 5. Owner sees the offer is out, but the cleaner is not confirmed yet.
    await ownerOpenRequests(page);
    await expect(ownerCard(page).getByText('Offered', { exact: true })).toBeVisible();
    await expect(ownerCard(page).getByText('Cleaner:', { exact: false })).toHaveCount(0); // partners never see the cleaner

    // 6. Cleaner accepts with a planned start inside the window.
    await cleanerOpenOffers(cleaner.page);
    const offer = cleanerOffer(cleaner.page);
    await expect(offer.getByText('2026-12-01 – 2026-12-02 · Estimated 90 minutes · Europe/Helsinki')).toBeVisible();
    await offer.getByLabel(`Planned start for ${PROPERTY}`, { exact: true }).fill(PLANNED);
    await offer.getByRole('button', { name: 'Accept cleaning', exact: true }).click();
    await expect(cleaner.page.getByRole('status').filter({ hasText: 'Cleaning accepted' })).toBeVisible();
    await expect(cleaner.page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
    expect(backend.writes.at(-1)).toMatchObject({ actor: 'cleaner', method: 'POST', body: { version: 2, plannedStartLocal: PLANNED } });

    // 7. Staff sees the accepted cleaner on the Assigned tab, not in the waiting queue.
    await staffOpenRequests(staff.page);
    await expect(staffCard(staff.page)).toHaveCount(0);
    await staffOpenAssigned(staff.page);
    await expect(staffCard(staff.page).getByText(`${PROPERTY} · Accepted`, { exact: true })).toBeVisible();
    await expect(staffCard(staff.page).getByText('Cleaner: Test Cleaner')).toBeVisible();

    // 8. Owner sees the cleaner and planned time, and the request is locked to staff changes.
    await ownerOpenRequests(page);
    await expect(ownerCard(page).getByText('Accepted', { exact: true })).toBeVisible();
    await expect(ownerCard(page).getByText('Cleaner:', { exact: false })).toHaveCount(0);
    await expect(ownerCard(page).getByText('Test Cleaner')).toHaveCount(0);
    await expect(ownerCard(page).getByText(/Planned: .*10:00/)).toBeVisible();
    await expect(ownerCard(page).getByRole('button', { name: 'Edit request', exact: true })).toHaveCount(0);
    await expect(ownerCard(page).getByRole('button', { name: 'Cancel request', exact: true })).toHaveCount(0);

    // 9. Owner asks staff for a later visit instead of editing the booking.
    await ownerCard(page).getByRole('button', { name: 'Request a change or cancellation', exact: true }).click();
    await page.getByLabel('Describe the requested change', { exact: true }).fill('Please arrange a later visit.');
    await page.getByRole('button', { name: 'Send change request', exact: true }).click();
    await expect(page.getByText('Change request sent to staff.', { exact: true })).toBeVisible();

    // 10. Staff resolves the change request.
    await staffOpenRequests(staff.page);
    await staff.page.getByRole('tab', { name: 'Changes', exact: true }).click();
    const change = staff.page.getByRole('article').filter({ hasText: 'Please arrange a later visit.' });
    await expect(change.getByText('Awaiting staff response')).toBeVisible();
    await change.getByRole('button', { name: 'Resolve change request', exact: true }).click();
    await staff.page.getByLabel('Resolution', { exact: true }).fill('Later visit is not available; the cleaner stays booked for 10:00.');
    await staff.page.getByRole('button', { name: 'Save resolution', exact: true }).click();
    await expect(staff.page.getByText('Change request resolved.', { exact: true })).toBeVisible();

    // 11. Owner reads the staff response and the company shift created by the acceptance.
    await ownerOpenRequests(page);
    await page.getByRole('tab', { name: 'Change requests', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Staff response' })).toBeVisible();
    await expect(page.getByText('Later visit is not available; the cleaner stays booked for 10:00.', { exact: true })).toBeVisible();
    await page.getByRole('tab', { name: 'Company shifts', exact: true }).click();
    await expect(page.getByRole('heading', { name: `Cleaning — ${PROPERTY}` })).toBeVisible();

    // Every write happened exactly once, in order, by the expected actor.
    expect(shape(backend.writes)).toEqual([
      'owner POST partner/branches',
      'owner POST partner/branches/<id>/quote-request',
      'admin PUT admin/partner/branches/<id>/pricing',
      'owner POST partner/requests',
      'admin POST admin/partner/requests/<id>/offer',
      'cleaner POST me/partner-offers/offer-<id>/accept',
      'owner POST partner/requests/<id>/change-requests',
      'admin POST admin/partner/change-requests/<id>/resolve',
    ]);
    expect(backend.writes.every(write => write.status === 200)).toBe(true);
    expect(Object.keys(backend.writes.find(write => write.path === 'partner/requests')!.body).sort())
      .toEqual(['branchId', 'clientRequestId', 'exactStartLocalTime', 'fromDate', 'notes', 'toDate']);
    expect(backend.writes.find(write => write.path.endsWith('/offer'))!.body).toEqual({ version: 1, employeeId: 'cleaner', plannedStartLocal: null });
  } finally {
    await staff.context.close();
    await cleaner.context.close();
  }
});

// ----- property creation and quote requests -----

test('owner property creation explains missing and invalid fields and saves exactly once', async ({ page }) => {
  const backend = new PartnerFlowBackend();
  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('button', { name: 'Add property', exact: true }).click();
  await page.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Complete the required fields before saving.');
  expect(backend.writes).toHaveLength(0);

  await page.getByLabel('Property name', { exact: true }).fill('Harbour flat');
  await page.getByLabel('Contact email', { exact: true }).fill('owner-at-example');
  await page.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Enter a valid property contact email.');
  expect(backend.writes).toHaveLength(0);
  await expect(page.getByLabel('Property name', { exact: true })).toHaveValue('Harbour flat');

  await page.getByLabel('Contact email', { exact: true }).fill('owner@example.invalid');
  await page.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(page.getByText('Property created. Request a quote to enable cleaning requests.', { exact: true })).toBeVisible();
  expect(backend.branches.filter(branch => branch.name === 'Harbour flat')).toHaveLength(1);
  await expect(page.getByRole('article', { name: 'Harbour flat', exact: true }).getByRole('button', { name: 'Request quote', exact: true })).toBeEnabled();
});

test('a property with a quote request cannot be quoted twice and the staff queue shows how long it has waited', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend();
  backend.addBranch({ name: 'Forest flat', companyId: 'company-one', quoteRequestedUtc: '2026-11-02T09:00:00Z' });
  backend.addBranch({ name: 'Lake apartment', companyId: 'company-one', quoteRequestedUtc: '2026-10-28T09:00:00Z' });
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staff.page.getByRole('tab', { name: 'Quote requests', exact: true }).click();
  const queue = staff.page.getByRole('article');
  await expect(queue).toHaveCount(2);
  await expect(queue.nth(0).getByRole('heading', { name: 'Lake apartment', exact: true })).toBeVisible();
  await expect(queue.nth(0).getByText('Waiting since 28 Oct 2026')).toBeVisible();
  await expect(queue.nth(1).getByText('Waiting since 2 Nov 2026')).toBeVisible();

  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  const forest = page.getByRole('article', { name: 'Forest flat', exact: true });
  await expect(forest.getByRole('button', { name: 'Request quote', exact: true })).toBeDisabled();
  await expect(forest.getByText('Quote requested — awaiting staff pricing', { exact: true })).toBeVisible();
  expect(backend.writes.filter(write => write.path.endsWith('/quote-request'))).toHaveLength(0);
  await staff.context.close();
});

// ----- edge cases, each from a seeded state -----

test('a staff offer built on an outdated request is refused and succeeds after reloading', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], { ...WINDOW, notes: 'Before checkout.' });
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await expect(staffCard(staff.page).getByText(`${PROPERTY} · Requested`, { exact: true })).toBeVisible();

  // Owner changes the instructions while staff still holds version 1.
  await prepareRolePage(page, backend, 'owner');
  await ownerOpenRequests(page);
  await ownerCard(page).getByRole('button', { name: 'Edit request', exact: true }).click();
  await page.getByRole('textbox', { name: 'Cleaning instructions', exact: true }).fill('After checkout, from 15:00.');
  await page.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(page.getByText('Cleaning request saved. Staff will arrange a cleaner.', { exact: true })).toBeVisible();

  await staffArrange(staff.page, 'cleaner');
  await expect(staff.page.locator('p[role="alert"]')).toContainText('This request changed. Reload it before making another change.');
  expect(backend.requests[0]).toMatchObject({ status: 'Requested', offeredEmployeeId: null, version: 2 });

  await staffOpenRequests(staff.page);
  await staffArrange(staff.page, 'cleaner');
  await expect(staff.page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();
  expect(backend.writes.filter(write => write.path.endsWith('/offer')).map(write => [write.body.version, write.status])).toEqual([[1, 409], [2, 200]]);
  await staff.context.close();
});

test('rescheduling an offered visit withdraws the cleaner offer and a stale acceptance is refused', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedOffer(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleanerOpenOffers(cleaner.page);
  await expect(cleanerOffer(cleaner.page)).toBeVisible();

  // Staff reschedules while the cleaner still sees the old offer.
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staffCard(staff.page).getByRole('button', { name: 'Edit / reschedule request', exact: true }).click();
  await staff.page.getByLabel('From date', { exact: true }).fill('2026-12-03');
  await staff.page.getByLabel('To date', { exact: true }).fill('2026-12-04');
  await staff.page.getByRole('button', { name: 'Save request changes', exact: true }).click();
  await expect(staff.page.getByText('Request updated. Arrange a new cleaner offer.', { exact: true })).toBeVisible();
  expect(backend.request(backend.requests[0].id)).toMatchObject({ status: 'Requested', offeredEmployeeId: null, offerId: null });

  // The stale offer can no longer be accepted.
  const offer = cleanerOffer(cleaner.page);
  await offer.getByLabel(`Planned start for ${PROPERTY}`, { exact: true }).fill(PLANNED);
  await offer.getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect(cleaner.page.locator('p[role="alert"]')).toBeVisible();
  expect(backend.requests[0].acceptedEmployeeId).toBeNull();
  await cleaner.page.getByRole('button', { name: 'Refresh offers', exact: true }).click();
  await expect(cleaner.page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();

  await staff.context.close();
  await cleaner.context.close();
});

test('cleaner declines with a reason; an over-long reason is refused and the visit returns to staff', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedOffer(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await cleanerOpenOffers(cleaner.page);

  await cleanerOffer(cleaner.page).getByLabel('Decline reason (optional)', { exact: true }).fill('x'.repeat(1001));
  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Decline cleaning', exact: true }).click();
  await expect(cleaner.page.locator('p[role="alert"]')).toContainText('Use at most 1000 characters for the reason.');
  await expect(cleanerOffer(cleaner.page)).toBeVisible();

  await cleanerOffer(cleaner.page).getByLabel('Decline reason (optional)', { exact: true }).fill('Already booked that morning.');
  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Decline cleaning', exact: true }).click();
  await expect(cleaner.page.getByRole('status').filter({ hasText: 'Offer declined' })).toBeVisible();
  await expect(cleaner.page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
  expect(backend.writes.filter(write => write.path.includes('/decline')).map(write => write.body)).toEqual([
    { version: 2, reason: 'x'.repeat(1001) },
    { version: 2, reason: 'Already booked that morning.' },
  ]);

  await staffOpenRequests(staff.page);
  await expect(staffCard(staff.page).getByText(`${PROPERTY} · Requested`, { exact: true })).toBeVisible();
  await expect(staffCard(staff.page).getByRole('button', { name: 'Arrange cleaner / time', exact: true })).toBeVisible();
  await prepareRolePage(page, backend, 'owner');
  await ownerOpenRequests(page);
  await expect(ownerCard(page).getByText('Requested', { exact: true })).toBeVisible();
  await staff.context.close();
  await cleaner.context.close();
});

test('an automatic-acceptance cleaner takes a flexible visit with no start time, and the owner sees it as flexible', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staffCard(staff.page).getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
  await staff.page.getByLabel('Cleaner', { exact: true }).selectOption('auto-cleaner');
  const start = staff.page.getByLabel(/^Planned start \(tenant local time, optional\)/);
  await expect(start).not.toHaveAttribute('required', '');
  await expect(staff.page.getByText('This cleaner accepts automatically. Set a start time, or leave it blank for a flexible visit.', { exact: true })).toBeVisible();
  await staff.page.getByRole('button', { name: 'Send cleaning offer', exact: true }).click();
  await expect(staff.page.getByText('Work scheduled automatically.', { exact: true })).toBeVisible();
  expect(backend.writes.find(write => write.path.endsWith('/offer'))!.body).toEqual({ version: 1, employeeId: 'auto-cleaner', plannedStartLocal: null });
  expect(backend.requests[0]).toMatchObject({ status: 'Accepted', plannedStartUtc: null, acceptedEmployeeId: 'auto-cleaner' });

  await staffOpenAssigned(staff.page);
  await expect(staffCard(staff.page).getByText(`${PROPERTY} · Accepted`, { exact: true })).toBeVisible();
  await expect(staffCard(staff.page).getByText('Cleaner: Auto Cleaner')).toBeVisible();

  const autoCleaner = await openActor(browser, testInfo, backend, 'autoCleaner');
  await autoCleaner.page.goto('/me/shifts/');
  await autoCleaner.page.getByRole('tab', { name: 'Offers', exact: true }).click();
  await autoCleaner.page.waitForLoadState('networkidle');
  await expect(autoCleaner.page.getByRole('region', { name: 'Cleaning offers' })).toHaveCount(0);

  await prepareRolePage(page, backend, 'owner');
  await ownerOpenRequests(page);
  await expect(ownerCard(page).getByText('Accepted', { exact: true })).toBeVisible();
  await expect(ownerCard(page).getByText(/Planned: Flexible during the requested dates/)).toBeVisible();
  await staff.context.close();
  await autoCleaner.context.close();
});

test('a visit that runs past the requested dates is refused by staff and by the cleaner', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staffArrange(staff.page, 'cleaner', OVERRUN);
  await expect(staff.page.locator('p[role="alert"]')).toContainText('The entire visit must fit within the requested dates.');
  expect(backend.requests[0].status).toBe('Requested');

  // Staff leaves the start open so the cleaner chooses it.
  await staff.page.getByLabel(/^Planned start \(tenant local time/).fill('');
  await staff.page.getByRole('button', { name: 'Send cleaning offer', exact: true }).click();
  await expect(staff.page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();

  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleanerOpenOffers(cleaner.page);
  await cleanerOffer(cleaner.page).getByLabel(`Planned start for ${PROPERTY}`, { exact: true }).fill(OVERRUN);
  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect(cleaner.page.locator('p[role="alert"]')).toContainText('The entire visit must fit within the requested dates.');
  await expect(cleanerOffer(cleaner.page)).toBeVisible();
  expect(backend.requests[0].status).toBe('Offered');

  await cleanerOffer(cleaner.page).getByLabel(`Planned start for ${PROPERTY}`, { exact: true }).fill('2026-12-01T09:00');
  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect(cleaner.page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
  expect(backend.requests[0]).toMatchObject({ status: 'Accepted', plannedStartUtc: '2026-12-01T07:00:00.000Z' });
  await staff.context.close();
  await cleaner.context.close();
});

test('owner cancels an unaccepted request only after confirming, and the cleaner loses the offer', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedOffer(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleanerOpenOffers(cleaner.page);
  await prepareRolePage(page, backend, 'owner');
  await ownerOpenRequests(page);

  // Cancelling asks for confirmation on the card; keeping the request sends nothing.
  await ownerCard(page).getByRole('button', { name: 'Cancel request', exact: true }).click();
  await ownerCard(page).getByRole('button', { name: 'Keep request', exact: true }).click();
  await expect(ownerCard(page).getByText('Offered', { exact: true })).toBeVisible();
  expect(backend.writes.filter(write => write.path.endsWith('/cancel'))).toHaveLength(0);

  await ownerCard(page).getByRole('button', { name: 'Cancel request', exact: true }).click();
  await ownerCard(page).getByLabel('Reason for cancellation', { exact: true }).fill('Owner cancelled travel.');
  await ownerCard(page).getByRole('button', { name: 'Yes, cancel request', exact: true }).click();
  await expect(page.getByText('Request cancelled.', { exact: true })).toBeVisible();
  expect(backend.writes.find(write => write.path.endsWith('/cancel'))!.body).toEqual({ version: expect.any(Number), reason: 'Owner cancelled travel.' });
  await expect(ownerCard(page)).toHaveCount(0);
  await expect(page.getByRole('tab', { name: /^Active requests \(0\)/ })).toBeVisible();
  await ownerOpenCancelled(page);
  await expect(ownerCard(page).getByText('Cancelled', { exact: true })).toBeVisible();
  expect(shape(backend.writes.filter(write => write.path.endsWith('/cancel')))).toEqual(['owner POST partner/requests/<id>/cancel']);

  await cleaner.page.getByRole('button', { name: 'Refresh offers', exact: true }).click();
  await expect(cleaner.page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
  await cleaner.context.close();
});

test('staff cancellation of an accepted visit uses an inline reason, not a browser prompt, and releases the cleaner', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', PLANNED);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenAssigned(staff.page);
  await expect(staffCard(staff.page).getByText('Cleaner: Test Cleaner')).toBeVisible();

  const form = staff.page.getByRole('dialog', { name: /^Cancel / });
  await staffCard(staff.page).getByRole('button', { name: 'Cancel request', exact: true }).click();
  await expect(form).toBeVisible();
  // Focus moves into the dialog, so keyboard users start inside it.
  expect(await staff.page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]'))).toBe(true);
  const confirm = form.getByRole('button', { name: 'Confirm cancellation', exact: true });
  await expect(confirm).toBeDisabled();
  await form.getByLabel('Reason for cancellation', { exact: true }).fill('   ');
  await expect(confirm).toBeDisabled();
  await form.getByRole('button', { name: 'Keep request', exact: true }).click();
  await expect(form).toHaveCount(0);
  expect(backend.writes.filter(write => write.path.endsWith('/cancel'))).toHaveLength(0);

  await staffCard(staff.page).getByRole('button', { name: 'Cancel request', exact: true }).click();
  await form.getByLabel('Reason for cancellation', { exact: true }).fill('Owner closed the property.');
  await confirm.click();
  await expect(staff.page.getByText('Request cancelled.', { exact: true })).toBeVisible();
  await expect(staff.page.getByText('No assigned visits.', { exact: true })).toBeVisible();
  expect(backend.writes.find(write => write.path.endsWith('/cancel'))!.body).toEqual({ version: 3, reason: 'Owner closed the property.' });
  expect(backend.shiftActive[backend.requests[0].linkedShiftId!]).toBe(false);

  await prepareRolePage(page, backend, 'owner');
  await ownerOpenRequests(page);
  await expect(ownerCard(page)).toHaveCount(0);
  await ownerOpenCancelled(page);
  await expect(ownerCard(page).getByText('Cancelled', { exact: true })).toBeVisible();
  await staff.context.close();
});

test('re-offering an accepted visit names the booked cleaner before anything is removed', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', PLANNED);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenAssigned(staff.page);
  await staffCard(staff.page).getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
  const arrange = staff.page.getByRole('dialog', { name: /^Arrange / });
  await expect(arrange.getByText('Test Cleaner is booked on this visit. Sending a new offer removes them from it.', { exact: true })).toBeVisible();
  await arrange.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(arrange).toHaveCount(0);
  expect(backend.writes).toHaveLength(0);
  await expect(staffCard(staff.page).getByText(`${PROPERTY} · Accepted`, { exact: true })).toBeVisible();
  await staff.context.close();
});

test('staff deactivates a property from its editor, the owner is blocked from booking it, and staff can reactivate it', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staff.page.getByRole('tab', { name: 'Properties', exact: true }).click();
  const card = () => staff.page.getByRole('article').filter({ has: staff.page.getByRole('heading', { name: `${PROPERTY} · Owner company` }) });
  const editor = staff.page.getByRole('dialog', { name: 'Edit property' });

  await card().getByRole('button', { name: 'Edit property', exact: true }).click();
  await editor.getByLabel('Active property', { exact: true }).uncheck();
  await editor.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(staff.page.getByText('Property saved.', { exact: true })).toBeVisible();
  expect(backend.writes.at(-1)!.body).toMatchObject({ isActive: false });
  await expect(card().getByText('Inactive', { exact: true })).toBeVisible();

  await prepareRolePage(page, backend, 'owner');
  await page.goto('/partner/');
  await expect(page.getByRole('article', { name: PROPERTY, exact: true }).getByRole('button', { name: 'Request cleaning', exact: true })).toBeDisabled();

  await card().getByRole('button', { name: 'Edit property', exact: true }).click();
  await expect(editor.getByLabel('Active property', { exact: true })).not.toBeChecked();
  await editor.getByLabel('Active property', { exact: true }).check();
  await editor.getByRole('button', { name: 'Save property', exact: true }).click();
  await expect(staff.page.getByText('Property saved.', { exact: true })).toBeVisible();
  expect(backend.writes.at(-1)!.body).toMatchObject({ isActive: true });
  await expect(card().getByText('Inactive', { exact: true })).toHaveCount(0);
  await staff.context.close();
});

test('a failed acceptance is shown, the offer stays open and a single retry books the visit once', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedOffer(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner');
  backend.failNext('POST', /^me\/partner-offers\/.+\/accept$/, 503, 'Temporarily unavailable. Try again.');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleanerOpenOffers(cleaner.page);
  await cleanerOffer(cleaner.page).getByLabel(`Planned start for ${PROPERTY}`, { exact: true }).fill(PLANNED);
  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect(cleaner.page.locator('p[role="alert"]')).toContainText('Temporarily unavailable. Try again.');
  await expect(cleanerOffer(cleaner.page)).toBeVisible();

  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect(cleaner.page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
  const accepts = backend.writes.filter(write => write.path.endsWith('/accept'));
  expect(accepts.map(write => write.status)).toEqual([503, 200]);
  expect(backend.requests[0]).toMatchObject({ status: 'Accepted', version: 3 });
  await cleaner.context.close();
});

test('a double click on send offer creates one offer', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staffCard(staff.page).getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
  await staff.page.getByLabel('Cleaner', { exact: true }).selectOption('cleaner');
  await staff.page.getByRole('button', { name: 'Send cleaning offer', exact: true }).dblclick();
  await expect(staff.page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();
  await staff.page.waitForLoadState('networkidle');
  expect(backend.writes.filter(write => write.path.endsWith('/offer'))).toHaveLength(1);
  await staff.context.close();
});

test('a cleaner may accept a visit without a start time, which becomes flexible within the requested dates', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedOffer(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner');
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleanerOpenOffers(cleaner.page);
  await expect(cleanerOffer(cleaner.page).getByText('Accept cleaning saves this start time. Leave it blank to choose any time from 2026-12-01 to 2026-12-02. The visit takes 90 minutes and must finish by the end of 2026-12-02.', { exact: true })).toBeVisible();
  await cleanerOffer(cleaner.page).getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect(cleaner.page.getByRole('status').filter({ hasText: 'Cleaning accepted for Lake apartment. Choose your start any time from 2026-12-01 to 2026-12-02.' })).toBeVisible();
  expect(backend.writes.at(-1)).toMatchObject({ actor: 'cleaner', body: { version: 2, plannedStartLocal: null } });
  expect(backend.requests[0]).toMatchObject({ status: 'Accepted', plannedStartUtc: null });

  await prepareRolePage(page, backend, 'owner');
  await ownerOpenRequests(page);
  await expect(ownerCard(page).getByText(/Planned: Flexible during the requested dates/)).toBeVisible();
  await cleaner.context.close();
});

test('owner keeps the agreed price when staff reprice the property', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  await prepareRolePage(page, backend, 'owner');
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staff.page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await staff.page.getByRole('article').filter({ has: staff.page.getByRole('heading', { name: `${PROPERTY} · Owner company` }) })
    .getByRole('button', { name: 'Set cleaning price', exact: true }).click();
  await staff.page.getByLabel('Fixed cleaning price excl. VAT (€)', { exact: true }).fill('120');
  await staff.page.getByRole('button', { name: 'Save pricing', exact: true }).click();
  await expect(staff.page.getByText('Pricing saved.', { exact: true })).toBeVisible();

  await ownerOpenRequests(page);
  await expect(ownerCard(page).getByText(/Agreed price €85\.00/)).toBeVisible();
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page.getByText('€120.00', { exact: false })).toBeVisible();
  await staff.context.close();
});

test('staff never list requests from a company without partner access', async ({ browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  const other = backend.addBranch({ name: 'Harbour flat', companyId: OTHER_COMPANY.id, priceNet: 60, estimatedDurationMinutes: 60 });
  backend.seedRequest(other, WINDOW);
  backend.seedRequest(backend.branches[0], WINDOW);
  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  // Wait for the owner-company request so the absence check runs after the list has loaded.
  await expect(staffCard(staff.page)).toHaveCount(1);
  await expect(staff.page.getByRole('article', { name: 'Harbour flat request', exact: true })).toHaveCount(0);
  await staff.context.close();
});

test('employees and owners cannot reach partner operations pages', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const cleaner = await openActor(browser, testInfo, backend, 'cleaner');
  await cleaner.page.goto('/partner/');
  await expect(cleaner.page.getByText('Access denied.', { exact: false })).toBeVisible();
  await cleaner.page.goto('/admin/partner-requests/');
  await expect(cleaner.page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(cleaner.page.getByRole('article', { name: `${PROPERTY} request`, exact: true })).toHaveCount(0);

  await prepareRolePage(page, backend, 'owner');
  await page.goto('/admin/partner-requests/');
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  expect(backend.writes).toHaveLength(0);
  await cleaner.context.close();
});

test('manager arranges the same cleaner offer through manager routes', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const manager = await openActor(browser, testInfo, backend, 'manager');
  await manager.page.goto('/manager/partner-requests/');
  await staffArrange(manager.page, 'cleaner');
  await expect(manager.page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();
  expect(shape(backend.writes)).toEqual(['manager POST manager/partner/requests/<id>/offer']);
  await manager.context.close();
});

// ----- defaults -----

test('a new employee defaults to automatic approval, and automatic only applies once they can sign in', async ({ page, browser }, testInfo) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedRequest(backend.branches[0], WINDOW);
  const newcomer = await openActor(browser, testInfo, backend, 'newCleaner');
  await newcomer.page.goto('/account/?tenant=readysetsiivous');
  await expect(newcomer.page.getByRole('checkbox', { name: 'Automatically accept cleaning offers' })).toBeChecked();
  await newcomer.context.close();

  const staff = await openActor(browser, testInfo, backend, 'admin');
  await staffOpenRequests(staff.page);
  await staffCard(staff.page).getByRole('button', { name: 'Arrange cleaner / time', exact: true }).click();
  const cleanerChoice = staff.page.getByLabel('Cleaner', { exact: true });
  await expect(cleanerChoice.locator('option', { hasText: 'Nina Newstart · Automatic acceptance' })).toHaveCount(1);
  await expect(cleanerChoice.locator('option', { hasText: 'Ola Pending · Automatic acceptance' })).toHaveCount(0);
  await cleanerChoice.selectOption('unlinked');
  await staff.page.getByRole('button', { name: 'Send cleaning offer', exact: true }).click();
  await expect(staff.page.getByText('Offer sent. Waiting for cleaner acceptance.', { exact: true })).toBeVisible();
  expect(backend.requests[0]).toMatchObject({ status: 'Offered', offeredEmployeeId: 'unlinked' });
  await staff.context.close();
});

