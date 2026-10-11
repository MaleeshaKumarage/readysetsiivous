import { test, expect } from '@playwright/test';
import { CORS_HEADERS, mockEntertainmentSignalR, hubConnected } from './entertainment-helpers';

const TRANSLATOR_ROUND = {
  roundNumber: 1,
  translatorPlayerId: 'player-1',
  sentenceEn: 'The cat sits on the mat.',
  sentenceFi: null,
  baseForms: ['cat', 'sit', 'mat'],
};

function mockCreate(page: any) {
  return page.route(/\/api\/v1\/entertainment\/groups$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      return route.fulfill({ status: 200, headers: CORS_HEADERS });
    }
    return route.fulfill({
      status: 200,
      json: { groupId: 'group-1', joinKey: 'ABC234', creatorPlayerId: 'player-1' },
      headers: CORS_HEADERS,
    });
  });
}

function mockLobby(page: any) {
  return page.route(/\/api\/v1\/entertainment\/groups\/group-1\/lobby$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      return route.fulfill({ status: 200, headers: CORS_HEADERS });
    }
    return route.fulfill({
      status: 200,
      json: {
        groupId: 'group-1',
        groupName: 'Travel English',
        status: 'Lobby',
        players: [
          { playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: true },
          { playerId: 'player-2', name: 'Bob', level: 'A2', isCreator: false, isOnline: true },
        ],
      },
      headers: CORS_HEADERS,
    });
  });
}

async function createAndStart(page: any, hub: any) {
  await page.goto('/entertainment/');
  await page.getByRole('button', { name: 'Create group' }).click();
  await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
  await page.getByPlaceholder('Your Gemini API key').fill('sk-test');
  await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
  await page.getByPlaceholder('Your name').fill('Alice');
  await page.getByRole('button', { name: 'B1' }).click();
  await page.getByRole('button', { name: 'Create group' }).click();
  await expect(page.getByText('Alice')).toBeVisible();
  await expect.poll(() => hubConnected(hub)).toBe(true);

  await page.getByRole('button', { name: 'Start game' }).click();
  hub.push('roundStarted', TRANSLATOR_ROUND);
  await expect(page.getByText('Round 1')).toBeVisible();
}

test.describe('Entertainment — Reveal / Score / Results', () => {
  test('translator reveals a hint and finishes with Done', async ({ page }) => {
    await mockCreate(page);
    await mockLobby(page);
    const hub = mockEntertainmentSignalR(page);

    await createAndStart(page, hub);

    await expect(page.getByTestId('covered-word')).toHaveCount(3);

    await page.getByTestId('covered-word').first().click();
    await expect(page.getByTestId('revealed-word')).toHaveCount(1);

    // Scoring push from the hub reaches the scoreboard.
    hub.push('scoreUpdated', [
      { playerId: 'player-1', score: 0 },
      { playerId: 'player-2', score: 1 },
    ]);
    await expect(page.getByText('Bob')).toBeVisible();
    await expect(page.getByText('1', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Done' }).click();
    await expect
      .poll(() => hub.invocations.some((i: any) => i.target === 'RevealHint'))
      .toBe(true);
    await expect
      .poll(() => hub.invocations.some((i: any) => i.target === 'Done'))
      .toBe(true);
  });

  test('observer reveals the Finnish answer', async ({ page }) => {
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
    await mockLobby(page);
    const hub = mockEntertainmentSignalR(page);

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Join group' }).click();
    await page.getByPlaceholder('6-character key').fill('ABC234');
    await page.getByPlaceholder('Your name').fill('Bob');
    await page.getByRole('button', { name: 'A2' }).click();
    await page.getByRole('button', { name: 'Join group' }).click();
    await expect(page.getByText('Travel English')).toBeVisible();
    await expect.poll(() => hubConnected(hub)).toBe(true);

    hub.push('roundStarted', {
      ...TRANSLATOR_ROUND,
      sentenceFi: 'Kissa istuu matolla.',
    });
    await expect(page.getByText('Round 1')).toBeVisible();

    await page.getByTestId('sentence-fi').click();
    await expect(page.getByTestId('sentence-fi-revealed')).toBeVisible();
    await expect
      .poll(() => hub.invocations.some((i: any) => i.target === 'RevealAnswer'))
      .toBe(true);
  });

  test('game end shows results with a winner', async ({ page }) => {
    await mockCreate(page);
    await mockLobby(page);
    const hub = mockEntertainmentSignalR(page);

    await createAndStart(page, hub);

    hub.push('gameEnded', [
      { playerId: 'player-2', name: 'Bob', score: 9, isCreator: false },
      { playerId: 'player-1', name: 'Alice', score: 3, isCreator: true },
    ]);

    await expect(page.getByText('Results')).toBeVisible();
    await expect(page.getByText('Winner')).toBeVisible();
    await expect(page.getByText('Bob')).toBeVisible();
    await expect(page.getByText('Alice')).toBeVisible();
  });

  test('happy path: create → start → round 1 reveal+done → rotate → end', async ({ page }) => {
    await mockCreate(page);
    await mockLobby(page);
    const hub = mockEntertainmentSignalR(page);

    await createAndStart(page, hub);

    // Round 1: Alice is the translator.
    await expect(page.getByText('Round 1')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Done' })).toBeVisible();

    await page.getByTestId('covered-word').first().click();
    await expect(page.getByTestId('revealed-word')).toHaveCount(1);

    await page.getByRole('button', { name: 'Done' }).click();
    await expect
      .poll(() => hub.invocations.some((i: any) => i.target === 'Done'))
      .toBe(true);

    // Hub pushes round 2 with Bob as translator → Alice becomes an observer.
    hub.push('roundStarted', {
      roundNumber: 2,
      translatorPlayerId: 'player-2',
      sentenceEn: 'The dog runs fast.',
      sentenceFi: 'Koira juoksee nopeasti.',
      baseForms: ['dog', 'run', 'fast'],
    });

    await expect(page.getByText('Round 2')).toBeVisible();
    await expect(page.getByTestId('sentence-fi')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Done' })).toHaveCount(0);

    // Creator ends the game.
    await page.getByRole('button', { name: 'End game' }).click();
    await expect
      .poll(() => hub.invocations.some((i: any) => i.target === 'EndGame'))
      .toBe(true);

    hub.push('gameEnded', [
      { playerId: 'player-1', name: 'Alice', score: 5, isCreator: true },
      { playerId: 'player-2', name: 'Bob', score: 1, isCreator: false },
    ]);

    await expect(page.getByText('Results')).toBeVisible();
    await expect(page.getByText('Winner')).toBeVisible();
  });
});
