import { test, expect } from '@playwright/test';
import { CORS_HEADERS, mockEntertainmentSignalR } from './entertainment-helpers';

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
        players: [{ playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: true }],
      },
      headers: CORS_HEADERS,
    });
  });
}

async function createGroup(page: any) {
  await page.goto('/entertainment/');
  await page.getByRole('button', { name: 'Create group' }).click();
  await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
  await page.getByPlaceholder('Your Gemini API key').fill('sk-test');
  await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
  await page.getByPlaceholder('Your name').fill('Alice');
  await page.getByRole('button', { name: 'B1' }).click();
  await page.getByRole('button', { name: 'Create group' }).click();
  await expect(page.getByText('Alice')).toBeVisible();
}

test.describe('Entertainment — Start game + round views', () => {
  test('translator sees English sentence + covered hint words, no Finnish', async ({ page }) => {
    await mockCreate(page);
    await mockLobby(page);
    const hub = mockEntertainmentSignalR(page);

    await createGroup(page);
    await page.getByRole('button', { name: 'Start game' }).click();

    hub.push('roundStarted', {
      roundNumber: 1,
      translatorPlayerId: 'player-1',
      sentenceEn: 'The cat sits on the mat.',
      sentenceFi: null,
      baseForms: ['cat', 'sit', 'mat'],
    });

    await expect(page.getByText('Round 1')).toBeVisible();
    await expect(page.getByTestId('sentence-en')).toHaveText(/cat sits/);
    await expect(page.getByTestId('covered-word')).toHaveCount(3);
    await expect(page.getByTestId('sentence-fi')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Done' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Skip' })).toBeVisible();
  });

  test('observer sees English + covered Finnish + hint words', async ({ page }) => {
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

    hub.push('roundStarted', {
      roundNumber: 1,
      translatorPlayerId: 'player-1',
      sentenceEn: 'The cat sits on the mat.',
      sentenceFi: 'Kissa istuu matolla.',
      baseForms: ['cat', 'sit', 'mat'],
    });

    await expect(page.getByText('Round 1')).toBeVisible();
    await expect(page.getByTestId('sentence-en')).toHaveText(/cat sits/);
    await expect(page.getByTestId('sentence-fi')).toBeVisible();
    await expect(page.getByTestId('covered-word')).toHaveCount(3);
    // Observers have no Done/Skip buttons.
    await expect(page.getByRole('button', { name: 'Done' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Skip' })).toHaveCount(0);
  });
});
