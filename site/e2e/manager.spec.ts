import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const company = '11111111-1111-4111-8111-111111111111', branch1 = '22222222-2222-4222-8222-222222222222', branch2 = '33333333-3333-4333-8333-333333333333';
const cleaner = '44444444-4444-4444-8444-444444444444';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
async function setup(page: Page, options: { role?: string; documentRole?: string; unbound?: boolean; failIdentity?: boolean; failures?: string[]; inactiveShift?: boolean } = {}) {
  await setupAdminMocks(page); const role = options.role ?? 'manager';
  await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = role ? [role] : []; }, role);
  let identity = 0; let requests = 0; let adminRequests = 0; let shiftLoads = 0;
  const writes: any[] = []; const failures = [...options.failures ?? []];
  let loadFailure = failures.includes('load');
  await page.route(/\/api\/v1\/me$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    identity++;
    return route.fulfill({ status: options.failIdentity && identity === 1 ? 503 : 200, headers,
      json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: role ? [role] : [] })], employee: options.unbound ? null : { id: cleaner, firstName: 'Tenant', lastName: 'Manager', role: options.documentRole ?? role, isActive: true } } });
  });
  await page.route(/\/api\/v1\/admin\//, route => { adminRequests++; return route.fulfill({ status: 403, headers }); });
  let shifts: any[] = [
    { id: '55555555-5555-4555-8555-555555555555', companyId: company, branchId: branch1, name: 'AAA1 weekdays', schedule: { type: 0, dailyStart: '08:00:00', dailyEnd: '10:00:00' }, isActive: true, validFrom: null, validUntil: null },
    { id: '66666666-6666-4666-8666-666666666666', companyId: company, branchId: branch2, name: 'AAA2 biweekly', schedule: { type: 3, biWeeklyDay: 1, biWeeklyWeekParity: 1, biWeeklyStart: '11:00:00', biWeeklyEnd: '14:00:00' }, isActive: !options.inactiveShift, validFrom: null, validUntil: null },
  ];
  let assignments: any[] = [];
  await page.route(/\/api\/v1\/manager\//, route => {
    const request = route.request(); if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    requests++; const path = new URL(request.url()).pathname.split('/manager')[1]; const method = request.method();
    const kind = path.endsWith('deactivate') ? 'deactivate' : path.includes('/assignments') && method === 'POST' ? 'assign' : method === 'GET' && path === '/shifts' ? 'load' : method === 'POST' || method === 'PUT' ? 'save' : '';
    if (kind === 'load' && loadFailure) return route.fulfill({ status: 409, headers, json: { detail: 'load failed; please retry' } });
    if (kind === 'load') {
      shiftLoads++; expect(new URL(request.url()).searchParams.get('includeInactive')).toBe('true');
      return route.fulfill({ status: 200, headers, json: shifts });
    }
    const fail = failures.indexOf(kind); if (fail >= 0) { failures.splice(fail, 1); return route.fulfill({ status: 409, headers, json: { detail: `${kind} failed; please retry` } }); }
    if (method === 'GET') {
      const json = path === '/time-zone' ? { timeZoneId: 'Europe/Helsinki' }
        : path === '/companies' ? { items: [{ id: company, name: 'AAA', isActive: true }], total: 1 }
        : path.endsWith('/branches') ? [{ id: branch1, companyId: company, name: 'AAA1', isActive: true }, { id: branch2, companyId: company, name: 'AAA2', isActive: true }]
        : path === '/employees' ? [{ id: cleaner, firstName: 'Test', lastName: 'Cleaner', phone: '', isActive: true }, { id: 'inactive', firstName: 'Inactive', lastName: 'Cleaner', phone: '', isActive: false }]
        : path.includes('/assignments') ? assignments : shifts;
      return route.fulfill({ status: 200, headers, json });
    }
    if (path.endsWith('/deactivate')) { shifts = shifts.map(s => path.includes(s.id) ? { ...s, isActive: false } : s); return route.fulfill({ status: 204, headers }); }
    if (path.endsWith('/reactivate')) { shifts = shifts.map(s => path.includes(s.id) ? { ...s, isActive: true } : s); return route.fulfill({ status: 200, headers, json: shifts.find(s => path.includes(s.id)) }); }
    if (path.includes('/assignments')) {
      if (method === 'DELETE') { assignments = []; return route.fulfill({ status: 204, headers }); }
      assignments = [{ id: cleaner, employeeId: request.postDataJSON().employeeId, shiftId: path.split('/')[2], isActive: true }];
      return route.fulfill({ status: 200, headers, json: assignments[0] });
    }
    if (method === 'DELETE' && path.startsWith('/shifts/')) {
      shifts = shifts.filter(s => s.id !== path.split('/')[2]);
      return route.fulfill({ status: 204, headers });
    }
    const body = request.postDataJSON(); writes.push(body);
    const saved = { ...body, id: method === 'PUT' ? path.split('/')[2] : `77777777-7777-4777-8777-${String(writes.length).padStart(12, '0')}`, companyId: company, branchId: body.branchId ?? branch1, isActive: true };
    shifts = method === 'PUT' ? shifts.map(s => s.id === saved.id ? saved : s) : [...shifts, saved];
    return route.fulfill({ status: 200, headers, json: saved });
  });
  return { writes, recoverLoad() { loadFailure = false; }, get requests() { return requests; }, get adminRequests() { return adminRequests; }, get shiftLoads() { return shiftLoads; } };
}

