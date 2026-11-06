import { test, expect, Locator, Page } from '@playwright/test';
import { shiftAction } from './shift-actions';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

const visitShift = {
  id: 'visit', companyId: 'company', branchId: 'branch', name: 'Past visit', isActive: true, notes: null, qualityCycleTemplateId: null,
  validFrom: '2026-10-01T00:00:00', validUntil: '2026-10-01T00:00:00',
  schedule: { type: 6, oneTimeDate: '2026-10-01', oneTimeStart: '09:00', oneTimeEnd: '11:30' },
};
const legacyOnCallShift = {
  id: 'legacy', companyId: 'company', branchId: 'branch', name: 'Legacy on-call', isActive: true, notes: null, qualityCycleTemplateId: null,
  validFrom: '2026-10-01T00:00:00', validUntil: null, schedule: { type: 4 },
};

type AdminMock = { writes: any[]; posts: any[] };

async function mockAdmin(page: Page, seeded: any[] = [], options: { failFirstCreate?: boolean; holdSecondCreate?: Promise<void> } = {}): Promise<AdminMock> {
  await setupAdminMocks(page);
  await page.clock.install({ time: new Date('2026-11-03T10:00:00Z') });
  const mock: AdminMock = { writes: [], posts: [] };
  let shifts = seeded;
  await page.route(/\/api\/v1\/admin\//, async route => {
    const method = route.request().method();
    const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (path.endsWith('/shifts') && method === 'POST') {
      const body = route.request().postDataJSON();
      mock.posts.push(body);
      if (options.failFirstCreate && mock.posts.length === 1) return route.fulfill({ status: 500, headers, json: { detail: 'Synthetic failure' } });
      if (options.holdSecondCreate && mock.posts.length === 2) await options.holdSecondCreate;
      mock.writes.push(body);
      const saved = { id: `created-${mock.writes.length}`, isActive: true, ...body };
      shifts = [...shifts, saved];
      return route.fulfill({ headers, json: saved });
    }
    if (path.includes('/shifts/') && method === 'PUT') {
      const body = route.request().postDataJSON();
      mock.writes.push(body);
      const id = path.split('/').at(-1);
      const saved = { ...shifts.find(s => s.id === id), ...body, id };
      shifts = shifts.map(s => s.id === id ? saved : s);
      return route.fulfill({ headers, json: saved });
    }
    return route.fulfill({ headers, json: path.endsWith('/shifts') ? shifts
      : path.endsWith('/companies/company') ? { company: { id: 'company', name: 'AAA', isActive: true }, branches: [{ id: 'branch', companyId: 'company', name: 'AAA1', isActive: true }] }
      : path.endsWith('/companies') ? { items: [{ id: 'company', name: 'AAA', isActive: true }], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [] });
  });
  return mock;
}

async function openAddShift(page: Page) {
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  return page.getByRole('dialog', { name: 'Add shift', exact: true });
}

async function fillCommon(page: Page, dialog: Locator, name: string) {
  await dialog.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'AAA', exact: true }).click();
  await dialog.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'AAA1', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name);
}

async function chooseOneTime(page: Page, dialog: Locator) {
  await dialog.getByLabel('Schedule', { exact: true }).click();
  await page.getByRole('option', { name: 'One-time', exact: true }).click();
}

test('one-time is offered for new shifts while on-call appears only for an existing on-call shift', async ({ page }) => {
  await mockAdmin(page, [legacyOnCallShift]);
  const dialog = await openAddShift(page);
  await dialog.getByLabel('Schedule', { exact: true }).click();
  await expect(page.getByRole('option', { name: 'One-time', exact: true })).toBeVisible();
  await expect(page.getByRole('option', { name: /On-call/ })).toHaveCount(0);
  await page.getByRole('option', { name: 'Daily', exact: true }).click();
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();

  await shiftAction(page, 'Legacy on-call', 'Edit');
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(edit.getByLabel('Schedule', { exact: true })).toHaveValue('On-call / flexible (legacy)');
  await edit.getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(edit).toHaveCount(0);
  await page.getByRole('row').filter({ hasText: 'Legacy on-call' }).getByRole('button', { name: /^More actions for / }).click(); await expect(page.getByRole('menuitem', { name: 'Schedule one-time visit', exact: true })).toBeVisible(); await page.keyboard.press('Escape');
});

