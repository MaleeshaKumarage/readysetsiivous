import type { Page, Route } from '@playwright/test';

/**
 * CORS headers for stubbed cross-origin responses.
 *
 * The site is a static export (`output: export`) with no Next.js proxy, so the app
 * fetches the API cross-origin using absolute URLs. Playwright's `route.fulfill`
 * still goes through the browser's normal response processing, so a JSON response
 * without `Access-Control-Allow-Origin` is blocked as a CORS failure and the app
 * sees a network error instead of the stubbed payload.
 */
function corsHeaders(): Record<string, string> {
  return {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET,POST,PUT,PATCH,DELETE,OPTIONS',
    'Access-Control-Allow-Headers': '*',
    'Access-Control-Max-Age': '86400',
  };
}

/**
 * Fulfil a route with a JSON body (and the CORS headers needed to read it).
 * `OPTIONS` preflights — triggered by the `application/json` POST on /sign — are
 * answered with 204 so the real request can follow.
 */
export async function json(route: Route, status: number, body: unknown): Promise<void> {
  if (route.request().method() === 'OPTIONS') {
    await route.fulfill({ status: 204, headers: corsHeaders() });
    return;
  }
  await route.fulfill({
    status,
    headers: { ...corsHeaders(), 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/**
 * Replace every backend call with a deterministic stub so the e2e suite never talks
 * to a live API or a real Keycloak instance.
 */
export async function stubApi(page: Page): Promise<void> {
  await page.route('**/api/**', async (route) => {
    const body = route.request().method() === 'GET' ? [] : { ok: true };
    await json(route, 200, body);
  });
}

/**
 * Make keycloak-js fail fast instead of hanging on a connection attempt to a
 * non-existent identity provider. Admin routes initialise auth on load; aborting the
 * realm endpoints lets `initAuth()` settle so the layout can decide what to show.
 */
export async function blockKeycloak(page: Page): Promise<void> {
  await page.route('**/realms/**', (route) => route.abort());
}

/**
 * Capture calls to `window.open` so tests can assert on generated WhatsApp deep links
 * without opening real tabs. Must run before the page's own scripts execute.
 */
export async function recordOpenedWindows(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const state = window as unknown as { __opened: string[] };
    state.__opened = [];
    window.open = ((url?: string | URL) => {
      state.__opened.push(String(url ?? ''));
      return null;
    }) as unknown as typeof window.open;
  });
}

export async function openedWindows(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const state = window as unknown as { __opened?: string[] };
    return state.__opened ?? [];
  });
}
