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



for (const width of [375, 768, 1440]) test(`workspace navigation and surfaces at ${width}px`, async ({ page }, testInfo) => {
  await page.setViewportSize({ width, height: 1000 });
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' });
  await setupAdminMocks(page); await standaloneDarkSite(page);
  await page.route('**/api/v1/admin/leads**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: { items: [{ id: 'theme-lead', fields: { name: 'Workspace preview company', businessId: '1234567-8', emails: ['preview@example.test'], primaryEmail: 'preview@example.test', branches: [] }, status: 'New', imageCount: 0, isActive: true }], total: 1 } }));
  await page.goto('/admin/');
  await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
  const header = page.getByRole('banner');
  await expect(header.getByText('Tenant administration', { exact: true })).toBeVisible();
  await expect(header.getByText('Dashboard', { exact: true })).toBeVisible();
  // The shared legacy helper hides the mobile sidebar; exercise the real shell.
  await page.locator('#mock-test-style').evaluate(element => element.remove());
  const main = page.getByRole('main');
  for (const destination of ['shifts', 'quality-cycle', 'leads', 'companies', 'agreements', 'employees', 'invoices', 'services']) {
    await expect(main.locator(`a[href="/admin/${destination}/"]`)).toHaveCount(1);
  }
  const noOverflow = async () => expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await noOverflow();
  await page.screenshot({ path: testInfo.outputPath(`dashboard-${width}.png`), fullPage: true });
  const navigation = page.getByRole('navigation', { name: 'Administration' });
  const burger = page.getByRole('button', { name: 'Toggle navigation' });
  if (width < 768) { await burger.click(); await expect(burger).toHaveAttribute('aria-expanded', 'true'); }
  await expect(navigation.getByText('Operations', { exact: true })).toBeVisible();
  await expect(navigation.getByText('Customers', { exact: true })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Dashboard', exact: true })).toHaveAttribute('aria-current', 'page');
  await navigation.getByRole('link', { name: 'Leads', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Leads', exact: true })).toBeVisible();
  await expect(header.getByText('Tenant administration', { exact: true })).toBeVisible();
  await expect(header.getByText('Leads', { exact: true })).toBeVisible();
  if (width < 768) await expect(burger).toHaveAttribute('aria-expanded', 'false');
  await expect(navigation.getByRole('link', { name: 'Leads', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('cell', { name: 'Workspace preview company' })).toBeVisible();
  await noOverflow();
  await page.screenshot({ path: testInfo.outputPath(`leads-${width}.png`), fullPage: true });
  await page.getByRole('textbox', { name: 'Lead filter', exact: true }).click();
  const option = page.getByRole('option', { name: 'Inactive', exact: true });
  await readable(option);
  await option.click();
  await page.getByRole('button', { name: 'New lead', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await readable(dialog.getByRole('textbox', { name: 'Company name', exact: true }));
  await expect(dialog).toBeVisible(); await noOverflow();
  await expect.poll(() => dialog.evaluate(element => getComputedStyle(element).opacity)).toBe('1');
  await expect.poll(() => dialog.evaluate(element => element.getAnimations({ subtree: true }).filter(animation => animation.playState === 'running').length)).toBe(0);
  await page.screenshot({ path: testInfo.outputPath(`lead-editor-${width}.png`), fullPage: true });
});

for (const priorScheme of [null, 'dark'] as const) test(`leaving admin restores prior Mantine scheme ${priorScheme}`, async ({ page }) => {
  await setupAdminMocks(page); await standaloneDarkSite(page);
  await page.goto('/account/');
  await expect(page.getByRole('link', { name: 'Continue to administration' })).toBeVisible();
  await page.evaluate(prior => {
    if (prior === null) document.documentElement.removeAttribute('data-mantine-color-scheme');
    else document.documentElement.setAttribute('data-mantine-color-scheme', prior);
  }, priorScheme);
  await page.getByRole('link', { name: 'Continue to administration' }).click();
  await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'light');
  await page.getByRole('link', { name: 'My account', exact: true }).filter({ visible: true }).click();
  await expect(page).toHaveURL(/\/account\//);
  await expect.poll(() => page.locator('html').getAttribute('data-mantine-color-scheme')).toBe(priorScheme);
  await expect(page.locator('html')).not.toHaveAttribute('data-admin-theme');
  expect(await page.evaluate(() => localStorage.getItem('theme'))).toBe('dark');
});

test('phone header with own shifts and invoice form remain within viewport', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 375, height: 1000 });
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], employee: { role: 'admin', isActive: true } } }));
  await page.route('**/api/v1/admin/**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: new URL(route.request().url()).pathname.endsWith('/issuer') ? null : [] }));
  await page.goto('/admin/invoices/');
  await expect(page.getByRole('heading', { name: 'Invoices', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const fields = page.getByRole('textbox', { name: 'Billing street', exact: true });
  await expect(fields).toBeVisible(); expect((await fields.boundingBox())!.width).toBeGreaterThan(160);
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe('rgb(246, 247, 251)');
  await expect.poll(() => page.getByText('No invoices yet.', { exact: true }).evaluate(element => getComputedStyle(element).color)).toBe('rgb(89, 100, 119)');
  await page.screenshot({ path: testInfo.outputPath('invoices-own-shifts-375.png'), fullPage: true });
  const accountLink = page.getByRole('banner').getByRole('link', { name: 'My account', exact: true }).filter({ visible: true });
  await expect(accountLink).toBeVisible();
  await expect(accountLink).toHaveAttribute('href', '/account/?tenant=readysetsiivous');
  await accountLink.click();
  await expect(page).toHaveURL(/\/account\/\?tenant=readysetsiivous$/);
  await expect(page.getByRole('heading', { name: 'Your ReadySetSiivous account', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Continue to administration', exact: true })).toBeVisible();
});

// Shapes mirror lifecycle-companies-services, lifecycle-agreements-employees,
// lifecycle-invoice-billing and quality-cycle fixtures. Only list reads are mocked.
async function setupPopulatedWorkspace(page: Page) {
  await setupAdminMocks(page);
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS' };
  const issuer = { companyName: 'Supplier', businessId: '123', companyAddress: { street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI' }, email: 'supplier@example.test', phone: '', bankAccountIBAN: 'FI123', bankBic: 'BIC', paymentTermsDays: 14, defaultVatRatePercent: 25.5 };
  const responses: Record<string, unknown> = {
    companies: { items: [{ id: 'company', name: 'Fixture company Oy', businessId: '1234567-8', isActive: true }], total: 1 },
    employees: [{ id: 'active', firstName: 'Active', lastName: 'Cleaner', email: 'active@example.com', role: 'employee', isActive: true }],
    services: [{ id: 'service-0', slug: 'service-0', name: { values: { fi: 'Service 0' } }, description: { values: { fi: 'Description' } }, isActive: true, priceNet: 10, durationMinutes: 60, vatRatePercent: 25.5, category: 'cleaning', icon: 'Sparkles', sortOrder: 0, isFeatured: false }],
    agreements: [{ id: 'signed', title: 'Signed history', code: 'signed', status: 'Completed', isActive: true, signerCount: 1, signedCount: 1 }],
    'agreements/legacy-repairs': { agreements: [], companies: [] },
    shifts: [],
    'quality-cycle/templates': [{ id: 'template-a', title: 'Activities checklist', companyId: 'company', branchId: 'branch', isActive: true, items: ['Corridor', 'Gym'] }],
    'invoices/issuer': issuer,
    invoices: [{ id: 'invoice', invoiceNumber: 'INV-1', issuer, customer: { name: 'Methehe' }, companyId: 'company', periodFrom: '2026-10-01', periodTo: '2026-10-31', issueDate: '2026-11-01', dueDate: '2026-11-15', status: 'Issued', total: { net: 150, vat: 38.25, gross: 188.25 }, lines: [] }],
  };
  for (const [endpoint, json] of Object.entries(responses)) {
    await page.route(new RegExp(`/api/v1/admin/${endpoint}(?:\\?.*)?$`), route => {
      expect(['GET', 'OPTIONS']).toContain(route.request().method());
      return route.fulfill({ headers, json });
    });
  }
}

test('dashboard groups link to workspaces and sidebar brand returns home on phone', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 1000 });
  await setupPopulatedWorkspace(page);
  await page.goto('/admin/');
  const burger = page.getByRole('button', { name: 'Toggle navigation' });
  for (const [group, destination, heading] of [['Operations', 'shifts', 'Shifts'], ['Customers', 'companies', 'Companies'], ['Business administration', 'services', 'Services']]) {
    await expect(page.getByRole('heading', { name: group, exact: true })).toBeVisible();
    const section = page.getByRole('main').locator('section').filter({ has: page.getByRole('heading', { name: group, exact: true }) });
    await section.locator(`a[href="/admin/${destination}/"]`).click();
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/admin/${destination}/$`));
    // Undo the legacy helper's sidebar suppression before exercising the real drawer.
    await page.evaluate(() => document.getElementById('mock-test-style')?.remove());
    await burger.click(); await expect(burger).toHaveAttribute('aria-expanded', 'true');
    const brand = page.getByRole('navigation', { name: 'Administration' }).getByRole('link', { name: 'ReadySetSiivous', exact: true });
    await expect(brand).toHaveAttribute('href', '/admin/'); await brand.click();
    await expect(page).toHaveURL(/\/admin\/$/);
    await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
    await expect(burger).toHaveAttribute('aria-expanded', 'false');
  }
});

for (const [section, record, actionName] of [
  ['companies', 'Fixture company Oy', 'Edit'], ['employees', 'Active Cleaner', 'Edit'],
  ['agreements', 'Signed history', 'Signers'], ['services', 'Service 0', 'Edit'], ['invoices', 'INV-1', 'PDF'],
]) test(`populated ${section} table keeps actions reachable at 375px`, async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 1000 }); await setupPopulatedWorkspace(page);
  await page.goto(`/admin/${section}/`);
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: record, exact: true }) });
  await expect(row).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const viewport = page.locator('.mantine-ScrollArea-viewport').filter({ has: row });
  expect(await viewport.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  await viewport.evaluate(element => { element.scrollLeft = element.scrollWidth; });
  const action = row.getByRole('button', { name: actionName, exact: true });
  await action.scrollIntoViewIfNeeded(); await expect(action).toBeInViewport();
  await action.click({ trial: true });
  expect(await viewport.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('populated quality checklist cards keep actions reachable at 375px', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 1000 }); await setupPopulatedWorkspace(page);
  await page.goto('/admin/quality-cycle/');
  const card = page.locator('.mantine-Card-root').filter({ has: page.getByText('Activities checklist', { exact: true }) });
  await expect(card).toBeVisible(); await expect(card.getByText('Corridor', { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const edit = card.getByRole('button', { name: 'Edit', exact: true });
  await edit.scrollIntoViewIfNeeded(); await expect(edit).toBeInViewport(); await edit.click();
  await expect(page.getByRole('textbox', { name: 'Template Title', exact: true })).toHaveValue('Activities checklist');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