test('admin creates a past one-time visit from one form without a second visit popup', async ({ page }) => {
  const mock = await mockAdmin(page);
  const dialog = await openAddShift(page);
  await fillCommon(page, dialog, 'Past visit');
  await chooseOneTime(page, dialog);
  await expect(dialog.getByRole('textbox', { name: 'Start date', exact: true })).toHaveCount(0);
  await dialog.getByRole('textbox', { name: 'Date', exact: true }).fill('2026-10-01');
  await dialog.getByRole('textbox', { name: 'Start time', exact: true }).fill('09:00');
  await dialog.getByRole('textbox', { name: 'End time', exact: true }).fill('11:30');
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();

  await expect(dialog).toHaveCount(0);
  expect(mock.writes).toHaveLength(1);
  expect(mock.writes[0]).toMatchObject({
    schedule: { type: 6, oneTimeDate: '2026-10-01', oneTimeStart: '09:00', oneTimeEnd: '11:30' },
    validFrom: '2026-10-01', validUntil: '2026-10-01',
  });
  const row = page.getByRole('row').filter({ hasText: 'Past visit' });
  await expect(row).toContainText('One-time 2026-10-01 09:00–11:30');
  await row.getByRole('button', { name: /^More actions for / }).click(); await expect(page.getByRole('menuitem', { name: 'Schedule one-time visit', exact: true })).toHaveCount(0); await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: /^Occurrences/ })).toHaveCount(0);
});

test('date, start and end errors block the save and keep the draft', async ({ page }) => {
  const mock = await mockAdmin(page);
  const dialog = await openAddShift(page);
  await fillCommon(page, dialog, 'Past visit');
  await chooseOneTime(page, dialog);

  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('One-time visit date is required.', { exact: true })).toBeVisible();

  await dialog.getByRole('textbox', { name: 'Date', exact: true }).fill('2026-10-01');
  await dialog.getByRole('textbox', { name: 'Start time', exact: true }).fill('');
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('One-time visit start and end times are required.', { exact: true })).toBeVisible();

  await dialog.getByRole('textbox', { name: 'Start time', exact: true }).fill('12:00');
  await dialog.getByRole('textbox', { name: 'End time', exact: true }).fill('11:00');
  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('End time must be after start time.', { exact: true })).toBeVisible();

  expect(mock.posts).toHaveLength(0);
  await expect(dialog.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Past visit');
  await expect(dialog.getByRole('textbox', { name: 'Date', exact: true })).toHaveValue('2026-10-01');
});

test('edit restores the one-time date and times and saves a changed end time', async ({ page }) => {
  const mock = await mockAdmin(page, [visitShift]);
  await page.goto('/admin/shifts/');
  await shiftAction(page, 'Past visit', 'Edit');
  const edit = page.getByRole('dialog', { name: 'Edit shift', exact: true });
  await expect(edit.getByLabel('Schedule', { exact: true })).toHaveValue('One-time');
  await expect(edit.getByRole('textbox', { name: 'Date', exact: true })).toHaveValue('2026-10-01');
  await expect(edit.getByRole('textbox', { name: 'Start time', exact: true })).toHaveValue('09:00');
  await expect(edit.getByRole('textbox', { name: 'End time', exact: true })).toHaveValue('11:30');

  await edit.getByRole('textbox', { name: 'End time', exact: true }).fill('12:00');
  await edit.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect(edit).toHaveCount(0);
  expect(mock.writes).toHaveLength(1);
  expect(mock.writes[0]).toMatchObject({ schedule: { type: 6, oneTimeDate: '2026-10-01', oneTimeStart: '09:00', oneTimeEnd: '12:00' }, validFrom: '2026-10-01', validUntil: '2026-10-01' });
  await expect(page.getByRole('row').filter({ hasText: 'Past visit' })).toContainText('One-time 2026-10-01 09:00–12:00');
});

