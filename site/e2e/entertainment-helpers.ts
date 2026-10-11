import type { Page } from '@playwright/test';

export const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

export function getBody(route: any) {
  try {
    return route.request().postDataJSON() || {};
  } catch {
    return {};
  }
}

/**
 * Mocks the SignalR hub over the LongPolling transport so e2e can exercise the
 * realtime wiring without a live server. Returns a handle to push hub messages
 * (e.g. a `lobbyUpdated` event) to the page.
 *
 * Protocol (ASP.NET Core SignalR / @microsoft/signalr JSON):
 *  1. POST  .../negotiate   -> connectionId
 *  2. GET   .../hub (poll)  -> empty (transport connect)
 *  3. POST  .../hub         -> handshake send
 *  4. GET   .../hub (poll)  -> `{}` handshake response
 *  5. GET   .../hub (poll)  -> queued messages / empty (keep alive)
 */
export function mockEntertainmentSignalR(page: Page) {
  const queue: string[] = [];
  let pollCount = 0;
  const RECORD_SEP = String.fromCharCode(0x1e);

  const push = (target: string, ...args: unknown[]) => {
    queue.push(JSON.stringify({ type: 1, target, arguments: args }) + RECORD_SEP);
  };

  page.route(/\/hubs\/entertainment/, async (route) => {
    const url = route.request().url();
    const method = route.request().method();

    if (method === 'OPTIONS') {
      return route.fulfill({ status: 200, headers: CORS_HEADERS });
    }

    if (url.includes('/negotiate')) {
      return route.fulfill({
        status: 200,
        headers: CORS_HEADERS,
        json: {
          negotiateVersion: 1,
          connectionId: 'test-conn',
          availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text'] }],
        },
      });
    }

    if (method === 'GET') {
      pollCount += 1;
      // First poll connects; second returns the handshake response.
      if (pollCount === 2) {
        return route.fulfill({ status: 200, headers: CORS_HEADERS, body: '{}' + RECORD_SEP });
      }
      if (queue.length > 0) {
        const msg = queue.shift();
        return route.fulfill({
          status: 200,
          headers: { ...CORS_HEADERS, 'Content-Type': 'application/json' },
          body: msg,
        });
      }
      return route.fulfill({ status: 200, headers: CORS_HEADERS, body: '' });
    }

    // POST: handshake or an invocation send.
    return route.fulfill({ status: 200, headers: CORS_HEADERS, body: '' });
  });

  return { push };
}
