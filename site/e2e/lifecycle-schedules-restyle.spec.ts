import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// Layout checks for the restyled Schedules controls. Runs on chromium-desktop and mobile-touch.

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

async function setup(page: Page) {
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
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
}

test('the Past, Today and Future tabs are at least 44px tall', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  for (const name of ['Past', 'Today', 'Future']) {
    const box = await page.getByRole('tab', { name, exact: true }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }
});

test('the date filter is a labelled group with From, To and Clear dates on the Past view', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  const group = page.getByRole('group', { name: 'Completed between dates' });
  await expect(group).toBeVisible();
  await expect(group.getByLabel('From date', { exact: true })).toBeVisible();
  await expect(group.getByLabel('To date', { exact: true })).toBeVisible();
  // Clear dates only appears once a date is chosen.
  await expect(group.getByRole('button', { name: 'Clear dates' })).toHaveCount(0);
  await group.getByLabel('From date', { exact: true }).fill('2026-11-01');
  await expect(group.getByRole('button', { name: 'Clear dates' })).toBeVisible();
});

test('the date inputs are at least 44px tall', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  for (const label of ['From date', 'To date']) {
    const box = await page.getByLabel(label, { exact: true }).boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  }
});

test('the Schedules controls do not widen the page on a phone', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  await expect(page.getByRole('group', { name: 'Completed between dates' })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('the header is a bordered white bar with 44px profile and sign-out controls', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  const header = page.locator('.employee-header');
  await expect(header).toBeVisible();
  const radius = await header.evaluate(el => parseFloat(getComputedStyle(el).borderTopLeftRadius));
  expect(radius).toBeGreaterThanOrEqual(12);
  for (const name of ['Profile', 'Sign out']) {
    const box = await page.getByRole('link', { name, exact: true }).or(page.getByRole('button', { name, exact: true })).first().boundingBox();
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
    expect(box?.width ?? 0).toBeGreaterThanOrEqual(44);
  }
});

test('a shift card has the gold left edge from the design', async ({ page }) => {
  await setup(page);
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({
    headers,
    json: {
      occurrences: [{ shiftId: 'riverside', shiftName: 'Riverside clean', startUtc: '2026-11-23T08:00:00Z', endUtc: '2026-11-23T12:00:00Z', timeZoneId: 'Europe/Helsinki', form: null }],
      timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-23', toLocalDate: '2026-11-23',
    },
  }));
  await page.goto('/me/shifts/');
  const card = page.getByRole('group', { name: /Riverside clean/ });
  await expect(card).toBeVisible();
  const { width, color } = await card.evaluate(el => {
    const s = getComputedStyle(el);
    return { width: parseFloat(s.borderLeftWidth), color: s.borderLeftColor };
  });
  expect(width).toBeGreaterThanOrEqual(4);
  expect(color).not.toBe('rgba(0, 0, 0, 0)');
});

test('the schedule column is wider on desktop and the Start button is a 44px target', async ({ page, isMobile }) => {
  await setup(page);
  await page.route(/\/api\/v1\/me\/shifts\/calendar(\?.*)?$/, route => route.fulfill({
    headers,
    json: {
      occurrences: [{ shiftId: 'oakfield', shiftName: 'Oakfield clean', startUtc: '2026-11-01T08:00:00Z', endUtc: '2026-11-01T12:00:00Z', timeZoneId: 'Europe/Helsinki', form: null }],
      timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-11-01', toLocalDate: '2026-11-01',
    },
  }));
  await page.goto('/me/shifts/');
  const start = page.getByRole('button', { name: 'Start', exact: true });
  await expect(start).toBeVisible();
  const box = await start.boundingBox();
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
  if (!isMobile) {
    const width = await page.locator('.employee-content').evaluate(el => el.getBoundingClientRect().width);
    expect(width).toBeGreaterThan(700);
  }
});
