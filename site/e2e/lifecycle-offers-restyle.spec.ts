import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { chooseEmployeeSection } from './employee-section';

// Layout and behaviour checks for the restyled Offers cards. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function offer(id: string, branchName: string) {
  return {
    id, requestId: `request-${id}`, version: 1, branchName, street: 'Test street 1', postalCode: '00100', city: 'Helsinki',
    fromDate: '2026-11-24', toDate: '2026-11-30', exactStartLocalTime: null, plannedStartUtc: null,
    estimatedDurationMinutes: 120, notes: null, timeZoneId: 'Europe/Helsinki',
  };
}

async function setup(page: Page, offers = [offer('offer-riverside', 'Riverside'), offer('offer-oakfield', 'Oakfield')]) {
  await setupAdminMocks(page);
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['employee'] })], employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } },
  }));
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({
    headers,
    json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-01', toLocalDate: '2026-11-22' },
  }));
  await page.route(/\/api\/v1\/me\/shifts(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: offers }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
}

async function openOffers(page: Page) {
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Offers');
  await expect(page.getByRole('article', { name: 'Riverside offer' })).toBeVisible();
}

test('each offer card has the gold left edge', async ({ page }) => {
  await setup(page);
  await openOffers(page);
  const card = page.getByRole('article', { name: 'Riverside offer' }).locator('> div').first();
  const width = await card.evaluate(el => parseFloat(getComputedStyle(el).borderLeftWidth));
  expect(width).toBeGreaterThanOrEqual(4);
});

test('Accept and Decline are at least 44px tall', async ({ page }) => {
  await setup(page);
  await openOffers(page);
  const article = page.getByRole('article', { name: 'Riverside offer' });
  for (const name of ['Accept cleaning', 'Decline cleaning']) {
    const box = await article.getByRole('button', { name, exact: true }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }
});

test('offers sit side by side on desktop and stack on phones', async ({ page, isMobile }) => {
  await setup(page);
  await openOffers(page);
  const first = await page.getByRole('article', { name: 'Riverside offer' }).boundingBox();
  const second = await page.getByRole('article', { name: 'Oakfield offer' }).boundingBox();
  expect(first && second).toBeTruthy();
  if (isMobile) {
    expect(second!.y).toBeGreaterThan(first!.y);
  } else {
    expect(Math.abs(second!.y - first!.y)).toBeLessThan(4);
    expect(second!.x).toBeGreaterThan(first!.x);
  }
});

test('the phone layout does not widen the page', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await openOffers(page);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('Accept still sends the decision for that offer', async ({ page }) => {
  await setup(page);
  const requests: string[] = [];
  await page.route(/\/api\/v1\/me\/partner-offers\/offer-riverside\/accept$/, route => {
    requests.push(route.request().url());
    return route.fulfill({ headers, json: {} });
  });
  await openOffers(page);
  await page.getByRole('article', { name: 'Riverside offer' }).getByRole('button', { name: 'Accept cleaning', exact: true }).click();
  await expect.poll(() => requests.length).toBe(1);
});
