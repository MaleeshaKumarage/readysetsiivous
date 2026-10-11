import { test, expect } from '@playwright/test';

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

function mockCreateGroup(page: any, result: any) {
  return page.route(/\/api\/v1\/entertainment\/groups$/, async (route) => {
    if (route.request().method() === 'OPTIONS') {
      return route.fulfill({ status: 200, headers: CORS_HEADERS });
    }
    if (route.request().method() === 'POST') {
      return route.fulfill({ status: 200, json: result, headers: CORS_HEADERS });
    }
    return route.continue();
  });
}

test.describe('Entertainment — Create group', () => {
  test('landing shows create and join options', async ({ page }) => {
    await page.goto('/entertainment/');
    await expect(page.getByRole('heading', { name: 'Language Game' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create group' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Join group' })).toBeVisible();
  });

  test('create group reveals join key in lobby', async ({ page }) => {
    await mockCreateGroup(page, {
      groupId: 'group-1',
      joinKey: 'ABC234',
      creatorPlayerId: 'player-1',
    });

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();

    await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
    await page.getByPlaceholder('Your Gemini API key').fill('sk-test');
    await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
    await page.getByPlaceholder('Your name').fill('Alice');
    await page.getByRole('button', { name: 'B1' }).click();

    await page.getByRole('button', { name: 'Create group' }).click();

    await expect(page.getByText('Group created')).toBeVisible();
    await expect(page.getByText('ABC234')).toBeVisible();
  });

  test('short description is blocked client-side', async ({ page }) => {
    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();

    await page.getByPlaceholder('Describe the topic or theme').fill('too short');

    await expect(page.getByText('At least 200 characters required.')).toBeVisible();
  });
});
