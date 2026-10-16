import { test, expect } from '@playwright/test';
import { setupGame, ROUND } from './entertainment-helpers';
const EVALUATION = { isCorrect: false, meaningPoints: 6, finnishPoints: 2, scorePoints: 8, correctedAnswer: 'Kissa istuu matolla.', explanation: 'Use the adessive case to express location.', corrections: [{ original: 'matto', replacement: 'matolla', reason: 'Location needs adessive.' }], grammarRules: ['Use the adessive case (-lla/-llä) to describe being on a surface.'] };
test('typed check shows original, corrected changes and why before explicit Next', async ({ page }) => {
  const hub = await setupGame(page); await page.getByLabel('Your Finnish answer').fill('Kissa istuu matto'); await page.getByRole('button', { name: 'Check answer' }).click();
  await expect.poll(() => hub.invocations.find(i => i.target === 'CheckAnswer')?.arguments).toEqual(['group-1', 'player-1', 'round-1', 'Kissa istuu matto']);
  hub.push('answerEvaluated', { roundId: 'round-1', answer: 'Kissa istuu matto', evaluation: EVALUATION });
  await expect(page.getByTestId('answer-feedback')).toContainText('Original answer: Kissa istuu matto'); await expect(page.getByTestId('word-comparison').getByLabel('Incorrect original word: matto')).toBeVisible(); await expect(page.getByTestId('word-comparison').getByLabel('Corrected word: matolla')).toBeVisible(); await expect(page.locator('mark')).toHaveText('matolla'); await expect(page.locator('del')).toHaveText('matto'); await expect(page.getByTestId('answer-feedback')).toContainText(EVALUATION.explanation); await expect(page.getByRole('region', { name: 'Finnish grammar rules' })).toContainText(EVALUATION.grammarRules[0]);
  await expect(page.getByTestId('round-score')).toContainText('8 / 10 (6 / 7 meaning, 2 / 3 Finnish)');
  await page.getByRole('button', { name: 'Next round' }).click(); await expect.poll(() => hub.invocations.find(i => i.target === 'NextRound')?.arguments).toEqual(['group-1', 'player-1', 'round-1']);
  await expect(page.getByTestId('answer-feedback')).toBeVisible(); hub.push('roundStarted', { ...ROUND, roundId: 'round-2', roundNumber: 2, translatorPlayerId: 'player-2' }); await expect(page.getByRole('heading', { name: 'Round 2' })).toBeVisible();
});
test('provider failure retains typed answer and same round for retry', async ({ page }) => {
  const hub = await setupGame(page); await page.getByLabel('Your Finnish answer').fill('Kissa istuu matto'); await page.getByRole('button', { name: 'Check answer' }).click();
  hub.push('gameError', 'AI unavailable. Please retry.'); hub.push('gameErrorDetail', 'Gemini key slot 1: rate limit or quota exceeded (HTTP 429). Retry after about 30 seconds.'); await expect(page.locator('[role="alert"]').filter({ hasText: 'AI unavailable' })).toContainText('AI unavailable'); await expect(page.getByLabel('Your Finnish answer')).toHaveValue('Kissa istuu matto'); await expect(page.getByRole('heading', { name: 'Round 1' })).toBeVisible();
  const detail = page.getByText('Gemini key slot 1: rate limit or quota exceeded (HTTP 429). Retry after about 30 seconds.'); await expect(detail).not.toBeVisible();
  await page.getByText('View error').click(); await expect(detail).toBeVisible();
  await page.getByRole('button', { name: 'Retry', exact: true }).click(); await expect.poll(() => hub.invocations.filter(i => i.target === 'CheckAnswer').length).toBe(2);
  hub.push('answerEvaluated', { roundId: 'round-1', answer: 'Kissa istuu matto', evaluation: EVALUATION }); await expect(page.getByRole('button', { name: 'Next round' })).toBeVisible();
});
test('accepted alternative has explanation and no fabricated corrections', async ({ page }) => {
  const hub = await setupGame(page); await page.getByLabel('Your Finnish answer').fill('Matolla istuu kissa.'); await page.getByRole('button', { name: 'Check answer' }).click();
  hub.push('answerEvaluated', { roundId: 'round-1', answer: 'Matolla istuu kissa.', evaluation: { isCorrect: true, meaningPoints: 7, finnishPoints: 3, scorePoints: 10, correctedAnswer: 'Matolla istuu kissa.', explanation: 'This alternative word order is correct.', corrections: [] } });
  await expect(page.getByTestId('answer-feedback')).toContainText('This alternative word order is correct.'); await expect(page.locator('mark, del')).toHaveCount(0);
});
test('word diff colors only changed words and preserves Finnish punctuation and spacing', async ({ page }) => {
  const hub = await setupGame(page); const original = 'Minä asun Helsingilla.'; const corrected = 'Minä asun Helsingissä.';
  await page.getByLabel('Your Finnish answer').fill(original); await page.getByRole('button', { name: 'Check answer' }).click();
  hub.push('answerEvaluated', { roundId: 'round-1', answer: original, evaluation: { ...EVALUATION, correctedAnswer: corrected } });
  const comparison = page.getByTestId('word-comparison');
  await expect(comparison.getByLabel('Original sentence')).toHaveText(original);
  await expect(comparison.getByLabel('Corrected sentence')).toHaveText(corrected);
  await expect(comparison.getByLabel('Unchanged word: Minä')).toBeVisible(); await expect(comparison.getByLabel('Unchanged word: asun')).toBeVisible();
  await expect(comparison.getByLabel('Incorrect original word: Helsingilla')).toBeVisible(); await expect(comparison.getByLabel('Corrected word: Helsingissä')).toBeVisible();
});
test('word diff labels additions and removals around repeated Finnish words', async ({ page }) => {
  const hub = await setupGame(page); const original = 'Minä asun, asun täällä.'; const corrected = 'Minä asun täällä oikein.';
  await page.getByLabel('Your Finnish answer').fill(original); await page.getByRole('button', { name: 'Check answer' }).click();
  hub.push('answerEvaluated', { roundId: 'round-1', answer: original, evaluation: { ...EVALUATION, correctedAnswer: corrected } });
  const comparison = page.getByTestId('word-comparison');
  await expect(comparison.getByLabel('Original sentence')).toHaveText(original); await expect(comparison.getByLabel('Corrected sentence')).toHaveText(corrected);
  await expect(comparison.getByLabel('Removed word: asun')).toBeVisible(); await expect(comparison.getByLabel('Added word: oikein')).toBeVisible();
});
test('word diff leaves punctuation unmarked', async ({ page }) => {
  const hub = await setupGame(page); const original = 'Minä asun Helsingissä!'; const corrected = 'Minä asun Helsingissä.';
  await page.getByLabel('Your Finnish answer').fill(original); await page.getByRole('button', { name: 'Check answer' }).click();
  hub.push('answerEvaluated', { roundId: 'round-1', answer: original, evaluation: { ...EVALUATION, correctedAnswer: corrected } });
  const comparison = page.getByTestId('word-comparison');
  await expect(comparison.getByLabel('Original sentence')).toHaveText(original); await expect(comparison.getByLabel('Corrected sentence')).toHaveText(corrected);
  await expect(comparison.locator('[aria-label^="Incorrect"], [aria-label^="Corrected"], [aria-label^="Added"], [aria-label^="Removed"]')).toHaveCount(0);
});
test('current translator is visible to every player and updates with each accepted round', async ({ page }) => {
  const hub = await setupGame(page, true); await expect(page.getByTestId('current-turn')).toHaveText('Current turn: Alice');
  hub.push('roundStarted', { ...ROUND, roundId: 'round-2', roundNumber: 2, translatorPlayerId: 'player-2' }); await expect(page.getByTestId('current-turn')).toHaveText('Current turn: Bob (you)');
  hub.push('roundStarted', { ...ROUND, roundId: 'round-1-stale', roundNumber: 1, translatorPlayerId: 'player-1' }); await expect(page.getByTestId('current-turn')).toHaveText('Current turn: Bob (you)');
});
test('current translator reconnecting status is displayed', async ({ page }) => {
  const hub = await setupGame(page); hub.push('lobbyUpdated', { groupId: 'group-1', groupName: 'Travel English', status: 'InGame', players: [{ playerId: 'player-1', name: 'Alice', level: 'B1', isCreator: true, isOnline: false }, { playerId: 'player-2', name: 'Bob', level: 'A2', isCreator: false, isOnline: true }] });
  await expect(page.getByTestId('current-turn')).toHaveText('Current turn: Alice (reconnecting)');
});
test('server score and end standings display and leave notifies server', async ({ page }) => {
  const hub = await setupGame(page); hub.push('scoreUpdated', [{ playerId: 'player-1', score: 5 }]); await expect(page.getByText('5', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'End game' }).click(); await expect.poll(() => hub.invocations.some(i => i.target === 'EndGame')).toBe(true);
  hub.push('gameEnded', [{ playerId: 'player-1', name: 'Alice', score: 5, isCreator: true }]); await expect(page.getByRole('heading', { name: 'Results' })).toBeVisible(); await expect(page.getByText('Winner')).toBeVisible();
  await page.getByRole('button', { name: 'Leave' }).click(); await expect.poll(() => hub.invocations.some(i => i.target === 'LeaveLobby')).toBe(true); await expect(page.getByRole('button', { name: 'Create group' })).toBeVisible();
});
test('same round snapshot preserves unsent typed answer', async ({ page }) => {
  const hub = await setupGame(page); await page.getByLabel('Your Finnish answer').fill('my unfinished answer'); hub.push('roundStarted', ROUND); await expect(page.getByLabel('Your Finnish answer')).toHaveValue('my unfinished answer');
});
test('snapshot restores submitted evaluation', async ({ page }) => {
  const hub = await setupGame(page); hub.push('roundStarted', { ...ROUND, submittedAnswer: 'Kissa istuu matto', evaluation: EVALUATION }); await expect(page.getByTestId('answer-feedback')).toContainText(EVALUATION.explanation);
});

