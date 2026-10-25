import { test, expect } from '@playwright/test';
import fi from '../src/i18n/fi.json';
import en from '../src/i18n/en.json';
import sv from '../src/i18n/sv.json';

const key = 'AB0123456789CDEF'.repeat(4);
const headers = { 'Access-Control-Allow-Origin': '*' };

for (const [lang, dictionary] of Object.entries({ fi, en, sv })) {
  for (const copyFails of [false, true]) {
    test(`${lang}: confirmation key copy ${copyFails ? 'failure' : 'success'} stays in active state only`, async ({ page }) => {
      const labels = dictionary.varaus;
      await page.addInitScript((fail: boolean) => {
        Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
          writeText: async (text: string) => {
            if (fail) throw new Error('Synthetic clipboard denial');
            (window as unknown as { copiedKey: string }).copiedKey = text;
          },
        } });
      }, copyFails);
      await page.route('**/api/v1/public/*/services?*', route => route.fulfill({ headers, json: [{
        id: '11111111-1111-1111-1111-111111111111', name: 'Synthetic cleaning', description: '', priceNet: 50,
      }] }));
      await page.route('**/api/v1/public/*/availability?*', route => route.fulfill({ headers, json: [{ startTime: '12:00', endTime: '13:00' }] }));
      await page.route('**/api/v1/public/*/bookings', route => route.fulfill({ headers, status: 201, json: {
        bookingId: 'synthetic', bookingNumber: 'RSS-2030-000001', customerReference: 'ABC123', managementKey: key,
      } }));
      const urls: string[] = [];
      page.on('request', request => urls.push(request.url()));
      await page.goto(`/${lang}/varaus`);
      await page.getByRole('button', { name: /Synthetic cleaning/ }).click();
      await page.locator('#booking-date').fill('2030-01-15');
      await page.getByRole('button', { name: '12:00', exact: true }).click();
      await page.locator('#customerName').fill('Synthetic customer');
      await page.locator('#customerPhone').fill('123');
      await page.locator('#customerEmail').fill('synthetic@example.invalid');
      await page.locator('#street').fill('Synthetic street');
      await page.locator('#postalCode').fill('00100');
      await page.locator('#city').fill('Helsinki');
      await page.getByRole('button', { name: labels.confirm, exact: true }).click();
      const field = page.getByRole('textbox', { name: labels.managementKey, exact: true });
      await expect(field).toHaveValue(key);
      await expect(field).toHaveAttribute('readonly', '');
      await expect(page.getByText(labels.managementKeyHelp, { exact: true })).toBeVisible();
      await page.getByRole('button', { name: labels.copyManagementKey, exact: true }).click();
      await expect(page.getByRole('status')).toHaveText(copyFails ? labels.managementKeyCopyFailed : labels.managementKeyCopied);
      if (!copyFails) expect(await page.evaluate(() => (window as unknown as { copiedKey: string }).copiedKey)).toBe(key);
      expect(urls.every(url => !url.includes(key))).toBe(true);
      expect(await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))).not.toContain(key);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.reload();
      await expect(page.getByRole('button', { name: /Synthetic cleaning/ })).toBeVisible();
      await expect(field).not.toBeVisible();
      await expect(page.getByText(labels.managementKeyCopied, { exact: true })).not.toBeVisible();
    });
  }
}
