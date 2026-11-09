import { selectDailyScheduleIfEmpty } from './admin-helpers';
import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks, isShiftsRoute, shiftsResponse } from './admin-helpers';
import { shiftAction } from './shift-actions';

// Branch price history: the company form writes dated prices, the shift form picks a version,
// and the pricing dialog shows and saves the version a shift is billed at.
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const company = { id: 'company', name: 'Methehe', isActive: true, businessId: '123' };
const version = (effectiveFrom: string, priceNet: number, mode = 'Monthly') => ({ effectiveFrom, mode, priceNet, vatRatePercent: 25.5, durationMinutes: 120 });

// Mirrors the server: a re-saved price in force adds no version; a changed price or basis adds one dated by the form.
function applyBranchWrite(state: any, body: any) {
  const sorted = [...state.priceVersions].sort((a: any, b: any) => a.effectiveFrom.localeCompare(b.effectiveFrom));
  const inForce = sorted[sorted.length - 1];
  const mode = body.priceMode ?? inForce?.mode ?? 'PerOccurrence';
  const unchanged = inForce && inForce.priceNet === body.priceNet && inForce.vatRatePercent === body.vatRatePercent && inForce.durationMinutes === body.estimatedDurationMinutes && inForce.mode === mode && !body.priceEffectiveFrom;
  if (body.priceNet != null && !unchanged) {
    const effectiveFrom = body.priceEffectiveFrom ?? new Date().toISOString().slice(0, 10);
    state.priceVersions = [...state.priceVersions.filter((v: any) => v.effectiveFrom !== effectiveFrom), { effectiveFrom, mode, priceNet: body.priceNet, vatRatePercent: body.vatRatePercent, durationMinutes: body.estimatedDurationMinutes }];
  }
  Object.assign(state, { name: body.name, city: body.city ?? state.city });
  return state;
}

async function setupCompany(page: Page, branchName: string, versions: any[]) {
  await setupAdminMocks(page);
  const writes: any[] = [];
  const state = { id: 'branch', companyId: 'company', name: branchName, isActive: true, city: 'Helsinki', priceVersions: versions };
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [company], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/companies\/company$/, route => route.fulfill({ headers, json: { company, branches: [{ ...state, priceNet: state.priceVersions.at(-1)?.priceNet ?? null, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true }] } }));
  await page.route(/\/api\/v1\/admin\/companies\/branches\/branch$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const body = route.request().postDataJSON(); writes.push(body);
    applyBranchWrite(state, body);
    return route.fulfill({ headers, json: { ...state, priceNet: state.priceVersions.at(-1)?.priceNet ?? null, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true } });
  });
  return { writes, state };
}

