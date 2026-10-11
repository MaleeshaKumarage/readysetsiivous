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
  const RECORD_SEP = String.fromCharCode(0x1e);
  let connCounter = 0;
  const getCounts = new Map<string, number>();

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
      connCounter += 1;
      const connId = `conn-${connCounter}`;
      return route.fulfill({
        status: 200,
        headers: CORS_HEADERS,
        json: {
          negotiateVersion: 1,
          connectionId: connId,
          connectionToken: connId,
          availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text'] }],
        },
      });
    }

    const connId = new URL(url).searchParams.get('id') || 'default';

    if (method === 'GET') {
      const count = (getCounts.get(connId) || 0) + 1;
      getCounts.set(connId, count);
      // First poll connects; second returns the handshake response.
      if (count === 2) {
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
