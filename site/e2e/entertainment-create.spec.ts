import { test, expect, type Route } from '@playwright/test';

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

function mockCreateGroup(page: any, result: any) {
  return page.route(/\/api\/v1\/entertainment\/groups$/, async (route: Route) => {
    if (route.request().method() === 'OPTIONS') {
      return route.fulfill({ status: 200, headers: CORS_HEADERS });
    }
    if (route.request().method() === 'POST') {
      return route.fulfill({ status: 200, json: result, headers: CORS_HEADERS });
    }
    return route.continue();
  });
}

test.describe('Entertainment â€” Create group', () => {
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
      creatorPlayerId: 'player-1', playerToken: 'secret-player-token',
    });

    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();

    await page.getByPlaceholder('e.g. Travel English').fill('Travel English');
    await page.getByPlaceholder('Describe the topic or theme').fill('x'.repeat(200));
    await page.getByPlaceholder('Your name').fill('Alice');
    await page.getByRole('button', { name: 'B1' }).click();

    await page.getByRole('button', { name: 'Create group' }).click();

    await expect(page.getByText('ABC234')).toBeVisible();
    await expect(page.getByText('Waiting for players to join...')).toBeVisible();
  });

  test('short description is blocked client-side', async ({ page }) => {
    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();

    await page.getByPlaceholder('Describe the topic or theme').fill('too short');

    await expect(page.getByText('At least 10 characters required.')).toBeVisible();
  });

  test('selected language level has a clear visual state', async ({ page }) => {
    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();

    const a2 = page.getByRole('button', { name: 'A2', exact: true });
    const b1 = page.getByRole('button', { name: 'B1', exact: true });
    await b1.click();

    await expect(b1).toHaveAttribute('aria-pressed', 'true');
    await expect(b1).toHaveClass(/bg-primary/);
    await expect(b1.locator('[aria-hidden="true"]')).toHaveText('✓');
    await expect(a2).toHaveAttribute('aria-pressed', 'false');
    await expect(a2).not.toHaveClass(/bg-primary/);
  });

  test('shows the actual API validation reason', async ({ page }) => {
    await page.route(/\/api\/v1\/entertainment\/groups$/, route => route.fulfill({
      status: 400,
      headers: CORS_HEADERS,
      json: { title: 'One or more validation errors occurred.', errors: { Description: ['Choose a more specific game topic.'] } },
    }));
    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();
    await page.getByPlaceholder('e.g. Travel English').fill('Travel');
    await page.getByPlaceholder('Describe the topic or theme').fill('Travel in Finland');
    await page.getByPlaceholder('Your name').fill('Alice');
    await page.getByRole('button', { name: 'B1', exact: true }).click();
    await page.getByRole('button', { name: 'Create group' }).click();

    await expect(page.getByRole('alert')).toHaveText('Choose a more specific game topic.');
  });

  test('create form Back returns to landing', async ({ page }) => {
    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Create group' }).click();
    await expect(page.getByRole('heading', { name: 'Create group' })).toBeVisible();

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByRole('button', { name: 'Join group' })).toBeVisible();
  });

  test('join form Back returns to landing', async ({ page }) => {
    await page.goto('/entertainment/');
    await page.getByRole('button', { name: 'Join group' }).click();
    await expect(page.getByRole('heading', { name: 'Join group' })).toBeVisible();

    await page.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByRole('button', { name: 'Create group' })).toBeVisible();
  });
});

test('create uses platform AI without a key input or request credential', async ({ page }) => {
  let body: Record<string, unknown> = {};
  await page.route(/\/api\/v1\/entertainment\/groups$/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    body = route.request().postDataJSON(); return route.fulfill({ status: 200, headers: CORS_HEADERS, json: { groupId: 'group-1', creatorPlayerId: 'player-1', playerToken: 'private-token', joinKey: 'ABC234' } });
  });
  await page.goto('/entertainment/'); await page.getByRole('button', { name: 'Create group' }).click();
  await expect(page.locator('input[type=password]')).toHaveCount(0);
  await page.getByPlaceholder('e.g. Travel English').fill('Travel'); await page.getByPlaceholder('Describe the topic or theme').fill('Travel in Finland'); await page.getByPlaceholder('Your name').fill('Alice');
  await page.getByRole('button', { name: 'B1' }).click(); await expect(page.getByRole('button', { name: 'B1' })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Create group' }).click(); await expect(page.getByText('ABC234')).toBeVisible();
  expect(body).toEqual({ name: 'Travel', description: 'Travel in Finland', creatorName: 'Alice', creatorLevel: 'B1' });
});
