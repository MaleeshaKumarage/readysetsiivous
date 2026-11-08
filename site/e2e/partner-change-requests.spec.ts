import { test, expect, type Page } from '@playwright/test';
import { PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

// The owner asks for a change to one cleaning request. Structured asks carry the proposal staff will review.

const PROPERTY = 'Lake apartment';
const WINDOW = { fromDate: '2026-12-01', toDate: '2026-12-02' };

const requestCard = (page: Page) => page.getByRole('article', { name: `${PROPERTY} ${WINDOW.fromDate}`, exact: true });
const changeDialog = (page: Page) => page.getByRole('dialog', { name: 'Request staff changes', exact: true });

async function openChangeDialog(page: Page) {
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await requestCard(page).getByRole('button', { name: 'Request a change or cancellation', exact: true }).click();
  await expect(changeDialog(page)).toBeVisible();
}

test('owner proposes new dates, and the change list shows the proposal', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  // Accepted visits are changed through staff; unaccepted requests are edited directly.
  backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', '2026-12-01T10:00');
  await prepareRolePage(page, backend, 'owner');

  await openChangeDialog(page);
  await page.getByRole('combobox', { name: 'What do you need?', exact: true }).selectOption('Reschedule');
  await page.getByLabel('New first date', { exact: true }).fill('2026-12-03');
  await page.getByLabel('New last date', { exact: true }).fill('2026-12-04');
  await page.getByLabel('Describe the requested change', { exact: true }).fill('Could we come a day later?');
  await page.getByRole('button', { name: 'Send change request', exact: true }).click();

  const sent = backend.writes.find(write => write.method === 'POST' && write.path.endsWith('/change-requests'));
  expect(sent?.body).toEqual(expect.objectContaining({ message: 'Could we come a day later?', action: 'Reschedule', fromDate: '2026-12-03', toDate: '2026-12-04' }));

  await page.getByRole('tab', { name: 'Change requests', exact: true }).click();
  await expect(page.getByText('Requested: 2026-12-03 – 2026-12-04', { exact: true })).toBeVisible();
});

test('owner asks to cancel an accepted visit with a reason', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', '2026-12-01T10:00');
  await prepareRolePage(page, backend, 'owner');

  await openChangeDialog(page);
  await page.getByRole('combobox', { name: 'What do you need?', exact: true }).selectOption('Cancel');
  await page.getByLabel('Describe the requested change', { exact: true }).fill('We no longer need this visit.');
  await page.getByRole('button', { name: 'Send change request', exact: true }).click();

  const sent = backend.writes.find(write => write.method === 'POST' && write.path.endsWith('/change-requests'));
  expect(sent?.body).toEqual(expect.objectContaining({ action: 'Cancel', message: 'We no longer need this visit.' }));
});
