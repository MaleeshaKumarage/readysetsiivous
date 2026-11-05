import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { buildScaleData, installScaleRoutes } from './admin-scale-data';

const NAV_PAGES = ['/admin/', '/admin/shifts/', '/admin/shifts/calendar/', '/admin/work-queue/', '/admin/partner-requests/', '/admin/quality-cycle/', '/admin/leads/', '/admin/companies/', '/admin/agreements/', '/admin/employees/', '/admin/invoices/', '/admin/services/'];

async function openAdmin(page: Page, path: string) {
  await page.goto(path, { waitUntil: 'commit' }).catch(() => {});
  await page.getByText('Checking login…').waitFor({ state: 'detached', timeout: 30000 }).catch(() => {});
  await expect(page.getByText('Tenant administration', { exact: true })).toBeVisible({ timeout: 15000 });
}

test('header names the most specific section and the partner entry matches its page', async ({ page }) => {
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  await openAdmin(page, '/admin/shifts/calendar/');
  await expect(page.getByText('Shift calendar', { exact: true }).first()).toBeVisible();
  const partnerLink = page.locator('a[aria-label="Partner operations"]').first();
  if (test.info().project.name === 'mobile-touch' && !(await partnerLink.isVisible())) await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
  await expect(partnerLink).toBeVisible({ timeout: 15000 });
  await openAdmin(page, '/admin/partner-requests/');
  await expect(page.getByRole('heading', { level: 1, name: 'Partner operations', exact: true })).toBeVisible();
});

test('every admin nav page has exactly one top-level heading', async ({ page }) => {
  test.setTimeout(180000);
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  for (const path of NAV_PAGES) {
    await openAdmin(page, path);
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  }
});

test('phone tabs and pager controls meet the 44px touch target', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  await openAdmin(page, '/admin/shifts/');
  const tabs = page.getByRole('tab');
  await expect(tabs.first()).toBeVisible();
  const count = await tabs.count();
  for (let i = 0; i < count; i++) {
    const box = await tabs.nth(i).boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(44);
  }
  await openAdmin(page, '/admin/companies/');
  const next = page.getByRole('button', { name: 'Next page', exact: true });
  await expect(next).toBeVisible();
  expect((await next.boundingBox())!.height).toBeGreaterThanOrEqual(44);
});

test('phone work queue tabs stay on one scrolling row instead of wrapping', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  await openAdmin(page, '/admin/work-queue/');
  const tabs = page.getByRole('tab');
  await expect(tabs.first()).toBeVisible();
  const tops = new Set<number>();
  const count = await tabs.count();
  for (let i = 0; i < count; i++) tops.add(Math.round((await tabs.nth(i).boundingBox())!.y));
  expect(tops.size).toBe(1);
});

test('phone tables explain sideways scrolling and can be reached from the keyboard', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  await openAdmin(page, '/admin/invoices/');
  await expect(page.getByText('Scroll sideways to see all columns.').first()).toBeVisible();
  await expect(page.getByRole('region', { name: 'Issued invoices table' })).toHaveAttribute('tabindex', '0');
  await openAdmin(page, '/admin/shifts/');
  await expect(page.getByRole('region', { name: 'Shifts table' })).toHaveAttribute('tabindex', '0');
  await openAdmin(page, '/admin/companies/');
  await expect(page.getByRole('region', { name: 'Companies table' })).toHaveAttribute('tabindex', '0');
});

test('dashboard operation cards use the full row width', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await setupAdminMocks(page);
  await openAdmin(page, '/admin/');
  const cards = page.getByRole('region', { name: 'Operations' }).getByRole('link');
  await expect(cards).toHaveCount(2);
  for (let i = 0; i < 2; i++) expect((await cards.nth(i).boundingBox())!.width).toBeGreaterThanOrEqual(450);
});

test('shift list loads employee counts and customer detail opens for a generated company', async ({ page }) => {
  const data = buildScaleData();
  const company = data.companies.find(c => c.name === 'Company 02 Ltd')!;
  await setupAdminMocks(page); await installScaleRoutes(page, data);
  await openAdmin(page, '/admin/shifts/');
  await expect(page.getByText('Could not load employee counts', { exact: false })).toHaveCount(0);
  await openAdmin(page, `/admin/companies/customer/?id=${company.id}`);
  await expect(page.getByText('Customer not found', { exact: true })).toHaveCount(0);
  await expect(page.getByText(company.name, { exact: true }).first()).toBeVisible({ timeout: 15000 });
});

test('sample issuer badge is readable at 12px or larger on phone', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  await openAdmin(page, '/admin/invoices/');
  const badge = page.getByText('Sample issuer details', { exact: true }).first();
  await expect(badge).toBeVisible();
  expect(parseFloat(await badge.evaluate(element => getComputedStyle(element).fontSize))).toBeGreaterThanOrEqual(12);
});

test('phone tabs and buttons meet the 44px touch target on every admin nav page', async ({ page }) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: 390, height: 844 });
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  for (const path of NAV_PAGES) {
    await openAdmin(page, path);
    const targets = page.getByRole('tab').or(page.getByRole('button', { name: 'Next', exact: true })).or(page.getByRole('button', { name: 'Previous', exact: true }));
    const count = await targets.count();
    for (let i = 0; i < count; i++) {
      const target = targets.nth(i);
      if (!(await target.isVisible())) continue;
      expect((await target.boundingBox())!.height, `${path} control ${i}`).toBeGreaterThanOrEqual(44);
    }
  }
});

test('quality cycle template list uses the full content width on desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await setupAdminMocks(page); await installScaleRoutes(page, buildScaleData());
  await openAdmin(page, '/admin/quality-cycle/');
  const list = page.getByText('Existing Templates', { exact: true });
  await expect(list).toBeVisible();
  const card = list.locator('xpath=ancestor::*[contains(@class,"mantine-Paper-root")][1]');
  const content = page.locator('.admin-content');
  const cardWidth = (await card.boundingBox())!.width;
  const contentWidth = (await content.boundingBox())!.width;
  expect(cardWidth).toBeGreaterThanOrEqual(contentWidth * 0.95);
});
