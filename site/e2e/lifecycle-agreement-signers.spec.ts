import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': '*' };

type Signer = { id: string; name: string; email: string; status: string; token: string };
type Fixture = { id: string; title: string; status: string; signers: Signer[] };

const DUPLICATE = 'ben@example.test is already a signer on this agreement. Use a different email address.';
const LAST_SIGNER = 'An agreement needs at least one signer. Add another signer before removing this one.';
const SIGNED_REMOVAL = 'This signer has already signed, so they cannot be removed. Keep the agreement for the record.';

const pending = (id: string, name: string, email: string): Signer => ({ id, name, email, status: 'Pending', token: `tok-${id}` });
const signed = (id: string, name: string, email: string): Signer => ({ id, name, email, status: 'Signed', token: `tok-${id}` });

function summaryOf(a: Fixture) {
  return {
    id: a.id, title: a.title, code: 'AG-1', status: a.status, isActive: true,
    signerCount: a.signers.length, signedCount: a.signers.filter(s => s.status === 'Signed').length,
    createdUtc: '2026-10-01T00:00:00Z', hasSignedEvidence: a.status !== 'Draft', companyId: 'company', companyName: 'Fixture Oy',
  };
}

/** A stateful stand-in for the admin agreements API: adds, removes and cancels change what the page reloads. */
async function setup(page: Page, initial: Fixture) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers, json: { items: [{ id: 'company', name: 'Fixture Oy', isActive: true }], total: 1 } }));
  const agreement: Fixture = structuredClone(initial);
  const calls: string[] = [];
  const control = { failDetailReads: false };
  let nextId = 100;

  await page.route('**/api/v1/admin/agreements**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (path.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: [], companies: [] } });

    const signersPath = path.match(/\/signers(?:\/([^/]+))?$/);
    if (signersPath && request.method() === 'POST') {
      calls.push('add-signer');
      const body = request.postDataJSON() as { name: string; email: string };
      if (agreement.signers.some(s => s.email.toLowerCase() === body.email.toLowerCase())) {
        return route.fulfill({ headers, status: 409, json: { title: 'Agreement conflict', detail: DUPLICATE } });
      }
      agreement.signers.push(pending(`s${nextId++}`, body.name, body.email));
      return route.fulfill({ headers, status: 204 });
    }
    if (signersPath && request.method() === 'DELETE') {
      const signerId = signersPath[1];
      calls.push(`remove-signer:${signerId}`);
      const target = agreement.signers.find(s => s.id === signerId);
      if (target?.status === 'Signed') return route.fulfill({ headers, status: 409, json: { title: 'Agreement conflict', detail: SIGNED_REMOVAL } });
      if (agreement.signers.length <= 1) return route.fulfill({ headers, status: 409, json: { title: 'Agreement conflict', detail: LAST_SIGNER } });
      agreement.signers = agreement.signers.filter(s => s.id !== signerId);
      return route.fulfill({ headers, status: 204 });
    }
    if (path.endsWith('/cancel') && request.method() === 'POST') {
      calls.push('cancel');
      agreement.status = 'Cancelled';
      return route.fulfill({ headers, status: 204 });
    }
    if (/\/admin\/agreements\/[^/]+$/.test(path) && request.method() === 'GET') {
      if (control.failDetailReads) return route.fulfill({ headers, status: 500, json: { title: 'Server error', detail: 'Temporary failure' } });
      return route.fulfill({ headers, json: { ...summaryOf(agreement), signers: agreement.signers, completedUtc: null } });
    }
    return route.fulfill({ headers, json: [summaryOf(agreement)] });
  });
  return { calls, agreement, control };
}

const draft = (): Fixture => ({ id: 'agr-1', title: 'Cleaning contract', status: 'Draft', signers: [pending('s1', 'Anna Pending', 'anna@example.test')] });

async function openSigners(page: Page, firstSigner = 'Anna Pending') {
  await page.goto('/admin/agreements/');
  await page.getByRole('button', { name: 'Signers', exact: true }).first().click();
  await expect(page.getByRole('dialog').getByText(firstSigner).first()).toBeVisible();
}

test('an admin adds a signer from the agreement screen and sees the list update', async ({ page }) => {
  const { calls, agreement } = await setup(page, draft());
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name').fill('Ben Newsigner');
  await dialog.getByLabel('Email').fill('ben@example.test');
  await dialog.getByRole('button', { name: 'Add signer' }).click();
  await expect(dialog.getByText('Ben Newsigner').first()).toBeVisible();
  await expect(dialog.getByText('ben@example.test').first()).toBeVisible();
  await expect(dialog.getByLabel('Name')).toHaveValue('');
  expect(agreement.signers).toHaveLength(2);
  expect(calls).toEqual(['add-signer']);
});

