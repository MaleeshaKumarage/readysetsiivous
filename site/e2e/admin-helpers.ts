import { test as baseTest, expect, Page } from '@playwright/test';

export async function setupAdminMocks(page: Page, options: { authenticated?: boolean } = {}) {
  const authed = options.authenticated ?? true;

  await page.addInitScript(({ authed }) => {
    if (sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
      (window as any).__MOCK_AUTHED__ = false;
    } else if (sessionStorage.getItem('__MOCK_AUTHED__') === 'true') {
      (window as any).__MOCK_AUTHED__ = true;
    } else {
      (window as any).__MOCK_AUTHED__ = authed;
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
