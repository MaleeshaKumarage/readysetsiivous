import { test as baseTest, expect, Page } from '@playwright/test';

export async function setupAdminMocks(page: Page, options: { authenticated?: boolean } = {}) {
  const authed = options.authenticated ?? true;
  await page.route(/\/api\/v1\/admin\/partner\/(?:quotes|requests|change-requests)$/, route => route.fulfill({
    headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' },
    json: [],
  }));
  // The quote banner reads the company list to count only partner-enabled companies; empty by default.
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({
    headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' },
    json: { items: [], total: 0 },
  }));
  await page.route(/\/api\/v1\/admin\/tenant$/, route => route.fulfill({
    headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' },
    json: { companyName: 'ReadySetSiivous', timeZoneId: 'Europe/Helsinki' },
  }));

  await page.route(/\/api\/v1\/me$/, route => route.fulfill({
    status: 200,
    headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' },
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['admin'] })], employee: null },
  }));

  await page.addInitScript(({ authed }) => {
    // A reload after logout must retain the logged-out state rather than
    // re-authenticating the mock user on every document load.
    if (authed && sessionStorage.getItem('__MOCK_LOGGED_OUT__') !== 'true') {
      (window as any).__MOCK_AUTHED__ = true;
    } else {
      sessionStorage.setItem('__MOCK_LOGGED_OUT__', 'true');
      (window as any).__MOCK_AUTHED__ = false;
    }

    const injectStyle = () => {
      if (document.head && !document.getElementById('mock-test-style')) {
        const style = document.createElement('style');
        style.id = 'mock-test-style';
        style.innerHTML = `
          @media (max-width: 768px) {
            .mantine-AppShell-navbar { display: none !important; pointer-events: none !important; }
          }
        `;
        document.head.appendChild(style);
      }
    };

    if (document.head) {
      injectStyle();
    } else {
      document.addEventListener('DOMContentLoaded', injectStyle);
    }

    const originalFetch = window.fetch;
    window.fetch = async (...args) => {
      const input = args[0];
      const url = typeof input === 'string' ? input : (input && typeof input === 'object' && 'url' in input ? String((input as any).url) : '');

      if (url.includes('auth.readysetsiivous.fi')) {
        return new Response(JSON.stringify({
          realm: 'readysetsiivous',
          public_key: 'mock',
          token_endpoint: 'http://localhost/token',
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      return originalFetch(...args);
    };
  }, { authed });
}

export { expect };

/** Opens or closes the phone navigation drawer. The desktop sidebar has no toggle, so this is a no-op there. */
export async function setNavOpen(page: Page, open: boolean) {
  // Wait for the admin header first, so the toggle's visibility is known before deciding.
  await expect(page.getByText('Tenant administration', { exact: true }).first()).toBeVisible({ timeout: 15000 });
  const toggle = page.getByRole('button', { name: 'Toggle navigation', exact: true });
  if (!(await toggle.isVisible())) return;
  if ((await toggle.getAttribute('aria-expanded')) === String(open)) return;
  if (open) await toggle.click();
  else await page.keyboard.press('Escape'); // the open drawer covers the toggle, so close it with Escape
  await expect(toggle).toHaveAttribute('aria-expanded', String(open));
  // The drawer's links mount after the open animation starts; wait for them before the caller looks inside.
  if (open) await expect(page.locator('#admin-navigation a[href]').first()).toBeAttached({ timeout: 15000 });
}
