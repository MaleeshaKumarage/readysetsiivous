import { test, expect, type Page } from '@playwright/test';
import { CORS, PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

async function work(page: Page, started = false) {
  await prepareRolePage(page, new PartnerFlowBackend(), 'cleaner');
  const now = Date.parse('2026-12-01T10:00:00Z');
  await page.clock.install({ time: now });
  const start = new Date(now - 3_600_000).toISOString();
  const occurrences = ['Kitchen', 'Hallway'].map((name, index) => ({
    shiftId: `shift-${index}`, shiftName: name, startUtc: start, endUtc: new Date(now + 3_600_000).toISOString(), timeZoneId: 'Europe/Helsinki',
    notes: '<img src=x onerror="window.__injected=true">\nKeep this as literal cleaning instructions.',
    form: started ? { id: `form-${index}`, shiftId: `shift-${index}`, employeeId: 'cleaner', templateId: 'template', templateTitle: 'Activities', items: [{ itemText: name, isChecked: false }], photos: [], photoUrls: [], isSubmitted: false, startedAtUtc: start, cleanerNotes: '' } : null,
  }));
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ headers: CORS, json: route.request().url().includes('/calendar') ? { occurrences, timeZoneId: 'Europe/Helsinki' } : occurrences }));
  return occurrences;
}

test('employee instructions containing HTML remain literal text', async ({ page }) => {
  await work(page);
  await page.goto('/me/shifts/');
  const instructions = page.getByRole('region', { name: 'Visit instructions' }).first();
  await expect(instructions).toContainText('<img src=x onerror="window.__injected=true">');
  await expect(instructions.locator('img')).toHaveCount(0);
  expect(await page.evaluate(() => (window as any).__injected)).toBeUndefined();
});

test('two in-progress visits keep separate checklist and note drafts', async ({ page }) => {
  await work(page, true);
  await page.goto('/me/shifts/');
  const open = page.getByRole('button', { name: 'Record activities & end', exact: true });
  await open.nth(0).click();
  await page.getByRole('checkbox', { name: 'Kitchen', exact: true }).check();
  await page.getByRole('textbox', { name: 'Shift note', exact: true }).fill('Only kitchen notes');
  await open.nth(1).click();
  await expect(page.getByRole('textbox', { name: 'Shift note', exact: true })).toHaveValue('');
  await expect(page.getByRole('checkbox', { name: 'Hallway', exact: true })).not.toBeChecked();
  await page.getByRole('textbox', { name: 'Shift note', exact: true }).fill('Only hallway notes');
  await open.nth(0).click();
  await expect(page.getByRole('textbox', { name: 'Shift note', exact: true })).toHaveValue('Only kitchen notes');
  await expect(page.getByRole('checkbox', { name: 'Kitchen', exact: true })).toBeChecked();
  await open.nth(1).click();
  await expect(page.getByRole('textbox', { name: 'Shift note', exact: true })).toHaveValue('Only hallway notes');
});

for (const action of ['start', 'end'] as const) {
  test(`employee ${action} stays disabled while pending and writes once`, async ({ page }) => {
    const occurrences = await work(page, action === 'end');
    let release!: () => void;
    const pending = new Promise<void>(resolve => { release = resolve; });
    let calls = 0;
    await page.route(action === 'start' ? /\/me\/shifts\/shift-0\/start$/ : /\/me\/quality-cycle\/form-0\/end$/, async route => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ headers: CORS });
      calls++;
      await pending;
      const form = { id: 'form-0', shiftId: 'shift-0', employeeId: 'cleaner', templateId: 'template', templateTitle: 'Activities', items: [], photos: [], photoUrls: [], cleanerNotes: '', startedAtUtc: new Date().toISOString(), isSubmitted: action === 'end' };
      occurrences[0].form = form as any;
      return route.fulfill({ headers: CORS, json: form });
    });
    await page.goto('/me/shifts/');
    if (action === 'end') await page.getByRole('button', { name: 'Record activities & end', exact: true }).first().click();
    const button = page.getByRole('button', { name: action === 'start' ? 'Start' : 'End shift', exact: true }).first();
    try {
      await button.click();
      await expect.poll(() => calls).toBe(1);
      await expect(button).toBeDisabled();
      if (action === 'end') {
        await expect(page.getByRole('textbox', { name: 'Shift note' })).toBeDisabled();
        await expect(page.getByLabel('Add shift photos')).toBeDisabled();
      }
      expect(calls).toBe(1);
    } finally { release(); }
    if (action === 'start') await expect(page.getByRole('button', { name: 'Record activities & end', exact: true })).toHaveCount(1);
    else await expect(page.getByText('Done', { exact: true })).toBeVisible();
    expect(calls).toBe(1);
  });
}