test('a failed save keeps the draft and a retry creates the visit once', async ({ page }) => {
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  const mock = await mockAdmin(page, [], { failFirstCreate: true, holdSecondCreate: held });
  const dialog = await openAddShift(page);
  await fillCommon(page, dialog, 'Past visit');
  await chooseOneTime(page, dialog);
  await dialog.getByRole('textbox', { name: 'Date', exact: true }).fill('2026-10-01');
  await dialog.getByRole('textbox', { name: 'Start time', exact: true }).fill('09:00');
  await dialog.getByRole('textbox', { name: 'End time', exact: true }).fill('11:30');

  await dialog.getByRole('button', { name: 'Create shift', exact: true }).click();
  await expect(dialog.getByText('Synthetic failure', { exact: true }).first()).toBeVisible();
  await expect(dialog.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Past visit');
  await expect(dialog.getByRole('textbox', { name: 'Date', exact: true })).toHaveValue('2026-10-01');

  const retry = dialog.getByRole('button', { name: 'Create shift', exact: true });
  await retry.click();
  await retry.click({ force: true });
  release();

  await expect(dialog).toHaveCount(0);
  expect(mock.posts).toHaveLength(2);
  expect(mock.writes).toHaveLength(1);
  await expect(page.getByRole('row').filter({ hasText: 'Past visit' })).toHaveCount(1);
});

test('one-time visit price saves only that visit from the occurrences dialog', async ({ page }) => {
  await mockAdmin(page, [visitShift]);
  const visit = { startUtc: '2026-10-01T06:00:00.000Z', endUtc: '2026-10-01T08:30:00.000Z', assigneeIds: [] };
  const priceWrites: any[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/visit\/occurrences(?:\?.*)?$/, route => route.fulfill({ headers, json: [visit] }));
  await page.route(/\/api\/v1\/admin\/shifts\/visit\/pricing$/, route => route.fulfill({ headers, json: { plans: [], overrides: [] } }));
  await page.route(/\/api\/v1\/admin\/shifts\/visit\/occurrence-price$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    priceWrites.push(route.request().postDataJSON());
    return route.fulfill({ headers, json: { plans: [], overrides: [] } });
  });
  await page.goto('/admin/shifts/');
  await shiftAction(page, 'Past visit', 'Occurrences');
  await page.getByRole('button', { name: 'Visit price', exact: true }).click();
  const dialog = page.getByRole('dialog').filter({ hasText: 'Visit price — Past visit' });
  await dialog.getByRole('textbox', { name: 'Visit price before VAT (€)', exact: true }).fill('75');
  await dialog.getByRole('textbox', { name: 'Reason for visit price override', exact: true }).fill('Extra cleaning requested');
  await dialog.getByRole('button', { name: 'Save price', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(priceWrites).toEqual([{ occurrenceStartUtc: visit.startUtc, rateNet: 75, reason: 'Extra cleaning requested' }]);
});

test('one-time shift can be priced per completed visit from the pricing dialog', async ({ page }) => {
  await mockAdmin(page, [visitShift]);
  const planWrites: any[] = [];
  await page.route(/\/api\/v1\/admin\/shifts\/visit\/pricing$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'PUT') {
      planWrites.push(route.request().postDataJSON());
      return route.fulfill({ headers, json: { plans: planWrites, overrides: [] } });
    }
    return route.fulfill({ headers, json: { plans: [], overrides: [] } });
  });
  await page.goto('/admin/shifts/');
  await shiftAction(page, 'Past visit', 'Pricing');
  const dialog = page.getByRole('dialog').filter({ hasText: 'Shift pricing — Past visit' });
  await expect(dialog.getByRole('button', { name: 'Save price', exact: true })).toBeEnabled();
  await dialog.getByLabel('Pricing basis', { exact: true }).click();
  await page.getByRole('option', { name: 'Per completed visit', exact: true }).click();
  await dialog.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true }).fill('40');
  await dialog.getByRole('button', { name: 'Save price', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(planWrites).toEqual([{ mode: 'PerOccurrence', rateNet: 40, vatRatePercent: 25.5, effectiveFrom: '2026-10-01' }]);
});
