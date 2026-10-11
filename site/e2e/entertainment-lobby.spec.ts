import { test, expect } from '@playwright/test';
import { CORS_HEADERS, mockEntertainmentSignalR, hubConnected } from './entertainment-helpers';

const LOBBY = {
  groupId: 'group-1',
  groupName: 'Travel English',
  status: 'Lobby',
  players: [
    { playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: true },
  ],
};

function mockLobby(page: any, lobby: any = LOBBY) {
  return page.route(/\/api\/v1\/entertainment\/groups\/group-1\/lobby$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      return route.fulfill({ status: 200, headers: CORS_HEADERS });
    }
    return route.fulfill({ status: 200, json: lobby, headers: CORS_HEADERS });
  });
}

test.describe('Entertainment — Join + Lobby', () => {
  test('joiner joins with key and sees the lobby player list', async ({ page }) => {
    await page.route(/\/api\/v1\/entertainment\/groups\/ABC234\/join$/, async (route) => {
      if (route.request().method() === 'OPTIONS') {
        return route.fulfill({ status: 200, headers: CORS_HEADERS });
      }
      return route.fulfill({
        status: 200,
        json: { groupId: 'group-1', playerId: 'player-2', playerName: 'Bob', level: 'A2' },
        headers: CORS_HEADERS,
      });
    });
    await mockLobby(page, {
      ...LOBBY,
      players: [
        ...LOBBY.players,
        { playerId: 'player-2', name: 'Bob', level: 'A2', isCreator: false, isOnline: true },
      ],
    });

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Join group' }).click();

    await page.getByPlaceholder('6-character key').fill('ABC234');
    await page.getByPlaceholder('Your name').fill('Bob');
    await page.getByRole('button', { name: 'A2' }).click();
    await page.getByRole('button', { name: 'Join group' }).click();

    await expect(page.getByText('Travel English')).toBeVisible();
    await expect(page.getByText('Alice')).toBeVisible();
    await expect(page.getByText('Bob')).toBeVisible();
    // Joiner must not see the Host Start button.
    await expect(page.getByRole('button', { name: 'Start game' })).toHaveCount(0);
  });

  test('creator lobby shows Host tag and Start button', async ({ page }) => {
    await page.route(/\/api\/v1\/entertainment\/groups$/, async (route) => {
      if (route.request().method() === 'OPTIONS') {
        return route.fulfill({ status: 200, headers: CORS_HEADERS });
      }
      return route.fulfill({
        status: 200,
        json: { groupId: 'group-1', joinKey: 'ABC234', creatorPlayerId: 'player-1' },
        headers: CORS_HEADERS,
      });
    });
    await mockLobby(page);

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();
    await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
    await page.getByPlaceholder('Your Gemini API key').fill('sk-test');
    await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
    await page.getByPlaceholder('Your name').fill('Alice');
    await page.getByRole('button', { name: 'B1' }).click();
    await page.getByRole('button', { name: 'Create group' }).click();

    await expect(page.getByText('ABC234')).toBeVisible();
    await expect(page.getByText('Alice')).toBeVisible();
    await expect(page.getByText('Host')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Start game' })).toBeVisible();
  });

  test('realtime: hub lobbyUpdated event updates the player list', async ({ page }) => {
    const hub = mockEntertainmentSignalR(page);

    await page.route(/\/api\/v1\/entertainment\/groups$/, async (route) => {
      if (route.request().method() === 'OPTIONS') {
        return route.fulfill({ status: 200, headers: CORS_HEADERS });
      }
      return route.fulfill({
        status: 200,
        json: { groupId: 'group-1', joinKey: 'ABC234', creatorPlayerId: 'player-1' },
        headers: CORS_HEADERS,
      });
    });
    await mockLobby(page); // initial snapshot has only Alice

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();
    await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
    await page.getByPlaceholder('Your Gemini API key').fill('sk-test');
    await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
    await page.getByPlaceholder('Your name').fill('Alice');
    await page.getByRole('button', { name: 'B1' }).click();
    await page.getByRole('button', { name: 'Create group' }).click();

    await expect(page.getByText('Alice')).toBeVisible();
    await expect(page.getByText('Bob')).toHaveCount(0);
    await expect.poll(() => hubConnected(hub)).toBe(true);

    // Push a live lobbyUpdated with a second player.
    hub.push('lobbyUpdated', {
      groupId: 'group-1',
      groupName: 'Travel English',
      status: 'Lobby',
      players: [
        { playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: true },
        { playerId: 'player-2', name: 'Bob', level: 'A2', isCreator: false, isOnline: true },
      ],
    });

    await expect(page.getByText('Bob')).toBeVisible();
  });

  test('Leave from lobby returns to landing', async ({ page }) => {
    await page.route(/\/api\/v1\/entertainment\/groups$/, async (route) => {
      if (route.request().method() === 'OPTIONS') {
        return route.fulfill({ status: 200, headers: CORS_HEADERS });
      }
      return route.fulfill({
        status: 200,
        json: { groupId: 'group-1', joinKey: 'ABC234', creatorPlayerId: 'player-1' },
        headers: CORS_HEADERS,
      });
    });
    await mockLobby(page);

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();
    await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
    await page.getByPlaceholder('Your Gemini API key').fill('sk-test');
    await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
    await page.getByPlaceholder('Your name').fill('Alice');
    await page.getByRole('button', { name: 'B1' }).click();
    await page.getByRole('button', { name: 'Create group' }).click();
    await expect(page.getByText('Alice')).toBeVisible();

    await page.getByRole('button', { name: 'Leave' }).click();
    await expect(page.getByRole('heading', { name: 'Language Game' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create group' })).toBeVisible();
  });
});
