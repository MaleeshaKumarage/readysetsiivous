import { test, expect, type Route } from '@playwright/test';
import { CORS_HEADERS, mockEntertainmentSignalR } from './entertainment-helpers';

const LOBBY = { groupId: 'group-1', groupName: 'Travel English', status: 'Lobby', players: [{ playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: true }] };

test('a failed lobby load says so, and retry shows the players without reconnecting', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('entSession', JSON.stringify({ groupId: 'group-1', playerId: 'player-1', playerToken: 'token-1', isCreator: true })));
  // Fails until the test flips it, so the outcome does not depend on how many times the page requests the lobby.
  let failing = true; let calls = 0;
  await page.route(/\/api\/v1\/entertainment\/groups\/group-1\/lobby$/, async (route: Route) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    calls++;
    if (failing) return route.fulfill({ status: 503, headers: CORS_HEADERS, json: { detail: 'unavailable' } });
    return route.fulfill({ status: 200, headers: CORS_HEADERS, json: LOBBY });
  });
  mockEntertainmentSignalR(page);
  await page.goto('/entertainment/');
  await expect(page.getByRole('alert').filter({ hasText: 'The lobby could not be loaded. Check your connection, then retry.' })).toBeVisible();
  failing = false;
  await page.getByRole('button', { name: 'Retry loading lobby', exact: true }).click();
  await expect(page.getByText('Alice', { exact: false }).first()).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'could not be loaded' })).toHaveCount(0);
  expect(calls).toBeGreaterThanOrEqual(2);
});
