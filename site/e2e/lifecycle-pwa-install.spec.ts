import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const language = {
  full: 'Install App', name: 'Install ReadySetSiivous app on your device', tooltip: 'Install ReadySetSiivous as App',
  close: 'Close iOS installation instructions', title: 'Install Admin App on iOS',
  introduction: 'To install ReadySetSiivous Admin on your iPhone or iPad:',
  steps: ["1. Tap the Share button in Safari's navigation bar.", '2. Scroll down and tap Add to Home Screen.', '3. Tap Add in the top right corner.'],
  done: 'Got it', installed: 'App Installed',
};

for (const savedLanguage of ['fi', 'sv', 'en']) {
  test(`${savedLanguage}: installed admin badge stays English without changing saved language`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.addInitScript(lang => {
      localStorage.setItem('lang', lang);
      const original = window.matchMedia.bind(window);
      window.matchMedia = query => {
        const media = original(query);
        if (query === '(display-mode: standalone)') Object.defineProperty(media, 'matches', { value: true });
        return media;
      };
    }, savedLanguage);
    await page.goto('/admin/');
    await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
    await page.locator('#mock-test-style').evaluate(element => element.remove());
    const burger = page.getByRole('button', { name: 'Toggle navigation' });
    if (await burger.isVisible()) await burger.click();
    await expect(page.getByText(language.installed, { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: language.name, exact: true, includeHidden: true })).toHaveCount(0);
    expect(await page.evaluate(() => localStorage.getItem('lang'))).toBe(savedLanguage);
  });

  test(`${savedLanguage}: admin installation stays English and invokes native prompt`, async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 1000 });
    await setupAdminMocks(page);
    await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: { items: [], total: 0 } }));
    await page.route('**/api/v1/admin/agreements**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: new URL(route.request().url()).pathname.endsWith('/legacy-repairs') ? { agreements: [] } : [] }));
    await page.addInitScript(lang => localStorage.setItem('lang', lang), savedLanguage);
    await page.goto('/admin/agreements/');
    await expect(page.getByRole('heading', { name: 'Agreements', exact: true })).toBeVisible();
    await page.locator('#mock-test-style').evaluate(element => element.remove());
    const burger = page.getByRole('button', { name: 'Toggle navigation' });
    await expect(burger).toBeVisible();
    await expect(burger).toHaveAttribute('aria-expanded', 'false');
    await burger.click();
    await expect(burger).toHaveAttribute('aria-expanded', 'true');
    const variants = page.getByRole('button', { name: language.name, exact: true, includeHidden: true });
    await expect(variants).toHaveCount(1);
    await expect(variants.nth(0)).toHaveText(language.full);
    const install = page.getByRole('button', { name: language.name, exact: true }).filter({ visible: true }).first();
    await expect(install).toBeVisible();
    await install.hover();
    await expect(page.getByRole('tooltip')).toHaveText(language.tooltip);
    // Dispatch only after hydration installed the real component listener.
    await page.evaluate(() => {
      const event = new Event('beforeinstallprompt', { cancelable: true });
      Object.assign(event, {
        prompt: async () => { (window as unknown as { installPromptCalls: number }).installPromptCalls = 1; },
        userChoice: Promise.resolve({ outcome: 'accepted', platform: 'web' }),
      });
      window.dispatchEvent(event);
    });
    await install.click();
    await expect.poll(() => page.evaluate(() => (window as unknown as { installPromptCalls: number }).installPromptCalls)).toBe(1);
    await expect(page.getByText(language.installed, { exact: true }).filter({ visible: true }).first()).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('lang'))).toBe(savedLanguage);
  });

  test(`${savedLanguage}: admin iOS instructions and accessible close stay English`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: { items: [], total: 0 } }));
    await page.route('**/api/v1/admin/agreements**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: new URL(route.request().url()).pathname.endsWith('/legacy-repairs') ? { agreements: [] } : [] }));
    await page.addInitScript(lang => {
      localStorage.setItem('lang', lang);
      Object.defineProperty(navigator, 'userAgent', { configurable: true, value: 'iPhone' });
    }, savedLanguage);
    await page.goto('/admin/agreements/');
    await expect(page.getByRole('heading', { name: 'Agreements', exact: true })).toBeVisible();
    await page.locator('#mock-test-style').evaluate(element => element.remove());
    const burger = page.getByRole('button', { name: 'Toggle navigation' });
    if (await burger.isVisible()) await burger.click();
    await page.getByRole('button', { name: language.name, exact: true }).filter({ visible: true }).first().click();
    const dialog = page.getByRole('dialog', { name: language.title, exact: true });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByText(language.introduction, { exact: true })).toBeVisible();
    for (const step of language.steps) await expect(dialog.getByText(step, { exact: true })).toBeVisible();
    await dialog.getByRole('button', { name: language.done, exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await page.getByRole('button', { name: language.name, exact: true }).filter({ visible: true }).first().click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: language.close, exact: true }).click();
    await expect(dialog).not.toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('lang'))).toBe(savedLanguage);
  });
}
