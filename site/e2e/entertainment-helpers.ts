import { expect, type Page } from '@playwright/test';

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

/** True once the mocked hub has seen the client connect (JoinLobby invoked). */
export function hubConnected(hub: { invocations: { target: string }[] }): boolean {
  return hub.invocations.some((i) => i.target === 'JoinLobby');
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
  const failures = new Map<string, string>();
  const invocations: { target: string; arguments: unknown[] }[] = [];
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
    if (method === 'POST') {
      const body = route.request().postData() || '';
      try {
        const parsed = JSON.parse(body.replace(RECORD_SEP, ''));
        if (parsed && parsed.type === 1 && parsed.target) {
          invocations.push({ target: parsed.target, arguments: parsed.arguments });
          const failure = failures.get(parsed.target);
          if (failure) { failures.delete(parsed.target); push('gameError', failure); }
          if (parsed.invocationId) queue.push(JSON.stringify({ type: 3, invocationId: parsed.invocationId }) + RECORD_SEP);
        }
      } catch {
        // handshake or malformed body â€” ignore
      }
    }
    return route.fulfill({ status: 200, headers: CORS_HEADERS, body: '' });
  });

  return { push, invocations, failNext: (method: string, message: string) => failures.set(method, message) };
}

export const ROUND = { roundId: 'round-1', roundNumber: 1, translatorPlayerId: 'player-1', sentenceEn: 'The cat sits on the mat.', sentenceFi: null, baseForms: [], hints: [{ english: 'cat', finnish: '' }, { english: 'sit', finnish: '' }, { english: 'mat', finnish: '' }] };
export async function setupGame(page: Page, guest = false) {
  await page.addInitScript(({ guest }) => localStorage.setItem('entSession', JSON.stringify({ groupId: 'group-1', playerId: guest ? 'player-2' : 'player-1', playerToken: guest ? 'token-2' : 'token-1', isCreator: !guest })), { guest });
  await page.route(/\/api\/v1\/entertainment\/groups\/group-1\/lobby/, route => route.fulfill({ status: 200, headers: CORS_HEADERS, json: { groupId: 'group-1', groupName: 'Travel English', status: 'Lobby', players: [{ playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: true }, { playerId: 'player-2', name: 'Bob', level: 'A2', isCreator: false, isOnline: true }] } }));
  const hub = mockEntertainmentSignalR(page);
  await page.goto('/entertainment/'); await expect.poll(() => hubConnected(hub)).toBe(true);
  hub.push('roundStarted', ROUND); await expect(page.getByRole('heading', { name: 'Round 1' })).toBeVisible(); return hub;
}
