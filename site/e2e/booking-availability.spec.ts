import { test, expect } from '@playwright/test';

const headers = { 'Access-Control-Allow-Origin': '*' };
const service = {
  id: '11111111-1111-1111-1111-111111111111',
  slug: 'cleaning', name: 'Home cleaning', description: 'Cleaning service',
  category: 'home', additionalInfo: null, icon: 'Sparkles', imageUrl: '',
  sortOrder: 1, priceNet: 50, vatRatePercent: 25.5, currency: 'EUR',
};

test.beforeEach(async ({ page }) => {
  await page.route('**/api/v1/public/*/services?*', route =>
    route.fulfill({ json: [service], headers }));
});

test('availability failure shows an error and retry recovers', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/v1/public/*/availability?*', route => {
    requests++;
    return requests === 1
      ? route.fulfill({ status: 503, json: {}, headers })
      : route.fulfill({ json: [{ startTime: '12:00', endTime: '13:00' }], headers });
  });
  await page.goto('/en/varaus');
  await page.getByRole('button', { name: /Home cleaning/ }).click();
  await page.locator('input[type="date"]').fill('2030-01-15');
  const error = page.getByRole('alert').filter({ hasText: "We couldn't load available times." });
  await expect(error).toHaveText("We couldn't load available times. Please try again.");
  await expect(page.getByText('Loading available times…', { exact: true })).not.toBeVisible();
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.getByRole('button', { name: '12:00', exact: true })).toBeVisible();
  await expect(error).not.toBeVisible();
  expect(requests).toBe(2);
});

test('empty availability shows no slots instead of an error', async ({ page }) => {
  await page.route('**/api/v1/public/*/availability?*', route => route.fulfill({ json: [], headers }));
  await page.goto('/en/varaus');
  await page.getByRole('button', { name: /Home cleaning/ }).click();
  await page.locator('input[type="date"]').fill('2030-01-15');
  await expect(page.getByText('No available times for this date.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Try again', exact: true })).not.toBeVisible();
});

test('a late response for the previous date cannot overwrite the current slots', async ({ page }) => {
  let releaseFirst!: () => void;
  let markFirstStarted!: () => void;
  const firstStarted = new Promise<void>(resolve => { markFirstStarted = resolve; });
  const firstReleased = new Promise<void>(resolve => { releaseFirst = resolve; });
  let markFirstFinished!: () => void;
  const firstFinished = new Promise<void>(resolve => { markFirstFinished = resolve; });
  await page.route('**/api/v1/public/*/availability?*', async route => {
    const date = new URL(route.request().url()).searchParams.get('date');
    if (date === '2030-01-15') {
      markFirstStarted();
      await firstReleased;
      await route.fulfill({ json: [{ startTime: '08:00', endTime: '09:00' }], headers });
      markFirstFinished();
    } else {
      await route.fulfill({ json: [{ startTime: '12:00', endTime: '13:00' }], headers });
    }
  });
  await page.goto('/en/varaus');
  await page.getByRole('button', { name: /Home cleaning/ }).click();
  await page.locator('input[type="date"]').fill('2030-01-15');
  await firstStarted;
  await page.locator('input[type="date"]').fill('2030-01-16');
  await expect(page.getByRole('button', { name: '12:00', exact: true })).toBeVisible();
  releaseFirst();
  await firstFinished;
  // Wait until the old response has been delivered to the browser.
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  await expect(page.getByRole('button', { name: '12:00', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '08:00', exact: true })).not.toBeVisible();
});
