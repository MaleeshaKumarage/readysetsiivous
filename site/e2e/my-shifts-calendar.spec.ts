import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { MyShiftOccurrence } from '../src/lib/meApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
function shift(id: string, shiftName: string, startUtc: string, completed = false, isDeleted = false): MyShiftOccurrence {
  const endUtc = new Date(Date.parse(startUtc) + 7200000).toISOString();
  return { shiftId: id, shiftName, startUtc, endUtc, timeZoneId: 'Europe/Helsinki', isDeleted,
    form: completed ? { id: `form-${id}`, shiftId: id, shiftName, employeeId: 'self', employeeName: 'Cleaner', templateId: 'template', templateTitle: 'Activities',
      shiftOccurrenceUtc: startUtc, shiftOccurrenceEndUtc: endUtc, token: 'self-token', items: [{ itemText: 'Corridor', isChecked: true }],
      photoUrls: [], isSubmitted: true, startedAtUtc: startUtc, endedAtUtc: endUtc, submittedUtc: endUtc, createdUtc: startUtc } : null };
}

async function calendar(page: Page) {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-10-17T12:00:00Z') });
  const state = { failEarlier: false, holdPast: null as Promise<void> | null, calls: [] as { view: string; cursor: string | null }[] };
  await page.route(/\/api\/v1\/me\/shifts\/calendar\?/, async route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url()); const view = url.searchParams.get('view')!; const cursor = url.searchParams.get('cursor');
    state.calls.push({ view, cursor });
    if (view === 'past' && state.holdPast) await state.holdPast;
    if (view === 'past' && cursor && state.failEarlier) return route.fulfill({ status: 503, headers });
    const occurrences = view === 'today' ? [shift('today', 'Today cleaning', '2026-10-17T08:00:00Z')]
      : view === 'future' ? cursor ? [shift('later', 'Later cleaning', '2026-11-20T08:00:00Z')]
        : [shift('soon', 'Upcoming cleaning', '2026-10-20T08:00:00Z'), shift('next-month', 'Next month cleaning', '2026-11-03T08:00:00Z')]
      : cursor ? [shift('older', 'Older recorded work', '2026-09-10T08:00:00Z', true)]
        : [shift('gym', 'Past gym', '2026-10-16T12:00:00Z', true), shift('corridor', 'Past corridor', '2026-10-16T08:00:00Z', true, true),
          shift('thursday', 'Previous day', '2026-10-15T08:00:00Z', true), shift('old-week', 'Previous week', '2026-10-08T08:00:00Z', true, true)];
    const detachedStarted = occurrences.find(item => item.shiftId === 'old-week');
    if (detachedStarted?.form) detachedStarted.form.isSubmitted = false;
    return route.fulfill({ headers, json: { occurrences, timeZoneId: 'Europe/Helsinki',
      fromLocalDate: view === 'past' ? cursor ? '2026-08-16' : '2026-09-16' : view === 'future' ? cursor ?? '2026-10-18' : '2026-10-17',
      toLocalDate: view === 'past' ? cursor ?? '2026-10-17' : view === 'future' ? cursor ? '2026-12-19' : '2026-11-18' : '2026-10-18' } });
  });
  return state;
}

