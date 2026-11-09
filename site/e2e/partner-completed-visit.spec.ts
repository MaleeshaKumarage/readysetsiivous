import { test, expect, type Page } from '@playwright/test';
import { PartnerFlowBackend, prepareRolePage, type VisitSeed } from './partner-flow-backend';

// The owner opens a finished visit from Completed requests and reads what the cleaner recorded.
// The cleaner is shown by first initial only; the full name stays with staff in the admin panel.

const PROPERTY = 'Lake apartment';
const WINDOW = { fromDate: '2026-12-01', toDate: '2026-12-01' };
const PLANNED = '2026-12-01T10:00';
// A 1x1 PNG, so the photo is a real image the browser can decode.
const PHOTO = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==';

const completedCard = (page: Page) => page.getByRole('article', { name: `${PROPERTY} ${WINDOW.fromDate}`, exact: true });
const visitDialog = (page: Page) => page.getByRole('dialog', { name: 'Completed visit', exact: true });

async function openCompletedVisit(page: Page) {
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('tab', { name: /^Completed requests/ }).click();
  await completedCard(page).getByRole('button', { name: 'View visit', exact: true }).click();
  await expect(visitDialog(page)).toBeVisible();
}

// A visit the cleaner finished: the request is accepted for 'cleaner' at PLANNED, then completed with the given work.
function finishedVisit(backend: PartnerFlowBackend, visit: VisitSeed) {
  const request = backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', PLANNED);
  backend.completeVisit(request, visit);
  return request;
}

test('owner reads the checklist, notes and photos of a completed visit, without the cleaner name', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  finishedVisit(backend, {
    tasks: [{ text: 'Vacuum floors', done: true }, { text: 'Restock towels', done: false }],
    cleanerNotes: 'Bathroom and kitchen done. Towels are low.',
    photos: [{ url: PHOTO, note: 'Kitchen after clean' }],
  });
  await prepareRolePage(page, backend, 'owner');

  await openCompletedVisit(page);
  const dialog = visitDialog(page);

  await expect(dialog.getByRole('heading', { name: PROPERTY, exact: true })).toBeVisible();
  await expect(dialog.getByText('Tasks done: 1 / 2', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('checkbox', { name: 'Vacuum floors', exact: true })).toBeChecked();
  await expect(dialog.getByRole('checkbox', { name: 'Restock towels', exact: true })).not.toBeChecked();
  await expect(dialog.getByText('Bathroom and kitchen done. Towels are low.', { exact: true })).toBeVisible();

  const photo = dialog.getByRole('img', { name: 'Photo 1: Kitchen after clean', exact: true });
  await expect(photo).toBeVisible();
  await expect.poll(() => photo.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBeGreaterThan(0);
  await expect(dialog.getByText('Kitchen after clean', { exact: true })).toBeVisible();

  // The partner sees the first initial, never the cleaner's full name.
  await expect(dialog.getByText('T.', { exact: true })).toHaveCount(0);
  await expect(dialog.getByText('Test Cleaner')).toHaveCount(0);
  await expect(dialog).not.toContainText('Test Cleaner');
});

test('a completed visit with no checklist, notes or photos says so', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  finishedVisit(backend, { tasks: [], cleanerNotes: null, photos: [] });
  await prepareRolePage(page, backend, 'owner');

  await openCompletedVisit(page);
  const dialog = visitDialog(page);

  await expect(dialog.getByText('No checklist was recorded for this visit.', { exact: true })).toBeVisible();
  await expect(dialog.getByText('No notes were left for this visit.', { exact: true })).toBeVisible();
  await expect(dialog.getByText('No photos were attached to this visit.', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('img')).toHaveCount(0);
});

test('messages about a completed visit appear with the staff response', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  const request = finishedVisit(backend, { tasks: [{ text: 'Vacuum floors', done: true }] });
  backend.changes.push({ id: 'change-1', companyId: request.companyId, branchId: request.branchId, requestId: request.id, kind: 'CleaningRequest', message: 'Please use the back door.', resolvedUtc: '2026-11-30T12:00:00Z', resolution: 'Passed on to the cleaner.' });
  await prepareRolePage(page, backend, 'owner');

  await openCompletedVisit(page);
  const dialog = visitDialog(page);

  await expect(dialog.getByRole('heading', { name: 'Messages about this visit', exact: true })).toBeVisible();
  await expect(dialog.getByText('Please use the back door.', { exact: true })).toBeVisible();
  await expect(dialog.getByText('Passed on to the cleaner.')).toBeVisible();
});

test('an accepted visit that is still in progress has no visit summary yet', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  backend.seedAccepted(backend.seedRequest(backend.branches[0], WINDOW), 'cleaner', PLANNED);
  await prepareRolePage(page, backend, 'owner');

  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await expect(page.getByRole('article', { name: `${PROPERTY} ${WINDOW.fromDate}`, exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'View visit', exact: true })).toHaveCount(0);
});
