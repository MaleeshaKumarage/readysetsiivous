import { test, expect, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { setupAdminMocks } from './admin-helpers';

async function setup(page: Page, mode = 'normal') {
  await setupAdminMocks(page);
  await page.route(/\/api\/v1\/me$/, r => r.fulfill({ json: { realm: 'readysetsiivous', employee: { id: 'e', firstName: 'Test', lastName: 'Staff', role: 'employee', isActive: true } } }));
  const methods: string[] = [];
  await page.route(/\/api\/v1\/me\/notifications.*/, r => {
    methods.push(r.request().method());
    if (r.request().method() === 'GET') return r.fulfill({ json: { available: mode !== 'unavailable', publicKey: 'BA', devices: [] } });
    return r.fulfill({ json: r.request().url().endsWith('/test') ? { accepted: mode !== 'test-rejected' } : { id: 'device-a', enabled: true } });
  });
  await page.addInitScript(mode => {
    (window as any).__permissionCalls = 0;
    let subscription: any = mode === 'switched' ? { unsubscribe: async () => { (window as any).__oldUnsubscribed = true; return true; } } : null;
    if (mode === 'switched') localStorage.setItem('readyset-push-device-v1', JSON.stringify({ identity: 'other-tenant:other-person', id: 'old-device' }));
    const manager = { getSubscription: async () => subscription, subscribe: async () => subscription = { options: {}, toJSON: () => ({ endpoint: 'https://fcm.googleapis.com/test', keys: { p256dh: 'key', auth: 'secret' } }), unsubscribe: async () => { subscription = null; return true; } } };
    const sw = { pushManager: manager };
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { register: async () => { if (mode === 'registration-failure') throw new Error('Registration failed. Reload to retry.'); return sw; }, ready: Promise.resolve(sw), getRegistration: async () => sw } });
    (window as any).PushManager = function () {};
    Object.defineProperty(window, 'Notification', { configurable: true, value: { permission: mode === 'denied' ? 'denied' : 'default', requestPermission: () => { (window as any).__permissionCalls++; return Promise.resolve('granted'); } } });
    if (mode === 'ios') Object.defineProperty(navigator, 'userAgent', { value: 'iPhone OS 17_0' });
  }, mode);
  await page.goto('/account/');
  return methods;
}
test('permission is opt-in, test feedback is honest, disabling affects this device', async ({ page }) => {
  const methods = await setup(page);
  await expect(page.getByRole('button', { name: 'Enable notifications' })).toBeEnabled();
  expect(await page.evaluate(() => (window as any).__permissionCalls)).toBe(0);
  await page.getByRole('button', { name: 'Enable notifications' }).click();
  await expect(page.getByText('Notifications are enabled on this device.')).toBeVisible();
  await page.getByRole('button', { name: 'Send test notification' }).click();
  await expect(page.getByText(/does not confirm delivery/)).toBeVisible();
  await page.getByRole('button', { name: 'Disable on this device' }).click();
  await expect(page.getByText(/Your other devices are unchanged/)).toBeVisible();
  expect(methods).toContain('PUT'); expect(methods).toContain('DELETE');
});
for (const mode of ['denied', 'unavailable', 'ios']) test(`notification guidance: ${mode}`, async ({ page }) => {
  await setup(page, mode);
  await expect(page.getByRole('button', { name: 'Enable notifications' })).toBeDisabled();
  await expect(page.getByRole('status')).toContainText(mode === 'denied' ? 'blocked' : mode === 'ios' ? 'Add to Home Screen' : 'unavailable');
});
test('employee and admin manifests stay distinct', async ({ request }) => {
  const employee = await (await request.get('/employee-manifest.json')).json();
  const admin = await (await request.get('/manifest.json')).json();
  expect(employee.id).toBe('/me/'); expect(employee.start_url).toBe('/me/shifts/'); expect(admin.start_url).toBe('/admin/');
});
test('service worker uses visible malformed fallback and rejects external clicks', async () => {
  const handlers: Record<string, Function> = {}; const shown: any[] = []; const opened: string[] = [];
  const self = { location: { origin: 'https://example.test' }, addEventListener: (name: string, fn: Function) => handlers[name] = fn, registration: { showNotification: async (...args: any[]) => shown.push(args) }, clients: { matchAll: async () => [], openWindow: async (url: string) => opened.push(url) } };
  vm.runInNewContext(readFileSync(path.join(__dirname, '../public/sw.js'), 'utf8'), { self, URL });
  let wait: Promise<any> = Promise.resolve();
  handlers.push({ data: { json: () => { throw new Error(); } }, waitUntil: (p: Promise<any>) => wait = p }); await wait;
  expect(shown[0][1].body).toContain('work schedule');
  for (const body of ['Your scheduled work starts soon. Open My shifts for details.', 'Your work is still running. Open My shifts to finish or submit it.', 'Notifications are ready on this device.', 'New work is available for your approval. Open your account to review it.']) {
    handlers.push({ data: { json: () => ({ body }) }, waitUntil: (p: Promise<any>) => wait = p }); await wait;
    expect(shown[shown.length - 1][1].body).toBe(body);
  }
  handlers.push({ data: { json: () => ({ body: 'Customer private address' }) }, waitUntil: (p: Promise<any>) => wait = p }); await wait;
  expect(shown[shown.length - 1][1].body).not.toContain('Customer');
  handlers.notificationclick({ notification: { close() {}, data: { url: 'https://evil.test/' } }, waitUntil: (p: Promise<any>) => wait = p }); await wait;
  expect(opened).toEqual(['https://example.test/me/shifts/']);
  handlers.notificationclick({ notification: { close() {}, data: { url: '/admin/partner-requests/?tenant=another-tenant&evil=1' } }, waitUntil: (p: Promise<any>) => wait = p }); await wait;
  expect(opened[1]).toBe('https://example.test/admin/partner-requests/?tenant=another-tenant');
  handlers.notificationclick({ notification: { close() {}, data: { url: '/account/?tenant=another-tenant&evil=1' } }, waitUntil: (p: Promise<any>) => wait = p }); await wait;
  expect(opened[2]).toBe('https://example.test/account/?tenant=another-tenant');
});
test('notification deep link selects tenant and still requires login', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: false });
  await page.goto('/me/shifts/?tenant=another-tenant');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('__AUTH_TENANT__'))).toBe('another-tenant');
});
test('logout invalidates browser endpoint when server cleanup fails', async ({ page }) => {
  await setup(page);
  await page.getByRole('button', { name: 'Enable notifications' }).click();
  await expect(page.getByText('Notifications are enabled on this device.')).toBeVisible();
  await page.route(/\/api\/v1\/me\/notifications\/subscription\/device-a$/, r => r.fulfill({ status: 503 }));
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem('readyset-push-device-v1'))).toBeNull();
});
test('account change invalidates previous browser subscription before opt-in', async ({ page }) => {
  const methods = await setup(page, 'switched');
  await expect(page.getByRole('button', { name: 'Enable notifications' })).toBeEnabled();
  expect(await page.evaluate(() => (window as any).__oldUnsubscribed)).toBe(true);
  expect(await page.evaluate(() => localStorage.getItem('readyset-push-device-v1'))).toBeNull();
  expect(methods).not.toContain('PUT');
});
test('registration failure is visible and never requests permission', async ({ page }) => {
  await setup(page, 'registration-failure');
  await expect(page.getByRole('status')).toContainText('Registration failed');
  expect(await page.evaluate(() => (window as any).__permissionCalls)).toBe(0);
});
test('provider rejection never claims the test was accepted', async ({ page }) => {
  await setup(page, 'test-rejected');
  await page.getByRole('button', { name: 'Enable notifications' }).click();
  await page.getByRole('button', { name: 'Send test notification' }).click();
  await expect(page.getByRole('status')).toContainText('was not accepted');
});
test('My shifts renders employee installation and manifest without an admin provider', async ({ page }) => {
  await setup(page);
  await page.route(/\/api\/v1\/me\/shifts(?:\?.*)?$/, r => r.fulfill({ json: [] }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\?.*)?$/, r => r.fulfill({ json: [] }));
  await page.goto('/me/shifts/');
  await expect(page.getByRole('heading', { name: 'My Shifts', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Install My shifts', exact: true })).toBeVisible();
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/employee-manifest.json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
