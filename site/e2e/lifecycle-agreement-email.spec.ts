import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
for (const denied of [false, true]) {
  test(`agreement rich email copy ${denied ? 'shows failure' : 'keeps HTML button and escaped content'}`, async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });
    await page.addInitScript(({ denied }) => {
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
        write: async (items: ClipboardItem[]) => {
          if (denied) throw new DOMException('Denied', 'NotAllowedError');
          const result: Record<string, string> = {};
          for (const type of items[0].types) result[type] = await (await items[0].getType(type)).text();
          (window as unknown as { copiedEmail: Record<string, string> }).copiedEmail = result;
        },
      } });
    }, { denied });
    const agreement = { id: 'agreement-html', title: 'Cleaning <script>alert(1)</script> & service', code: 'test', status: 'Draft', isActive: true, signerCount: 1, signedCount: 0 };
    await page.route('**/api/v1/admin/agreements**', route => {
      const path = new URL(route.request().url()).pathname;
      const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': '*' };
      if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
      if (path.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: [], companies: [] } });
      if (path.endsWith('/agreement-html')) return route.fulfill({ headers, json: { ...agreement, signers: [{ id: 's', name: 'A <img src=x onerror=alert(1)>', email: 'signer@example.test', token: 'token+&value', status: 'Pending' }] } });
      return route.fulfill({ headers, json: [agreement] });
    });
    await page.goto('/admin/agreements/');
    await page.getByRole('button', { name: 'Signers', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Copy email', exact: true }).click();
    if (denied) {
      await expect(dialog.getByRole('alert')).toContainText('Could not copy the formatted email');
      await expect(dialog.getByRole('button', { name: 'Copied email', exact: true })).toHaveCount(0);
      await expect(dialog.getByRole('button', { name: 'Copy link', exact: true })).toBeVisible();
      return;
    }
    await expect(dialog.getByRole('button', { name: 'Copied email', exact: true })).toBeVisible();
    const copied = await page.evaluate(() => (window as unknown as { copiedEmail: Record<string, string> }).copiedEmail);
    expect(copied['text/plain']).toContain('Cleaning <script>alert(1)</script> & service');
    expect(copied['text/html']).toContain('&lt;script&gt;');
    expect(copied['text/html']).not.toContain('<script>');
    expect(copied['text/html']).not.toContain('<img');
    const email = await page.context().newPage();
    await email.setContent(copied['text/html']);
    await expect(email.getByRole('link', { name: 'Allekirjoita sopimus', exact: true })).toHaveAttribute('href', 'http://localhost:3000/fi/sign?token=token%2B%26value');
    expect(await email.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await email.close();
  });
}
