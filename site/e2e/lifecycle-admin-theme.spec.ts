import { test, expect, type Locator, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
async function readable(locator: Locator) {
  await expect.poll(async () => locator.evaluate(element => {
    const luminance = (color: string) => {
      const values = color.match(/[\d.]+/g)?.slice(0, 3).map(Number) ?? [];
      if (values.length !== 3) return Number.NaN;
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
    await readable(page.getByRole('link', { name: 'Services Manage cleaning services, prices and descriptions.' }).getByText('Services', { exact: true }));
    await page.getByRole('link', { name: 'Leads Manage prospects, contact details and customer conversion.' }).click();
    await expect(page).toHaveURL(/\/admin\/leads\//, { timeout: 15000 });
    if (test.info().project.name === 'chromium-desktop') {
      await page.goto('/admin/'); await page.getByRole('navigation').getByRole('link', { name: 'Leads', exact: true }).click();
      await expect(page).toHaveURL(/\/admin\/leads\//, { timeout: 15000 });
    }
    await page.getByRole('button', { name: 'New lead', exact: true }).click();
    const dialog = page.getByRole('dialog'); await readable(dialog.getByRole('heading', { name: 'Company details' }));
    await readable(dialog.getByRole('textbox', { name: 'Company name' }));
    expect(await page.evaluate(() => localStorage.getItem('theme'))).toBe('dark');
  });
  test('all admin sections retain readable headings', async ({ page }) => {
    await page.route('**/api/v1/admin/**', route => {
      const path = new URL(route.request().url()).pathname;
      return route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: path.endsWith('/legacy-repairs') ? { agreements: [] } : path.endsWith('/companies') ? { items: [], total: 0 } : path.endsWith('/issuer') ? null : [] });
    });
    await setupAdminMocks(page); await standaloneDarkSite(page);
    for (const [path, title] of [['companies', 'Companies'], ['shifts', 'Shifts'], ['employees', 'Employees'], ['invoices', 'Invoices'], ['agreements', 'Agreements'], ['services', 'Services'], ['quality-cycle', 'Quality Cycle Management']]) {
      await page.goto(`/admin/${path}/`);
      await readable(page.getByRole('heading', { name: title, exact: true }));
    }
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