test('adding a signer without a name or email explains what is missing and sends nothing', async ({ page }) => {
  const { calls } = await setup(page, draft());
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name').fill('Ben Newsigner');
  await dialog.getByRole('button', { name: 'Add signer' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Enter a name and an email address for the new signer.');
  expect(calls).toEqual([]);
});

test('an email that is already a signer is refused with the reason and a toast, and the list is unchanged', async ({ page }) => {
  const fixture = draft();
  fixture.signers.push(pending('s2', 'Ben Existing', 'ben@example.test'));
  const { agreement } = await setup(page, fixture);
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name').fill('Ben Again');
  await dialog.getByLabel('Email').fill('ben@example.test');
  await dialog.getByRole('button', { name: 'Add signer' }).click();
  await expect(dialog.getByRole('alert')).toHaveText(DUPLICATE);
  await expect(page.getByText('Signer not added').first()).toBeVisible();
  expect(agreement.signers).toHaveLength(2);
});

test('an unsigned signer can be removed from the agreement', async ({ page }) => {
  const fixture = draft();
  fixture.signers.push(pending('s2', 'Ben Remove', 'ben@example.test'));
  const { calls, agreement } = await setup(page, fixture);
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Remove signer Ben Remove' }).click();
  await expect(dialog.getByText('Ben Remove')).toHaveCount(0);
  expect(agreement.signers.map(s => s.id)).toEqual(['s1']);
  expect(calls).toEqual(['remove-signer:s2']);
});

test('a signer who has already signed has no remove control', async ({ page }) => {
  const fixture: Fixture = { id: 'agr-1', title: 'Cleaning contract', status: 'PartiallySigned', signers: [signed('s1', 'Anna Signed', 'anna@example.test'), pending('s2', 'Ben Pending', 'ben@example.test')] };
  await setup(page, fixture);
  await openSigners(page, 'Anna Signed');
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('button', { name: 'Remove signer Anna Signed' })).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Remove signer Ben Pending' })).toBeVisible();
});

test('a removal the server refuses shows the reason and keeps the signer listed', async ({ page }) => {
  const { calls, agreement } = await setup(page, draft());
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Remove signer Anna Pending' }).click();
  await expect(dialog.getByRole('alert')).toHaveText(LAST_SIGNER);
  await expect(page.getByText('Signer not removed').first()).toBeVisible();
  await expect(dialog.getByText('Anna Pending').first()).toBeVisible();
  expect(agreement.signers).toHaveLength(1);
  expect(calls).toEqual(['remove-signer:s1']);
});

test('cancelling an agreement after confirmation closes it to signers and to new signers', async ({ page }) => {
  const { calls, agreement } = await setup(page, draft());
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  page.once('dialog', d => d.accept());
  await dialog.getByRole('button', { name: 'Cancel agreement', exact: true }).click();
  await expect(dialog.getByText('Signers cannot be changed on a cancelled agreement.')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Add signer', exact: true })).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Cancel agreement', exact: true })).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Remove signer Anna Pending' })).toHaveCount(0);
  expect(agreement.status).toBe('Cancelled');
  expect(calls).toEqual(['cancel']);
});

test('dismissing the cancel confirmation leaves the agreement open', async ({ page }) => {
  const { calls, agreement } = await setup(page, draft());
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  page.once('dialog', d => d.dismiss());
  await dialog.getByRole('button', { name: 'Cancel agreement', exact: true }).click();
  await expect(dialog.getByRole('button', { name: 'Cancel agreement', exact: true })).toBeVisible();
  expect(agreement.status).toBe('Draft');
  expect(calls).toEqual([]);
});

test('a completed agreement shows its signers read-only with no change controls', async ({ page }) => {
  const fixture: Fixture = { id: 'agr-1', title: 'Cleaning contract', status: 'Completed', signers: [signed('s1', 'Anna Done', 'anna@example.test')] };
  const { calls } = await setup(page, fixture);
  await openSigners(page, 'Anna Done');
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Signers cannot be changed on a completed agreement.')).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Add signer', exact: true })).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Cancel agreement', exact: true })).toHaveCount(0);
  expect(calls).toEqual([]);
});

test('a signer change that saves but cannot refresh the agreement says it may have saved and re-enables the controls', async ({ page }) => {
  const { calls, agreement, control } = await setup(page, draft());
  await openSigners(page);
  const dialog = page.getByRole('dialog');
  control.failDetailReads = true;
  await dialog.getByLabel('Name').fill('Ben Newsigner');
  await dialog.getByLabel('Email').fill('ben@example.test');
  await dialog.getByRole('button', { name: 'Add signer', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('The change may have been saved, but the agreement could not be refreshed. Close and reopen it to check the current signers.');
  await expect(page.getByText('Agreement not refreshed').first()).toBeVisible();
  // The server did save the signer; the controls must come back so the admin can carry on.
  expect(agreement.signers).toHaveLength(2);
  await expect(dialog.getByRole('button', { name: 'Add signer', exact: true })).toBeEnabled();
  await expect(dialog.getByRole('button', { name: 'Cancel agreement', exact: true })).toBeEnabled();
  expect(calls).toEqual(['add-signer']);
});

test('an agreement that cannot be loaded shows a message in the dialog instead of a spinner', async ({ page }) => {
  const { control } = await setup(page, draft());
  control.failDetailReads = true;
  await page.goto('/admin/agreements/');
  await page.getByRole('button', { name: 'Signers', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('alert')).toHaveText('This agreement could not be loaded. Close it and try again.');
  await expect(dialog.locator('.mantine-Loader-root')).toHaveCount(0);
});
