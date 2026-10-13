import { test, expect, APIRequestContext } from '@playwright/test';
import { readFileSync } from 'node:fs';
const fixturePath = process.env.AUDIENCE_TEST_CONTEXT_PATH;
test.skip(!fixturePath, 'Requires owned disposable Keycloak/PostgreSQL fixture; never targets production.');
const api = process.env.NEXT_PUBLIC_API_URL!;
function fixture() {
  const f = JSON.parse(readFileSync(fixturePath!, 'utf8'));
  if (!f.realm.startsWith('audience-test-') || !f.email.endsWith('.invalid') || !['127.0.0.1', 'localhost'].includes(new URL(f.keycloakUrl).hostname))
    throw new Error('Authentication integration requires an owned disposable fixture.');
  return f;
}
function claims(token: string) { return JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString()); }
async function loginToken(request: APIRequestContext, f: any, username = f.email, client = 'cleaning-suite-web') {
  const response = await request.post(`${f.keycloakUrl}/realms/${f.realm}/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: client, username, password: f.password, scope: 'openid email' }
  });
  expect(response.status()).toBe(200); return await response.json();
}

test('genuine PKCE login, refresh, API access and browser logout retain resource boundaries', async ({ page, request }) => {
  const f = fixture();
  let browserJwt = '';
  page.on('request', req => { if (req.url() === `${api}/api/v1/me`) browserJwt = req.headers().authorization?.replace(/^Bearer /, '') || ''; });
  await page.goto(`/account/?tenant=${f.realm}`);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect(new URL(page.url()).searchParams.get('code_challenge_method')).toBe('S256');
  await page.locator('#username').fill(f.email); await page.locator('#password').fill(f.password);
  const browserTokenResponse = page.waitForResponse(response => response.url().endsWith('/protocol/openid-connect/token') && response.request().method() === 'POST');
  await page.locator('#kc-login').click();
  const tokens = await (await browserTokenResponse).json();
  await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();
  expect(browserJwt).toBeTruthy();
  expect([claims(browserJwt).aud].flat()).toContain('cleaning-suite-api');
  expect([claims(browserJwt).aud].flat()).not.toContain('cleaning-suite-platform-api');
  expect((await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${browserJwt}` } })).status()).toBe(200);
  // A real refresh session emits the same resource audience, while the ID token remains SPA-only.
  expect([claims(tokens.id_token).aud].flat()).not.toContain('cleaning-suite-api');
  const refresh = await request.post(`${f.keycloakUrl}/realms/${f.realm}/protocol/openid-connect/token`, {
    form: { grant_type: 'refresh_token', client_id: 'cleaning-suite-web', refresh_token: tokens.refresh_token }
  });
  expect(refresh.status()).toBe(200); const refreshed = await refresh.json();
  expect([claims(refreshed.access_token).aud].flat()).toContain('cleaning-suite-api');
  expect((await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${refreshed.access_token}` } })).status()).toBe(200);
  // RP logout ends this browser's session; other independently created sessions are separate.
  // Already-issued access JWTs retain their lifetime.
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  const confirmation = page.getByRole('button', { name: /logout|log out|sign out/i });
  if (await confirmation.count()) await confirmation.first().click();
  await page.goto(`/account/?tenant=${f.realm}`);
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  const afterLogout = await request.post(`${f.keycloakUrl}/realms/${f.realm}/protocol/openid-connect/token`, {
    form: { grant_type: 'refresh_token', client_id: 'cleaning-suite-web', refresh_token: refreshed.refresh_token }
  });
  expect(afterLogout.status()).toBe(400);
  expect((await afterLogout.json()).error).toBe('invalid_grant');
});

test('real tenant, employee, platform and wrong-audience tokens enforce all system routes', async ({ request }) => {
  const f = fixture();
  const serviceSecret = process.env.AUDIENCE_TEST_PLATFORM_SECRET!;
  expect(serviceSecret).toBeTruthy();
  async function service(client: string) {
    const response = await request.post(`${f.keycloakUrl}/realms/master/protocol/openid-connect/token`, {
      form: { grant_type: 'client_credentials', client_id: client, client_secret: serviceSecret }
    });
    expect(response.status()).toBe(200); return (await response.json()).access_token;
  }
  const allowed = await service('audience-platform-allowed');
  const denied = await service('audience-platform-denied');
  expect([claims(allowed).aud].flat()).toContain('cleaning-suite-platform-api');
  expect(claims(allowed).realm_access.roles).toContain('platform.tenants.manage');
  expect(claims(denied).realm_access?.roles || []).not.toContain('platform.tenants.manage');
  const tenant = (await loginToken(request, f)).access_token;
  const employee = (await loginToken(request, f, f.employeeEmail)).access_token;
  const wrong = (await loginToken(request, f, f.email, 'audience-wrong')).access_token;
  const missing = (await loginToken(request, f, f.email, 'audience-missing')).access_token;
  for (const jwt of [wrong, missing]) {
    expect((await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${jwt}` } })).status()).toBe(401);
  }
  for (const jwt of [tenant, employee]) expect((await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${jwt}` } })).status()).toBe(200);
  for (const jwt of [allowed, denied]) {
    expect((await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${jwt}` } })).status()).toBe(403);
    expect((await request.get(`${api}/api/v1/admin/quality-cycle/templates`, { headers: { Authorization: `Bearer ${jwt}` } })).status()).toBe(403);
  }
  const newSlug = `auth-browser-${Date.now().toString(36)}`;
  const payload = { slug: newSlug, companyName: 'Owned auth test', ownerEmail: `owner@${newSlug}.invalid`, ownerFirstName: 'Auth', ownerLastName: 'Test' };
  for (const [jwt, expected] of [[undefined, 401], [wrong, 401], [missing, 401], [denied, 403], [tenant, 403], [employee, 403]] as const) {
    const headers: Record<string, string> = jwt ? { Authorization: `Bearer ${jwt}` } : {};
    expect((await request.get(`${api}/api/v1/system/tenants`, { headers })).status()).toBe(expected);
    expect((await request.post(`${api}/api/v1/system/tenants`, { headers, data: payload })).status()).toBe(expected);
    for (const action of ['suspend', 'activate']) expect((await request.post(`${api}/api/v1/system/tenants/${f.realm}/${action}`, { headers })).status()).toBe(expected);
  }
  const headers = { Authorization: `Bearer ${allowed}` };
  const before = await request.get(`${api}/api/v1/system/tenants`, { headers }); expect(before.status()).toBe(200);
  expect((await before.json()).some((t: any) => t.slug === newSlug)).toBe(false);
  const created = await request.post(`${api}/api/v1/system/tenants`, { headers, data: payload }); expect(created.status()).toBe(201);
  expect((await created.json()).slug).toBe(newSlug);
  expect((await request.post(`${api}/api/v1/system/tenants`, { headers, data: payload })).status()).toBe(409);
  expect((await request.post(`${api}/api/v1/system/tenants/${newSlug}/suspend`, { headers })).status()).toBe(204);
  expect((await request.post(`${api}/api/v1/system/tenants/${newSlug}/activate`, { headers })).status()).toBe(204);
  const after = await request.get(`${api}/api/v1/system/tenants`, { headers });
  expect((await after.json()).find((t: any) => t.slug === newSlug).status).toBe('Active');
});