test('failed Next keeps feedback and retries same transition without advancing', async ({ page }) => {
  const hub = await setupGame(page);
  hub.push('answerEvaluated', { roundId: 'round-1', answer: 'Kissa istuu matto', evaluation: EVALUATION });
  await page.getByRole('button', { name: 'Next round' }).click(); hub.push('gameError', 'Generation unavailable. Retry.');
  await expect(page.locator('[role="alert"]').filter({ hasText: 'Generation unavailable' })).toContainText('Generation unavailable'); await expect(page.getByTestId('answer-feedback')).toBeVisible(); await expect(page.getByRole('heading', { name: 'Round 1' })).toBeVisible();
  await page.getByRole('button', { name: 'Retry', exact: true }).click(); await expect.poll(() => hub.invocations.filter(i => i.target === 'NextRound').length).toBe(2);
});

test('stale feedback and hints cannot alter the current round', async ({ page }) => {
  const hub = await setupGame(page);
  hub.push('hintRevealed', { roundId: 'old-round', index: 0, finnish: 'hidden-stale-hint' });
  hub.push('answerEvaluated', { roundId: 'old-round', answer: 'stale answer', evaluation: EVALUATION });
  await expect(page.getByText('hidden-stale-hint', { exact: true })).toHaveCount(0);
  await expect(page.getByTestId('answer-feedback')).toHaveCount(0);
  await expect(page.getByLabel('Your Finnish answer')).toHaveValue('');
});
