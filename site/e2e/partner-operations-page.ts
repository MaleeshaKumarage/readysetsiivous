import type { Page } from '@playwright/test';

/**
 * Partner operations left the admin workspace: the office handles partner work in the Work queue (see work-queue.spec.ts).
 * Tenant managers still use the same Partner operations screen at /manager/partner-requests/, and an admin may open it
 * there too. These specs keep testing that screen through the manager route while reusing their admin API mocks: the
 * page's /api/v1/manager/ calls are sent to the matching /api/v1/admin/ mock, which takes the same requests.
 */
export const PARTNER_OPERATIONS = '/manager/partner-requests/';

export async function viaManagerPage(page: Page) {
  await page.addInitScript(() => {
    const inner = window.fetch;
    window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
      if (url.includes('/api/v1/manager/')) {
        const next = url.replace('/api/v1/manager/', '/api/v1/admin/');
        return inner(typeof input === 'string' || input instanceof URL ? next : new Request(next, input), init);
      }
      return inner(input, init);
    };
  });
}
