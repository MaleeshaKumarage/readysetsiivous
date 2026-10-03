import { test, expect } from '@playwright/test';
import { json } from './support';

/** Minimal shape of `PublicAgreementDto`, matching site/src/lib/agreementApi.ts. */
function agreement(overrides: Partial<{
  title: string;
  status: string;
  active: boolean;
  totalSigners: number;
  signedCount: number;
  completed: boolean;
  signed: boolean;
}> = {}) {
  return {
    title: 'Cleaning agreement',
    status: 'Pending',
    active: true,
    totalSigners: 2,
    signedCount: 0,
    completed: false,
    signed: false,
    ...overrides,
  };
}

test.describe('agreement signing', () => {
  test('renders the agreement and signing form for a valid token', async ({ page }) => {
    await page.route('**/api/**', (route) =>
      route.request().method() === 'GET'
        ? json(route, 200, agreement())
        : json(route, 200, { ok: true }),
    );

    await page.goto('/fi/sign?token=valid-token');

    // SignPageClient swaps its loading state for the agreement view once the DTO lands.
    await expect(page.getByRole('heading', { name: 'Cleaning agreement' })).toBeVisible();
    await expect(page.getByText('0/2 signed')).toBeVisible();
    await expect(page.getByText('Full name')).toBeVisible();
  });

  test('shows a friendly message for an invalid or expired link', async ({ page }) => {
    // agreementApi.getPublicAgreement returns null on !r.ok, which maps to `invalid`.
    await page.route('**/api/**', (route) => json(route, 404, { title: 'Not found' }));

    await page.goto('/fi/sign?token=expired-token');

    await expect(page.getByRole('heading', { name: 'Link not found' })).toBeVisible();
    await expect(page.getByText(/this signing link is invalid or has expired/i)).toBeVisible();
  });

  test('does not crash when no token is supplied', async ({ page }) => {
    await page.route('**/api/**', (route) => json(route, 400, { title: 'Bad request' }));

    await page.goto('/fi/sign');

    await expect(page.getByRole('heading', { name: 'Link not found' })).toBeVisible();
    await expect(page.locator('body')).not.toContainText('Application error');
  });

  test('confirms when the signer has already signed', async ({ page }) => {
    await page.route('**/api/**', (route) =>
      route.request().method() === 'GET'
        ? json(route, 200, agreement({ signed: true, signedCount: 1 }))
        : json(route, 200, { ok: true }),
    );

    await page.goto('/fi/sign?token=already-signed');

    await expect(page.getByText('You have signed this agreement.')).toBeVisible();
    await expect(page.getByText(/waiting for the remaining signers/i)).toBeVisible();
  });

  test('offers the completed document once everyone has signed', async ({ page }) => {
    await page.route('**/api/**', (route) =>
      route.request().method() === 'GET'
        ? json(route, 200, agreement({ signed: true, completed: true, signedCount: 2 }))
        : json(route, 200, { ok: true }),
    );

    await page.goto('/fi/sign?token=completed');

    await expect(page.getByText(/everyone has signed/i)).toBeVisible();
    await expect(page.getByRole('button', { name: /download signed document/i })).toBeVisible();
  });
});
