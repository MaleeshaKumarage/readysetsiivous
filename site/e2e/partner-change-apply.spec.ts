import { test, expect } from '@playwright/test';
import { PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

// Staff review an owner's structured request on the Changes tab and apply it, rather than cancelling in a separate step.

const WINDOW = { fromDate: '2026-12-01', toDate: '2026-12-02' };

test('staff apply a cancellation from the change request and the request is cancelled', async ({ page }) => {
  const backend = new PartnerFlowBackend({ priced: true });
  const request = backend.seedRequest(backend.branches[0], WINDOW);
  backend.changes.push({ id: 'change-cancel', companyId: request.companyId, branchId: request.branchId, requestId: request.id, kind: 'Cleaning',
    message: 'The owner cannot host this week.', resolvedUtc: null, resolution: null, action: 'Cancel' });
  await prepareRolePage(page, backend, 'admin');

  await page.goto('/admin/partner-requests/');
  await page.getByRole('tab', { name: 'Changes', exact: true }).click();
  const change = page.getByRole('article').filter({ hasText: 'The owner cannot host this week.' });
  await expect(change.getByText('Requested: Cancel the request', { exact: true })).toBeVisible();
  await change.getByRole('button', { name: 'Review and apply', exact: true }).click();

  const dialog = page.getByRole('dialog', { name: 'Apply change request', exact: true });
  await dialog.getByLabel('Resolution', { exact: true }).fill('Cancelled at the owner\'s request.');
  await dialog.getByRole('button', { name: 'Apply and resolve', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(backend.request(request.id).status).toBe('Cancelled');
  expect(backend.changes[0].resolvedUtc).not.toBeNull();
  await expect(change.getByText('Resolved: Cancelled at the owner\'s request.', { exact: true })).toBeVisible();
});