test('Today is default; past weeks show daily counts while folded, with only one week and day open', async ({ page }, testInfo) => {
  await calendar(page); await page.goto('/me/shifts/');
  await expect(page.getByRole('tab', { name: 'Today', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('heading', { name: 'Today cleaning' })).toBeVisible();
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  const recent = page.getByTestId('shift-week-2026-10-12'); const older = page.getByTestId('shift-week-2026-10-05');
  await expect(recent.getByRole('button', { name: /Week of/ })).toHaveAttribute('aria-expanded', 'false');
  await expect(recent.getByRole('button', { name: /Oct 16.*2 shifts/ })).toBeVisible();
  expect(await page.locator('[data-testid^="shift-week-"]').evaluateAll(nodes => nodes.map(node => node.getAttribute('data-testid'))))
    .toEqual(['shift-week-2026-10-12', 'shift-week-2026-10-05']);
  await recent.getByRole('button', { name: /Oct 16.*2 shifts/ }).click();
  await expect(page.getByRole('heading', { name: 'Past gym' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Past corridor' })).toBeVisible();
  await expect(recent.getByText('Deleted shift · historical record')).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('past-calendar.png'), fullPage: true });
  await recent.getByRole('button', { name: /Oct 15.*1 shift/ }).click();
  await expect(page.getByRole('heading', { name: 'Past corridor' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Previous day' })).toBeVisible();
  await older.getByRole('button', { name: /Oct 8.*1 shift/ }).click();
  await expect(recent.getByRole('button', { name: /Week of/ })).toHaveAttribute('aria-expanded', 'false');
  await expect(page.getByRole('region', { name: /^Shifts on / })).toHaveCount(1);
  await expect(page.getByText('In progress', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Record activities & end' })).toHaveCount(0);
  await older.getByRole('button', { name: /Week of/ }).click();
  await expect(page.getByRole('region', { name: /^Shifts on / })).toHaveCount(0);
  await expect(older.getByRole('button', { name: /Oct 8.*1 shift/ })).toBeVisible();
  await page.getByRole('tab', { name: 'Future', exact: true }).click();
  const future = page.getByTestId('shift-week-2026-11-02');
  await future.getByRole('button', { name: /Nov 3.*1 shift/ }).click();
  await expect(page.getByRole('heading', { name: 'Next month cleaning' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeDisabled();
});

test('calendar loads earlier and later pages, retains history on failure, and retries the same cursor', async ({ page }) => {
  const state = await calendar(page); await page.goto('/me/shifts/');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  state.failEarlier = true;
  await page.getByRole('button', { name: 'Load earlier shifts' }).click();
  await expect(page.getByRole('alert', { name: 'Shift error' })).toContainText('Could not load your shifts.');
  await expect(page.getByTestId('shift-week-2026-10-12')).toBeVisible();
  state.failEarlier = false;
  await page.getByRole('button', { name: 'Load earlier shifts' }).click();
  await expect(page.getByTestId('shift-week-2026-09-07')).toBeVisible();
  expect(state.calls.filter(call => call.cursor === '2026-09-16')).toHaveLength(2);
  await page.getByRole('tab', { name: 'Future', exact: true }).click();
  await page.getByRole('button', { name: 'Load later shifts' }).click();
  await expect(page.getByTestId('shift-week-2026-11-16')).toBeVisible();
  expect(state.calls.at(-1)).toEqual({ view: 'future', cursor: '2026-11-18' });
});

test('a delayed past response cannot replace the selected future calendar and tabs support keyboard navigation', async ({ page }) => {
  const state = await calendar(page); let release!: () => void;
  state.holdPast = new Promise(resolve => { release = resolve; });
  await page.goto('/me/shifts/'); await expect(page.getByRole('heading', { name: 'Today cleaning' })).toBeVisible();
  await page.getByRole('tab', { name: 'Today', exact: true }).focus();
  await page.keyboard.press('ArrowLeft'); await expect(page.getByRole('tab', { name: 'Past', exact: true })).toBeFocused();
  await page.keyboard.press('End');
  await expect(page.getByTestId('shift-week-2026-11-02')).toBeVisible();
  release();
  await expect(page.getByRole('tab', { name: 'Future', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByTestId('shift-week-2026-10-12')).toHaveCount(0);
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByRole('tab', { name: 'Today', exact: true })).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Future', exact: true })).toBeFocused();
  await page.keyboard.press('Home');
  await expect(page.getByRole('tab', { name: 'Past', exact: true })).toBeFocused();
  await expect(page.getByTestId('shift-week-2026-10-12')).toBeVisible();
});
