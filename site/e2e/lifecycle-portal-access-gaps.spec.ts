import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { CORS, OWNER_COMPANY, PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

// UI boundary tests: API authorization itself remains the backend suite's responsibility.
for (const invalid of ['foreign realm', 'inactive person', 'wrong persisted role', 'inactive company', 'disabled partner company']) {
  test(`partner rejects ${invalid} before reading company records`, async ({ page }) => {
    const backend = new PartnerFlowBackend({ priced: true });
    await prepareRolePage(page, backend, 'owner');
    const reads: string[] = [];
    await page.route(/\/api\/v1\/partner\//, route => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ headers: CORS });
      const path = new URL(route.request().url()).pathname;
      reads.push(path);
      return route.fulfill({ headers: CORS, json: {
        realm: invalid === 'foreign realm' ? 'foreign-tenant' : 'readysetsiivous',
        person: { id: 'owner', role: invalid === 'wrong persisted role' ? 'employee' : 'partner', isActive: invalid !== 'inactive person' },
        company: { ...OWNER_COMPANY, isActive: invalid !== 'inactive company', isPartnerEnabled: invalid !== 'disabled partner company' },
        timeZoneId: 'Europe/Helsinki',
      } });
    });
    await page.goto('/partner/');
    await expect(page.getByRole('main').getByRole('alert')).toContainText('Access denied');
    await expect(page.getByRole('tab', { name: 'Properties', exact: true })).toHaveCount(0);
    expect(reads).toEqual(['/api/v1/partner/me']);
    expect(backend.writes).toEqual([]);
  });
}

for (const roles of [[], ['employee'], ['admin', 'partner'], ['manager', 'partner']]) {
  test(`partner rejects token roles ${roles.join('+') || 'none'} without calling partner APIs`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.addInitScript(roles => { (window as any).__MOCK_ROLES__ = roles; }, roles);
    const calls: string[] = [];
    await page.route(/\/api\/v1\/partner\//, route => {
      calls.push(route.request().url());
      return route.fulfill({ headers: CORS, status: 403 });
    });
    await page.goto('/partner/');
    await expect(page.getByRole('main').getByRole('alert')).toContainText('Access denied');
    expect(calls).toEqual([]);
  });
}

for (const role of ['partner', 'employee', 'manager', 'none']) {
  test(`${role} cannot mount finance, bookings or operational admin routes`, async ({ page }) => {
    test.setTimeout(90_000);
    await setupAdminMocks(page);
    await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = role === 'none' ? [] : [role]; }, role);
    const reads: string[] = [];
    await page.route(/\/api\/v1\/admin\//, route => {
      if (route.request().method() !== 'OPTIONS') reads.push(route.request().url());
      return route.fulfill({ headers: CORS, json: [] });
    });
    for (const path of ['invoices', 'bookings', 'inventory', 'partner-requests', 'work-queue', 'shifts/calendar', 'companies/customer/?id=foreign-company']) {
      await page.goto(`/admin/${path.includes('?') ? path : path + '/'}`);
      await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
      expect(reads, `No administrative reads for ${path}`).toEqual([]);
    }
  });
}

for (const invalid of ['inactive admin', 'no persisted roles', 'malformed role shape']) {
  test(`admin rejects ${invalid} before mounting financial data`, async ({ page }) => {
    await setupAdminMocks(page);
    await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers: CORS, json: {
      realm: 'readysetsiivous',
      roles: invalid === 'no persisted roles' ? [] : [JSON.stringify({ roles: invalid === 'malformed role shape' ? 'admin' : ['admin'] })],
      employee: invalid === 'inactive admin' ? { id: 'inactive', role: 'admin', isActive: false } : null,
    } }));
    const calls: string[] = [];
    await page.route(/\/api\/v1\/admin\//, route => {
      calls.push(route.request().url());
      return route.fulfill({ headers: CORS, json: [] });
    });
    await page.goto('/admin/invoices/');
    await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
    expect(calls).toEqual([]);
  });
}
