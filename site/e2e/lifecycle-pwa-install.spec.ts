import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const languages = [
  { lang: 'fi', compact: 'Asenna', full: 'Asenna sovellus', name: 'Asenna ReadySetSiivous-sovellus laitteellesi', tooltip: 'Asenna ReadySetSiivous Sovelluksena', close: 'Sulje iOS-asennusohjeet', title: 'Asenna Admin-sovellus iOS-laitteelle', introduction: 'Näin asennat ReadySetSiivous Admin -sovelluksen iPhoneen tai iPadiin:', steps: ['1. Napauta Safarin navigointipalkin Jaa-painiketta.', '2. Vieritä alas ja napauta Lisää Koti-valikkoon.', '3. Napauta oikeassa yläkulmassa Lisää.'], done: 'Selvä', installed: /^(Sovellus asennettu|Asennettu)$/ },
  { lang: 'sv', compact: 'Installera', full: 'Installera app', name: 'Installera ReadySetSiivous-appen på din enhet', tooltip: 'Installera ReadySetSiivous som app', close: 'Stäng installationsanvisningarna för iOS', title: 'Installera Admin-appen på iOS', introduction: 'Så installerar du ReadySetSiivous Admin på din iPhone eller iPad:', steps: ['1. Tryck på Dela i Safaris navigeringsfält.', '2. Rulla ned och tryck på Lägg till på hemskärmen.', '3. Tryck på Lägg till uppe till höger.'], done: 'Jag förstår', installed: /^(Appen är installerad|Installerad)$/ },
  { lang: 'en', compact: 'Install', full: 'Install App', name: 'Install ReadySetSiivous app on your device', tooltip: 'Install ReadySetSiivous as App', close: 'Close iOS installation instructions', title: 'Install Admin App on iOS', introduction: 'To install ReadySetSiivous Admin on your iPhone or iPad:', steps: ["1. Tap the Share button in Safari's navigation bar.", '2. Scroll down and tap Add to Home Screen.', '3. Tap Add in the top right corner.'], done: 'Got it', installed: /^(App )?Installed$/ },
];

for (const language of languages) {
  test(`${language.lang}: installation uses saved language and invokes native prompt`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.addInitScript(lang => localStorage.setItem('lang', lang), language.lang);
    await page.goto('/admin/agreements/');
    const variants = page.getByRole('button', { name: language.name, exact: true, includeHidden: true });
    await expect(variants).toHaveCount(2);
    await expect(variants.nth(0)).toHaveText(language.compact);
    await expect(variants.nth(1)).toHaveText(language.full);
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
  });

  test(`${language.lang}: iOS install modal has a localized accessible close button`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.addInitScript(lang => {
      localStorage.setItem('lang', lang);
      Object.defineProperty(navigator, 'userAgent', { configurable: true, value: 'iPhone' });
    }, language.lang);
    await page.goto('/admin/agreements/');
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
  });
}