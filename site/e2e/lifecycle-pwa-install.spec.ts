import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const languages = [
  { lang: 'fi', name: 'Asenna ReadySetSiivous-sovellus laitteellesi', tooltip: 'Asenna ReadySetSiivous Sovelluksena', close: 'Sulje iOS-asennusohjeet' },
  { lang: 'sv', name: 'Installera ReadySetSiivous-appen på din enhet', tooltip: 'Installera ReadySetSiivous som app', close: 'Stäng installationsanvisningarna för iOS' },
  { lang: 'en', name: 'Install ReadySetSiivous app on your device', tooltip: 'Install ReadySetSiivous as App', close: 'Close iOS installation instructions' },
];

for (const language of languages) {
  test(`${language.lang}: installation uses saved language and invokes native prompt`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.addInitScript(lang => localStorage.setItem('lang', lang), language.lang);
    await page.goto('/admin/agreements/');
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
    await expect(page.getByText(/^(App )?Installed$/, { exact: true }).filter({ visible: true }).first()).toBeVisible();
  });

  test(`${language.lang}: iOS install modal has a localized accessible close button`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.addInitScript(lang => {
      localStorage.setItem('lang', lang);
      Object.defineProperty(navigator, 'userAgent', { configurable: true, value: 'iPhone' });
    }, language.lang);
    await page.goto('/admin/agreements/');
    await page.getByRole('button', { name: language.name, exact: true }).filter({ visible: true }).first().click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: language.close, exact: true }).click();
    await expect(dialog).not.toBeVisible();
  });
}