test('tenant manager sees both branches and creates, edits, assigns, removes and deactivates shifts', async ({ page }) => {
  const state = await setup(page); await page.goto('/manager/');
  await expect(page.getByRole('article', { name: 'AAA1 weekdays' })).toBeVisible();
  await expect(page.getByRole('article', { name: 'AAA2 biweekly' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Account', exact: true })).toHaveAttribute('href', '/account/?tenant=readysetsiivous');
  await expect(page.getByText(/Payroll|Invoice|Checklist template|Monthly report/)).toHaveCount(0);
  await page.getByRole('button', { name: 'Add shift' }).click(); await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await page.getByRole('button', { name: 'Add shift' }).click();
  await page.getByLabel('Shift name', { exact: true }).fill('New branch2 shift');
  await page.getByLabel('Company', { exact: true }).selectOption(company); await page.getByLabel('Branch', { exact: true }).selectOption(branch2);
  await page.getByRole('button', { name: 'Save shift' }).click();
  const card = page.getByRole('article', { name: 'New branch2 shift' }); await expect(card).toBeVisible();
  await expect(page.getByRole('tab', { name: /^Active/ })).toHaveAttribute('aria-selected', 'true');
  await card.getByRole('button', { name: 'Edit shift' }).click(); await expect(page.getByLabel('Company', { exact: true })).toBeDisabled();
  await page.getByLabel('Shift name', { exact: true }).fill('Edited branch2 shift'); await page.getByRole('button', { name: 'Save shift' }).click();
  const edited = page.getByRole('article', { name: 'Edited branch2 shift' }); await edited.getByRole('button', { name: 'Employees' }).click();
  await edited.getByLabel('Employee', { exact: true }).selectOption(cleaner); await edited.getByRole('button', { name: 'Assign employee' }).click();
  await expect(edited.getByText('Test Cleaner', { exact: true })).toBeVisible(); await edited.getByRole('button', { name: 'Remove assignment' }).click();
  await expect(edited.getByText('No employees assigned.')).toBeVisible(); await edited.getByRole('button', { name: 'Close assignments' }).click();
  await edited.getByRole('button', { name: 'Deactivate shift' }).click(); await expect(edited).toHaveCount(0);
  await page.getByRole('tab', { name: /^Inactive/ }).click(); await expect(edited).toBeVisible();
  await edited.getByRole('button', { name: 'Reactivate shift' }).click(); await expect(edited).toHaveCount(0);
  await page.getByRole('tab', { name: /^Active/ }).click(); await expect(edited).toBeVisible();
  await page.getByRole('button', { name: 'Refresh operations' }).click();
  expect(state.writes[0].branchId).toBe(branch2); expect(state.writes[1]).not.toHaveProperty('branchId');
  for (const write of state.writes) { expect(write).not.toHaveProperty('qualityCycleTemplateId'); expect(write).not.toHaveProperty('notes'); expect(write).not.toHaveProperty('role'); }
  expect(state.adminRequests).toBe(0);
});

for (const role of ['employee', 'staff', '']) test(`${role || 'no-role'} cannot mount manager operations`, async ({ page }) => {
  const state = await setup(page, { role }); await page.goto('/manager/');
  await expect(page.getByRole('main').getByRole('alert')).toContainText('Access denied'); expect(state.requests).toBe(0);
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveCount(0);
});
for (const options of [{ documentRole: 'employee' }, { unbound: true }]) test(`manager ${options.unbound ? 'without binding' : 'with stale document role'} denied`, async ({ page }) => {
  const state = await setup(page, options); await page.goto('/manager/'); await expect(page.getByRole('main').getByRole('alert')).toContainText('Access denied'); expect(state.requests).toBe(0);
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveCount(0);
});
test('verification and operational failures are visible and retry preserves entered shift data', async ({ page }) => {
  const state = await setup(page, { failIdentity: true, failures: ['load', 'save', 'assign', 'deactivate'] }); await page.goto('/manager/');
  await expect(page.getByRole('button', { name: 'Retry access check' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Retry access check' }).click(); await expect(page.getByRole('main').getByRole('alert')).toContainText('load failed');
  state.recoverLoad(); await page.getByRole('button', { name: 'Refresh operations' }).click();
  const card = page.getByRole('article', { name: 'AAA1 weekdays' }); await card.getByRole('button', { name: 'Edit shift' }).click();
  await page.getByLabel('Shift name', { exact: true }).fill('Retained name'); await page.getByRole('button', { name: 'Save shift' }).click();
  await expect(page.getByRole('main').getByRole('alert')).toContainText('save failed'); await expect(page.getByLabel('Shift name', { exact: true })).toHaveValue('Retained name');
  await page.getByRole('button', { name: 'Save shift' }).click(); const retained = page.getByRole('article', { name: 'Retained name' });
  await retained.getByRole('button', { name: 'Employees' }).click(); await retained.getByLabel('Employee', { exact: true }).selectOption(cleaner);
  await retained.getByRole('button', { name: 'Assign employee' }).click(); await expect(page.getByRole('main').getByRole('alert')).toContainText('assign failed');
  await retained.getByRole('button', { name: 'Assign employee' }).click(); await expect(retained.getByText('Test Cleaner', { exact: true })).toBeVisible();
  await retained.getByRole('button', { name: 'Deactivate shift' }).click(); await expect(page.getByRole('main').getByRole('alert')).toContainText('deactivate failed');
  await retained.getByRole('button', { name: 'Deactivate shift' }).click(); await expect(retained).toHaveCount(0);
});
test('manager overlap is visible, selection survives and inactive employees are omitted', async ({ page }) => {
  await setup(page); let conflict = true;
  await page.route(/\/manager\/shifts\/[^/]+\/assignments$/, route => {
    if (route.request().method() === 'POST' && conflict) {
      conflict = false; return route.fulfill({ status: 409, headers, json: { detail: 'Employee is already assigned to an overlapping shift.' } });
    }
    return route.fallback();
  });
  await page.goto('/manager/'); const card = page.getByRole('article', { name: 'AAA1 weekdays' });
  await card.getByRole('button', { name: 'Employees' }).click();
  await expect(card.getByRole('option', { name: 'Inactive Cleaner' })).toHaveCount(0);
  await card.getByLabel('Employee', { exact: true }).selectOption(cleaner);
  await card.getByRole('button', { name: 'Assign employee' }).click();
  await expect(page.getByRole('main').getByRole('alert')).toContainText('Employee is already assigned to an overlapping shift.');
  await expect(card.getByLabel('Employee', { exact: true })).toHaveValue(cleaner);
  await card.getByRole('button', { name: 'Assign employee' }).click();
  await expect(card.getByRole('button', { name: 'Remove assignment' })).toBeVisible();
});

test('manager account links to operations and sign out removes operational access', async ({ page }) => {
  await setup(page); await page.goto('/account/?tenant=readysetsiivous'); await page.getByRole('link', { name: 'Manage tenant operations' }).click();
  await expect(page.getByRole('button', { name: 'Add shift' })).toBeVisible();
  await page.getByRole('link', { name: 'Account', exact: true }).click(); await expect(page).toHaveURL(/account\/\?tenant=readysetsiivous/);
  await page.getByRole('link', { name: 'Manage tenant operations' }).click();
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-10-17', toLocalDate: '2026-10-18' } }));
  await page.getByRole('link', { name: 'My shifts', exact: true }).click(); await expect(page.getByText('No shifts scheduled.')).toBeVisible();
  await page.goto('/manager/'); await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Add shift' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await expect(page.getByRole('button', { name: 'Add shift' })).toBeVisible();
});
test('manager creates each schedule type with explicit fields', async ({ page }) => {
  const state = await setup(page); await page.goto('/manager/');
  for (const type of [1, 2, 3, 4, 5]) {
    await page.getByRole('button', { name: 'Add shift' }).click(); await page.getByLabel('Shift name', { exact: true }).fill(`Schedule ${type}`);
    await page.getByLabel('Company', { exact: true }).selectOption(company); await page.getByLabel('Branch', { exact: true }).selectOption(branch1);
    await page.getByLabel('Schedule', { exact: true }).selectOption(String(type));
    if (type === 1) await page.getByLabel('Monday start', { exact: true }).fill('09:00');
    if (type === 2) await page.getByRole('checkbox', { name: 'Tuesday' }).check();
    if (type === 3) { await page.getByLabel('Week parity').selectOption('2'); await page.getByLabel('Day', { exact: true }).selectOption('2'); }
    if (type === 5) await page.getByLabel('Day of month').fill('15');
    await page.getByRole('button', { name: 'Save shift' }).click(); await expect(page.getByRole('article', { name: `Schedule ${type}` })).toBeVisible();
  }
  expect(state.writes.map(w => w.schedule.type)).toEqual([1, 2, 3, 4, 5]);
  expect(state.writes[0].schedule.dailyTimes.Monday.start).toBe('09:00'); expect(state.writes[1].schedule.weeklyDays).toContain(2);
  expect(state.writes[2].schedule.biWeeklyWeekParity).toBe(2); expect(state.writes[4].schedule.monthlyDay).toBe(15);
});

test('manager deletion removes inactive work and its count after refresh while active work remains', async ({ page }) => {
  const state = await setup(page, { inactiveShift: true });
  let accept = false; const messages: string[] = [];
  page.on('dialog', dialog => { messages.push(dialog.message()); return accept ? dialog.accept() : dialog.dismiss(); });
  await page.goto('/manager/');
  await page.getByRole('tab', { name: 'Inactive (1)', exact: true }).click();
  const inactive = page.getByRole('article', { name: 'AAA2 biweekly' });
  await inactive.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(inactive).toBeVisible();
  accept = true;
  await inactive.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(inactive).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Inactive (0)', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Refresh operations' }).click();
  await expect(inactive).toHaveCount(0);
  await page.getByRole('tab', { name: 'Active (1)', exact: true }).click();
  await expect(page.getByRole('article', { name: 'AAA1 weekdays' })).toBeVisible();
  expect(messages.at(-1)).toContain('Completed checklist records, photos and reports will remain in history.');
  expect(state.adminRequests).toBe(0);
});

test('manager requires a start date and supports optional end dates', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-17T22:00:00Z') });
  const state = await setup(page); await page.goto('/manager/');
  await page.getByRole('button', { name: 'Add shift' }).click();
  await expect(page.getByLabel('Start date', { exact: true })).toHaveValue('2026-10-18');
  await page.getByLabel('Start date', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Save shift' }).click();
  expect(await page.getByLabel('Start date', { exact: true }).evaluate((input: HTMLInputElement) => input.validity.valueMissing)).toBe(true);
  expect(state.writes).toHaveLength(0);
  await page.getByLabel('Start date', { exact: true }).fill('2026-11-01');
  await page.getByLabel('Shift name', { exact: true }).fill('Dated shift');
  await page.getByLabel('Company', { exact: true }).selectOption(company);
  await page.getByLabel('Branch', { exact: true }).selectOption(branch1);
  await page.getByRole('button', { name: 'Save shift' }).click();
  await expect(page.getByRole('article', { name: 'Dated shift', exact: true })).toBeVisible();
  expect(state.writes[0].validFrom).toBe('2026-11-01'); expect(state.writes[0].validUntil).toBeNull();
  const card = page.getByRole('article', { name: 'Dated shift', exact: true });
  await card.getByRole('button', { name: 'Edit shift' }).click();
  await expect(page.getByLabel('Start date', { exact: true })).toHaveValue('2026-11-01');
  const end = page.getByLabel('End date (optional)', { exact: true });
  await expect(end).toHaveAttribute('min', '2026-11-01');
  await end.fill('2026-10-31');
  await page.getByRole('button', { name: 'Save shift' }).click();
  expect(state.writes).toHaveLength(1);
  expect(await end.evaluate((input: HTMLInputElement) => input.validity.rangeUnderflow)).toBe(true);
  await end.fill('2026-11-30');
  await page.getByRole('button', { name: 'Save shift' }).click();
  await expect(page.getByLabel('Start date', { exact: true })).toHaveCount(0);
  expect(state.writes[1].validUntil).toBe('2026-11-30');
  await card.getByRole('button', { name: 'Edit shift' }).click();
  await expect(page.getByLabel('Start date', { exact: true })).toHaveValue('2026-11-01');
  await expect(end).toHaveValue('2026-11-30');
  await end.fill('');
  await page.getByRole('button', { name: 'Save shift' }).click();
  await expect(page.getByLabel('Start date', { exact: true })).toHaveCount(0);
  expect(state.writes[2].validUntil).toBeNull();
});

for (const width of [1280, 390]) test(`manager lifecycle tabs filter locally and clear assignment selections at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 844 });
  const state = await setup(page, { inactiveShift: true }); await page.goto('/manager/');
  const active = page.getByRole('tab', { name: 'Active (1)', exact: true });
  const inactive = page.getByRole('tab', { name: 'Inactive (1)', exact: true });
  await expect(active).toHaveAttribute('aria-selected', 'true');
  const first = page.getByRole('article', { name: 'AAA1 weekdays' });
  await expect(first).toBeVisible(); await expect(page.getByRole('article', { name: 'AAA2 biweekly' })).toHaveCount(0);
  await first.getByRole('button', { name: 'Employees' }).click();
  await first.getByLabel('Employee', { exact: true }).selectOption(cleaner);
  const requests = state.requests; const loads = state.shiftLoads;
  await active.focus(); await active.press('ArrowRight');
  await expect(inactive).toBeFocused(); await expect(inactive).toHaveAttribute('aria-selected', 'true');
  const second = page.getByRole('article', { name: 'AAA2 biweekly' });
  await expect(second).toBeVisible(); await expect(second.getByRole('button', { name: 'Employees' })).toHaveCount(0);
  await inactive.press('Home'); await expect(first).toBeVisible();
  await expect(first.getByLabel('Employee', { exact: true })).toHaveCount(0);
  expect(state.requests).toBe(requests); expect(state.shiftLoads).toBe(loads);
  await first.getByRole('button', { name: 'Employees' }).click();
  await expect(first.getByLabel('Employee', { exact: true })).toHaveValue('');
  expect(state.adminRequests).toBe(0);
});

test('late manager assignment response cannot reopen or populate a hidden lifecycle selection', async ({ page }) => {
  await setup(page, { inactiveShift: true });
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  let started = false; let delayed = true;
  await page.route(/\/manager\/shifts\/[^/]+\/assignments$/, async route => {
    if (route.request().method() !== 'GET' || !delayed) return route.fallback();
    delayed = false; started = true; await gate;
    return route.fulfill({ status: 200, headers, json: [{ id: 'late', employeeId: cleaner, isActive: true }] });
  });
  await page.goto('/manager/'); const card = page.getByRole('article', { name: 'AAA1 weekdays' });
  await card.getByRole('button', { name: 'Employees' }).click(); await expect.poll(() => started).toBe(true);
  await page.getByRole('tab', { name: /^Inactive/ }).click(); await page.getByRole('tab', { name: /^Active/ }).click();
  release(); await expect(card.getByLabel('Employee', { exact: true })).toHaveCount(0);
  await card.getByRole('button', { name: 'Employees' }).click();
  await expect(card.getByText('No employees assigned.')).toBeVisible();
  await expect(card.getByRole('button', { name: 'Remove assignment' })).toHaveCount(0);
});