test('branch price is saved with its effective date and basis, and history shows it', async ({ page }) => {
  const { writes } = await setupCompany(page, 'Main', [version('2020-01-01', 100)]);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('group', { name: 'Branch Main' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Edit branch' })).toBeVisible();
  // The edit form starts from the price and basis in force.
  await expect(page.getByRole('textbox', { name: 'Pricing basis', exact: true })).toHaveValue('Fixed monthly fee');

  await page.getByRole('textbox', { name: 'Fixed cleaning price, excl. VAT (€)', exact: true }).fill('120');
  await page.getByLabel('Price effective from (blank = today)', { exact: true }).fill('2020-06-01');
  await page.getByRole('textbox', { name: 'Pricing basis', exact: true }).click();
  await page.getByRole('option', { name: 'Per scheduled hour', exact: true }).click();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Edit branch' })).toHaveCount(0);

  expect(writes).toEqual([expect.objectContaining({ priceNet: 120, priceEffectiveFrom: '2020-06-01', priceMode: 'Hourly' })]);
  await expect(page.getByText(/Price history: 2020-01-01 €100\.00 → 2020-06-01 €120\.00/)).toBeVisible();

  // Reopening shows the saved basis.
  await page.getByRole('group', { name: 'Branch Main' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Pricing basis', exact: true })).toHaveValue('Per scheduled hour');
});

test('editing only the name keeps the billing basis and adds no price version', async ({ page }) => {
  const { writes } = await setupCompany(page, 'Main', [version('2020-01-01', 100)]);
  await page.goto('/admin/companies/');
  await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('group', { name: 'Branch Main' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Main renamed');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Edit branch' })).toHaveCount(0);

  expect(writes).toEqual([expect.objectContaining({ name: 'Main renamed', priceNet: 100, priceMode: 'Monthly' })]);
  expect(writes[0].priceEffectiveFrom).toBeUndefined();
  // Single version: no history line is shown, and the stored basis is still Monthly.
  await expect(page.getByText(/Price history:/)).toHaveCount(0);
  await page.getByRole('group', { name: 'Branch Main renamed' }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Pricing basis', exact: true })).toHaveValue('Fixed monthly fee');
});

test('create shift lets the admin pick an older branch price version', async ({ page }) => {
  await setupAdminMocks(page);
  const created: any[] = [];
  const branch = { id: 'branch', companyId: 'company', name: 'Main', isActive: true, city: 'Helsinki', priceNet: 120, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true,
    priceVersions: [version('2020-01-01', 100), version('2020-06-01', 120)] };
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: [company], total: 1 } }));
  await page.route(/\/api\/v1\/admin\/companies\/company$/, route => route.fulfill({ headers, json: { company, branches: [branch] } }));
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/admin\/shifts(?:\/page)?(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'POST') { const body = route.request().postDataJSON(); created.push(body); return route.fulfill({ status: 201, headers, json: { ...body, id: 'created', isActive: true } }); }
    return route.fulfill({ headers, json: shiftsResponse([], route.request().url()) });
  });

  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Add shift', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Add shift' });
  await modal.getByLabel('Company', { exact: true }).click(); await page.getByRole('option', { name: 'Methehe', exact: true }).click();
  await modal.getByLabel('Branch', { exact: true }).click(); await page.getByRole('option', { name: 'Main', exact: true }).click();

  // With history, the version in force today is the default.
  const picker = modal.getByRole('textbox', { name: 'Price version', exact: true });
  await expect(picker).toHaveValue('From 2020-06-01 · €120.00 excl. VAT');
  await picker.click(); await page.getByRole('option', { name: 'From 2020-01-01 · €100.00 excl. VAT', exact: true }).click();

  await selectDailyScheduleIfEmpty(page); await modal.getByRole('button', { name: 'Create shift' }).click();
  await expect(page.getByRole('dialog', { name: 'Add shift' })).toHaveCount(0);
  expect(created).toEqual([expect.objectContaining({ priceEffectiveFrom: '2020-01-01' })]);
});

test('pricing dialog for a branch-linked shift shows its version and saves a different one', async ({ page }) => {
  await setupAdminMocks(page);
  const shift = { id: 'shift', companyId: 'company', branchId: 'branch', name: 'Biweekly', isActive: true, validFrom: '2020-01-01', schedule: { type: 3 } };
  const versions = [version('2020-01-01', 100), version('2020-06-01', 120)];
  const pricing = { plans: [], overrides: [], branchPriceVersions: versions, branchPriceEffectiveFrom: '2020-01-01' };
  const writes: any[] = [];
  await page.route(/\/api\/v1\/admin\/shifts(?:\/page)?(?:\?.*)?$/, route => route.fulfill({ headers, json: shiftsResponse([shift], route.request().url()) }));
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/pricing$/, route => {
    if (route.request().method() === 'PUT') { writes.push(route.request().postDataJSON()); pricing.branchPriceEffectiveFrom = writes.at(-1).effectiveFrom; return route.fulfill({ headers, json: pricing }); }
    return route.fulfill({ headers, json: pricing });
  });
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/branch-price$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    writes.push(route.request().postDataJSON()); pricing.branchPriceEffectiveFrom = writes.at(-1).effectiveFrom; return route.fulfill({ headers, json: pricing });
  });
  await page.route(/\/api\/v1\/admin\/companies\/company$/, route => route.fulfill({ headers, json: { company, branches: [{ id: 'branch', name: 'Main', isActive: true, priceVersions: versions }] } }));

  await page.goto('/admin/shifts/'); await shiftAction(page, 'Biweekly', 'Pricing');
  const dialog = page.getByRole('dialog').filter({ hasText: 'Shift pricing — Biweekly' });
  await expect(dialog.getByRole('textbox', { name: 'Price version', exact: true })).toHaveValue('From 2020-01-01 · Fixed monthly fee · €100.00 excl. VAT · VAT 25.5%');
  await expect(dialog.getByText('Price history for this property')).toBeVisible();
  await expect(dialog.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true })).toHaveCount(0);

  await dialog.getByRole('textbox', { name: 'Price version', exact: true }).click();
  await page.getByRole('option', { name: /From 2020-06-01/ }).click();
  await dialog.getByRole('button', { name: 'Save price', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes).toEqual([{ effectiveFrom: '2020-06-01' }]);

  // Reopening shows the persisted choice.
  await shiftAction(page, 'Biweekly', 'Pricing');
  await expect(page.getByRole('dialog').filter({ hasText: 'Shift pricing — Biweekly' }).getByRole('textbox', { name: 'Price version', exact: true })).toHaveValue(/From 2020-06-01/);
});

