import { test, expect } from '@playwright/test';
import { CORS, PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

for (const audience of ['admin', 'employee'] as const) {
  for (const outcome of ['accepted', 'dismissed', 'external-install'] as const) {
    test(`${audience} install handles ${outcome} without reusing a consumed prompt`, async ({ page, isMobile }) => {
      const backend = new PartnerFlowBackend();
      await prepareRolePage(page, backend, audience === 'admin' ? 'admin' : 'cleaner');
      await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ headers: CORS, json: route.request().url().includes('/calendar') ? { occurrences: [], timeZoneId: 'Europe/Helsinki' } : [] }));
      await page.goto(audience === 'admin' ? '/admin/partner-requests/' : '/me/shifts/');
      if (audience === 'admin' && isMobile) await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
      if (audience === 'employee') await page.getByRole('tab', { name: 'Settings', exact: true }).click();
      const install = page.getByRole('button', { name: audience === 'admin' ? 'Install ReadySetSiivous app on your device' : 'Install My shifts', exact: true });
      const installed = page.getByText(audience === 'admin' ? /^(App Installed|Installed)$/ : 'My shifts app installed', { exact: true });
      await expect(install).toBeVisible();
      await page.evaluate(outcome => {
        (window as any).__installCalls = 0;
        const event = new Event('beforeinstallprompt', { cancelable: true });
        Object.assign(event, {
          prompt: async () => { (window as any).__installCalls++; },
          userChoice: Promise.resolve({ outcome: outcome === 'accepted' ? 'accepted' : 'dismissed', platform: 'web' }),
        });
        window.dispatchEvent(event);
        (window as any).__installPrevented = event.defaultPrevented;
      }, outcome);
      expect(await page.evaluate(() => (window as any).__installPrevented)).toBe(true);
      // Arrival of the browser event never invokes the native prompt by itself.
      expect(await page.evaluate(() => (window as any).__installCalls)).toBe(0);
      if (outcome === 'external-install') {
        await page.evaluate(() => window.dispatchEvent(new Event('appinstalled')));
        await expect(installed).toBeVisible();
        await expect(install).toHaveCount(0);
        expect(await page.evaluate(() => (window as any).__installCalls)).toBe(0);
      } else {
        await install.click();
        expect(await page.evaluate(() => (window as any).__installCalls)).toBe(1);
        if (outcome === 'accepted') {
          await expect(installed).toBeVisible();
          await expect(install).toHaveCount(0);
        } else {
          await expect(install).toBeVisible();
          await install.click();
          expect(await page.evaluate(() => (window as any).__installCalls)).toBe(1);
          await page.evaluate(() => {
            const event = new Event('beforeinstallprompt', { cancelable: true });
            Object.assign(event, { prompt: async () => { (window as any).__installCalls++; }, userChoice: Promise.resolve({ outcome: 'accepted', platform: 'web' }) });
            window.dispatchEvent(event);
          });
          await install.click();
          await expect(installed).toBeVisible();
          expect(await page.evaluate(() => (window as any).__installCalls)).toBe(2);
        }
      }
      expect(backend.writes).toEqual([]);
    });
  }
}
