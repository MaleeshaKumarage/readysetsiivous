import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { chooseEmployeeSection } from './employee-section';

// Layout checks for the restyled employee Settings and Inventory screens. Runs on chromium-desktop and mobile-touch.

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
  await page.route(/\/api\/v1\/me\/partner-offers(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  await page.route(/\/api\/v1\/me\/inventory\/(locations|items)$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/me\/inventory\/usage(\?.*)?$/, route => route.fulfill({ headers, json: [] }));
}

async function yOf(page: Page, name: string) {
  const box = await page.getByRole('heading', { name, exact: true }).boundingBox();
  return box?.y ?? -1;
}

test('the Settings cards each have the gold left edge', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Settings');
  for (const name of ['Cleaning offers', 'App', 'Account']) {
    const card = page.getByRole('heading', { name, exact: true }).locator('xpath=..');
    const width = await card.evaluate(el => parseFloat(getComputedStyle(el).borderLeftWidth));
    expect(width).toBeGreaterThanOrEqual(4);
  }
});

test('the Settings cards sit in a row on desktop and stack on phones', async ({ page, isMobile }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Settings');
  const app = await yOf(page, 'App');
  const offers = await yOf(page, 'Cleaning offers');
  const account = await yOf(page, 'Account');
  if (isMobile) {
    expect(offers).toBeLessThan(app);
    expect(app).toBeLessThan(account);
  } else {
    expect(Math.abs(app - offers)).toBeLessThan(4);
    expect(Math.abs(account - offers)).toBeLessThan(4);
  }
});

test('the Settings page does not widen a phone', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Settings');
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});

test('the Inventory log form has the gold edge and a 44px Save button', async ({ page }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Inventory');
  const section = page.getByRole('region', { name: 'Log an item taken', exact: true });
  const width = await section.evaluate(el => parseFloat(getComputedStyle(el).borderLeftWidth));
  expect(width).toBeGreaterThanOrEqual(4);
  const box = await page.getByRole('button', { name: 'Save entry', exact: true }).boundingBox();
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(44);
});

test('the Inventory log and history sit side by side on desktop and stack on phones', async ({ page, isMobile }) => {
  await setup(page);
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Inventory');
  // Compare the section tops: the log card has padding and the history section does not, so headings sit at different heights.
  const log = (await page.getByRole('region', { name: 'Log an item taken', exact: true }).boundingBox())?.y ?? -1;
  const history = (await page.getByRole('region', { name: 'My history', exact: true }).boundingBox())?.y ?? -1;
  if (isMobile) {
    expect(history).toBeGreaterThan(log);
  } else {
    expect(Math.abs(history - log)).toBeLessThan(4);
  }
});

test('the Inventory page does not widen a phone', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'phone width check');
  await setup(page);
  await page.goto('/me/shifts/');
  await chooseEmployeeSection(page, 'Inventory');
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});
