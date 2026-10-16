import { test, expect, type Page } from '@playwright/test';
import { CORS_HEADERS, mockEntertainmentSignalR, hubConnected } from './entertainment-helpers';
import { setupGame } from './entertainment-helpers';
test('translator gets English table with no hidden Finnish and keyboard reveals server text', async ({ page }) => {
  const hub = await setupGame(page);
  await expect(page.getByRole('columnheader', { name: 'English' })).toBeVisible(); await expect(page.getByText('cat', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Done', exact: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Skip', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Next round' })).toHaveCount(0);
  const reveal = page.getByRole('button', { name: 'Reveal Finnish for cat' }); await reveal.focus(); await page.keyboard.press('Enter');
  await expect.poll(() => hub.invocations.find(i => i.target === 'RevealHint')?.arguments).toEqual(['group-1', 'player-1', 0, 'round-1']);
  await expect(page.getByText('kissa', { exact: true })).toHaveCount(0);
  hub.push('hintRevealed', { roundId: 'round-1', index: 0, finnish: 'kissa' }); await expect(page.getByTestId('revealed-word')).toHaveText('kissa');
});
test('observer reveals server supplied answer without translator actions', async ({ page }) => {
  const hub = await setupGame(page, true);
  await expect(page.getByLabel('Your Finnish answer')).toHaveCount(0); await expect(page.getByRole('button', { name: 'End game' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Reveal Finnish answer' }).click();
  await expect.poll(() => hub.invocations.find(i => i.target === 'RevealAnswer')?.arguments).toEqual(['group-1', 'player-2', 'round-1']);
  await expect(page.getByText('Kissa istuu matolla.', { exact: true })).toHaveCount(0);
  hub.push('answerRevealed', { roundId: 'round-1', sentenceFi: 'Kissa istuu matolla.' }); await expect(page.getByTestId('sentence-fi-revealed')).toHaveText('Kissa istuu matolla.');
});