test('a shift with its own plans on a priced branch keeps its manual rate and basis', async ({ page }) => {
  await setupAdminMocks(page);
  const shift = { id: 'shift', companyId: 'company', branchId: 'branch', name: 'Biweekly', isActive: true, validFrom: '2020-01-01', schedule: { type: 3 } };
  const pricing = { plans: [{ mode: 'Monthly', rateNet: 90, vatRatePercent: 25.5, effectiveFrom: '2020-01-01' }], overrides: [], branchPriceVersions: [version('2020-01-01', 100)], branchPriceEffectiveFrom: null };
  await page.route(/\/api\/v1\/admin\/shifts(?:\/page)?(?:\?.*)?$/, route => route.fulfill({ headers, json: shiftsResponse([shift], route.request().url()) }));
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/pricing$/, route => route.fulfill({ headers, json: pricing }));

  await page.goto('/admin/shifts/'); await shiftAction(page, 'Biweekly', 'Pricing');
  const dialog = page.getByRole('dialog').filter({ hasText: 'Shift pricing — Biweekly' });
  await expect(dialog.getByRole('textbox', { name: 'Rate before VAT (€)', exact: true })).toHaveValue('90');
  await expect(dialog.getByRole('textbox', { name: 'Price version', exact: true })).toHaveCount(0);
});

test('per-visit price override still works on a branch-linked shift', async ({ page }) => {
  await setupAdminMocks(page);
  const shift = { id: 'shift', companyId: 'company', branchId: 'branch', name: 'Biweekly', isActive: true, validFrom: '2020-01-01', schedule: { type: 4 } };
  const visit = { startUtc: '2020-06-20T13:00:00Z', endUtc: '2020-06-20T15:00:00Z', assigneeIds: [] };
  const pricing = { plans: [], overrides: [], branchPriceVersions: [version('2020-01-01', 100)], branchPriceEffectiveFrom: '2020-01-01' };
  const writes: any[] = [];
  await page.route(/\/api\/v1\/admin\/shifts(?:\/page)?(?:\?.*)?$/, route => route.fulfill({ headers, json: shiftsResponse([shift], route.request().url()) }));
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/pricing$/, route => route.fulfill({ headers, json: pricing }));
  await page.route(/\/api\/v1\/admin\/shifts\/shift\/(?:occurrences|occurrence-price)(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    if (route.request().method() === 'PUT') { writes.push(route.request().postDataJSON()); return route.fulfill({ status: 204, headers }); }
    return route.fulfill({ headers, json: [visit] });
  });

  await page.goto('/admin/shifts/'); await shiftAction(page, 'Biweekly', 'Occurrences');
  await page.getByRole('button', { name: 'Visit price', exact: true }).click();
  const dialog = page.getByRole('dialog').filter({ hasText: 'Visit price — Biweekly' });
  await dialog.getByRole('textbox', { name: 'Visit price before VAT (€)', exact: true }).fill('75');
  await dialog.getByRole('textbox', { name: 'Reason for visit price override', exact: true }).fill('Extra cleaning requested');
  await dialog.getByRole('button', { name: 'Save price', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(writes).toEqual([{ occurrenceStartUtc: visit.startUtc, rateNet: 75, reason: 'Extra cleaning requested' }]);
});
