import { test, expect, type Locator, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
async function readable(locator: Locator) {
  await expect.poll(async () => locator.evaluate(element => {
    const luminance = (color: string) => {
      const values = color.match(/[\d.]+/g)?.slice(0, 3).map(Number) ?? [];
      if (values.length !== 3) throw new Error(`Unexpected computed color: ${color}`);
      const linear = values.map(value => { const c = value / 255; return c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4; });
      return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
    };
    let parent: Element | null = element; let background = '';
    while (parent) { const color = getComputedStyle(parent).backgroundColor; if (color !== 'rgba(0, 0, 0, 0)' && color !== 'transparent') { background = color; break; } parent = parent.parentElement; }
    const foreground = getComputedStyle(element).color;
    const a = luminance(foreground); const b = luminance(background || 'rgb(255, 255, 255)');
    return b > .8 && (Math.max(a, b) + .05) / (Math.min(a, b) + .05) >= 4.5;
  })).toBe(true);
}
async function standaloneDarkSite(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('theme', 'dark');
    const original = window.matchMedia.bind(window);
    window.matchMedia = query => { const media = original(query); if (query === '(display-mode: standalone)') Object.defineProperty(media, 'matches', { value: true }); return media; };
  });
}
for (const scheme of ['light', 'dark'] as const) test.describe(`admin with ${scheme} system theme`, () => {
  test.use({ colorScheme: scheme });
  test('dashboard and portaled editor stay light and readable in installed mode', async ({ page }) => {
    await setupAdminMocks(page); await standaloneDarkSite(page);
    await page.route('**/api/v1/admin/leads**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: { items: [], total: 0 } }));
    await page.goto('/admin/');
    expect(await page.evaluate(() => matchMedia('(display-mode: standalone)').matches)).toBe(true);
    await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'light');
    await readable(page.getByRole('heading', { name: 'Dashboard', exact: true }));
    await page.goto('/admin/leads/'); await page.getByRole('button', { name: 'New lead', exact: true }).click();
    const dialog = page.getByRole('dialog'); await readable(dialog.getByRole('heading', { name: 'Company details' }));
    await readable(dialog.getByRole('textbox', { name: 'Company name' }));
    expect(await page.evaluate(() => localStorage.getItem('theme'))).toBe('dark');
  });
  test('signed out screen stays light', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: false }); await standaloneDarkSite(page); await page.goto('/admin/');
    await expect(page.getByText('Sign in with your ReadySetSiivous admin account.')).toBeVisible(); await readable(page.getByText('Admin', { exact: true }));
    await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'light');
  });
  test('access denied screen stays light', async ({ page }) => {
    await setupAdminMocks(page); await standaloneDarkSite(page); await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['employee']; });
    await page.goto('/admin/'); await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible(); await readable(page.getByRole('heading', { name: 'Admin access required' }));
  });
});


