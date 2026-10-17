import { test, expect, Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const shiftId = '11111111-1111-4111-8111-111111111111';
const formId = '22222222-2222-4222-8222-222222222222';
const evidencePhoto = { name: 'corridor.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64') };

async function setupWork(page: Page, options: { empty?: boolean; failStart?: boolean; failEnd?: boolean; lostStart?: boolean; lostEnd?: boolean; loadFailure?: boolean; future?: boolean; startAt?: string; role?: 'employee' | 'admin' | 'manager' } = {}) {
  await setupAdminMocks(page);
  const role = options.role ?? 'employee';
  await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = [role]; }, role);
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ status: 200, headers,
    json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: [role] })],
      employee: { id: 'employee-a', firstName: 'Test', lastName: 'Staff', role, isActive: true } } }));
  const start = new Date(Date.now() - 3600000).toISOString();
  const scheduledStart = options.startAt ? Date.parse(options.startAt) : options.future ? Date.now() + 86400000 : Date.parse(start);
  const occurrence: any = { shiftId, shiftName: 'AAA1 weekday shift', startUtc: new Date(scheduledStart).toISOString(),
    endUtc: new Date(scheduledStart + (options.startAt ? 8 * 3600000 : options.future ? 3600000 : 2 * 3600000)).toISOString(), timeZoneId: 'Europe/Helsinki', form: null };
  let startCalls = 0; let endCalls = 0; let loadCalls = 0; let submitted: any = null;
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    loadCalls++;
    return route.fulfill(options.loadFailure && loadCalls === 1 ? { status: 403, headers } : { status: 200, headers, json: route.request().url().includes('/calendar') ? { occurrences: [occurrence], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-10-17', toLocalDate: '2026-10-18' } : [occurrence] });
  });
  await page.route(new RegExp(`/api/v1/me/shifts/${shiftId}/start$`), route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    startCalls++;
    if (options.failStart && startCalls === 1) return route.fulfill({ status: 503, headers });
    occurrence.form = { id: formId, shiftId, employeeId: 'employee-a', templateId: options.empty ? '00000000-0000-0000-0000-000000000000' : formId,
      templateTitle: 'Daily activities', items: options.empty ? [] : [{ itemText: 'Corridor', isChecked: false }, { itemText: 'Gym', isChecked: false }],
      photoUrls: [], cleanerNotes: '', isSubmitted: false, startedAtUtc: new Date().toISOString(), submittedUtc: null };
    return route.fulfill(options.lostStart ? { status: 503, headers } : { status: 200, headers, json: occurrence.form });
  });
  await page.route(new RegExp(`/api/v1/me/quality-cycle/${formId}/end$`), route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    endCalls++;
    if (options.failEnd && endCalls === 1) return route.fulfill({ status: 503, headers });
    submitted = route.request().postDataJSON();
    occurrence.form = { ...occurrence.form, ...submitted, isSubmitted: true, submittedUtc: new Date().toISOString(), endedAtUtc: new Date().toISOString() };
    return route.fulfill(options.lostEnd ? { status: 503, headers } : { status: 200, headers, json: occurrence.form });
  });
  return { occurrence, get loadCalls() { return loadCalls; }, get submitted() { return submitted; }, get startCalls() { return startCalls; }, get endCalls() { return endCalls; } };
}

for (const selected of ['none', 'subset', 'all']) {
  test(`employee starts shift and ends with ${selected} activities and no required notes`, async ({ page }) => {
    const work = await setupWork(page);
    await page.goto('/me/shifts/');
    await page.getByRole('button', { name: 'Start', exact: true }).click();
    await expect(page.getByText(/^Actual start: /)).toBeVisible();
    await page.getByRole('button', { name: 'Record activities & end', exact: true }).click();
    if (selected !== 'none') await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
    if (selected === 'all') await page.getByRole('checkbox', { name: 'Gym', exact: true }).check();
    await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked({ checked: selected !== 'none' });
    await page.getByRole('button', { name: 'End shift', exact: true }).click();
    await expect(page.getByText('Done', { exact: true })).toBeVisible();
    await expect(page.getByText(/^Actual end: /)).toBeVisible();
    expect(work.submitted.items).toEqual([{ itemText: 'Corridor', isChecked: selected !== 'none' }, { itemText: 'Gym', isChecked: selected === 'all' }]);
    expect(work.submitted.cleanerNotes).toBe(''); expect(work.endCalls).toBe(1);
  });
}

test('activity selections and notes survive calendar tab changes', async ({ page }) => {
  await setupWork(page); await page.goto('/me/shifts/');
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await page.getByRole('textbox', { name: 'Shift note' }).fill('Keep my work');
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  await page.getByRole('tab', { name: 'Today', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked();
  await expect(page.getByRole('textbox', { name: 'Shift note' })).toHaveValue('Keep my work');
});

test('tenant midnight reloads Today and preserves an open activity draft', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-17T20:59:00Z') });
  await page.clock.pauseAt(new Date('2026-10-17T20:59:10Z'));
  const work = await setupWork(page, { startAt: '2026-10-17T20:00:00Z' }); await page.goto('/me/shifts/');
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await page.getByRole('textbox', { name: 'Shift note' }).fill('Midnight work');
  const before = work.loadCalls;
  await page.clock.fastForward(60000);
  await expect.poll(() => work.loadCalls).toBeGreaterThan(before);
  await expect(page.getByRole('tab', { name: 'Today', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('textbox', { name: 'Shift note' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked();
  await expect(page.getByRole('textbox', { name: 'Shift note' })).toHaveValue('Midnight work');
});

test('changing calendar days preserves activity selections, shift notes and photo notes', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-17T12:00:00Z') });
  const work = await setupWork(page); await page.goto('/me/shifts/');
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.route(/\/api\/v1\/me\/shifts\/calendar\?/, route => route.fulfill({ headers, json: {
    occurrences: [
      { ...work.occurrence, startUtc: '2026-10-16T08:00:00Z', endUtc: '2026-10-16T10:00:00Z' },
      { ...work.occurrence, shiftId: 'other', shiftName: 'Other day', startUtc: '2026-10-15T08:00:00Z', endUtc: '2026-10-15T10:00:00Z' },
    ], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-09-16', toLocalDate: '2026-10-17',
  } }));
  await page.getByRole('tab', { name: 'Past', exact: true }).click();
  await page.getByRole('button', { name: /Oct 16.*1 shift/ }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await page.getByRole('textbox', { name: 'Shift note' }).fill('Preserve day draft');
  await page.getByLabel('Add shift photos').setInputFiles(evidencePhoto);
  await page.getByRole('textbox', { name: 'Photo 1 note' }).fill('Corridor evidence');
  await page.getByRole('button', { name: /Oct 15.*1 shift/ }).click();
  await expect(page.getByRole('heading', { name: 'Other day' })).toBeVisible();
  await page.getByRole('button', { name: /Oct 16.*1 shift/ }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked();
  await expect(page.getByRole('textbox', { name: 'Shift note' })).toHaveValue('Preserve day draft');
  await expect(page.getByRole('textbox', { name: 'Photo 1 note' })).toHaveValue('Corridor evidence');
  await expect(page.getByRole('button', { name: 'Remove photo' })).toHaveCount(1);
});

test('employee can start and end a shift without an assigned checklist', async ({ page }) => {
  const work = await setupWork(page, { empty: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await expect(page.getByText('No checklist assigned. You can end this shift without recording activities.')).toBeVisible();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible(); expect(work.submitted.items).toEqual([]);
});

test('photos require their own note even with a general note and retry keeps evidence', async ({ page }) => {
  const work = await setupWork(page, { failEnd: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await page.getByLabel('Add shift photos').setInputFiles(evidencePhoto);
  await expect(page.getByRole('button', { name: 'Remove photo' })).toHaveCount(1);
  await page.getByRole('textbox', { name: 'Shift note' }).fill('General shift note');
  const note = page.getByRole('textbox', { name: 'Photo 1 note' });
  await expect(note).toHaveAttribute('aria-required', 'true');
  await note.fill('  \n  ');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByRole('alert', { name: 'Shift error' })).toContainText('Add a separate note for each photo.');
  await expect(note).toBeFocused(); expect(work.endCalls).toBe(0);
  await note.fill('Corridor cleaned; entrance stain removed.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByRole('alert', { name: 'Shift error' })).toContainText('Could not end shift.');
  await expect(note).toHaveValue('Corridor cleaned; entrance stain removed.');
  await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked();
  await expect(page.getByRole('button', { name: 'Remove photo' })).toHaveCount(1);
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible();
  expect(work.submitted.photos).toHaveLength(1); expect(work.submitted.photos[0].url).toMatch(/^data:image\/png;base64,/);
  expect(work.submitted.photos[0].note).toBe('Corridor cleaned; entrance stain removed.');
  expect(work.submitted.cleanerNotes).toBe('General shift note');
});

test('removing the last photo removes the note requirement and notes alone remain valid', async ({ page }) => {
  const work = await setupWork(page);
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByLabel('Add shift photos').setInputFiles(evidencePhoto);
  await page.getByRole('button', { name: 'Remove photo' }).click();
  const note = page.getByRole('textbox', { name: 'Shift note' });
  await expect(note).toHaveAttribute('aria-required', 'false');
  await note.fill('Gym was closed today.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible();
  expect(work.submitted.photos).toEqual([]); expect(work.submitted.cleanerNotes).toBe('Gym was closed today.');
});

test('each photo needs its own note and removing one preserves the other note', async ({ page }) => {
  const work = await setupWork(page); await page.goto('/me/shifts/');
  await page.getByRole('button', { name: 'Start', exact: true }).click(); await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByLabel('Add shift photos').setInputFiles([evidencePhoto, { ...evidencePhoto, name: 'gym.png' }]);
  await page.getByRole('textbox', { name: 'Photo 1 note' }).fill('Corridor cleaned');
  await page.getByRole('textbox', { name: 'Shift note' }).fill('General shift note');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Photo 2 note' })).toBeFocused(); expect(work.endCalls).toBe(0);
  await page.getByRole('textbox', { name: 'Photo 2 note' }).fill('Gym floor cleaned');
  await page.getByRole('button', { name: 'Remove photo' }).first().click();
  await expect(page.getByRole('textbox', { name: 'Photo 1 note' })).toHaveValue('Gym floor cleaned');
  await expect(page.getByRole('textbox', { name: 'Photo 2 note' })).toHaveCount(0);
  await page.getByRole('button', { name: 'End shift', exact: true }).click(); await expect(page.getByText('Done', { exact: true })).toBeVisible();
  expect(work.submitted.photos.map((p: any) => p.note)).toEqual(['Gym floor cleaned']);
});

test('shift cannot end while a selected photo is still being read', async ({ page }) => {
  await page.addInitScript(() => {
    const Reader = window.FileReader;
    window.FileReader = class extends Reader { readAsDataURL(blob: Blob) { setTimeout(() => super.readAsDataURL(blob), 1200); } };
  });
  const work = await setupWork(page);
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('textbox', { name: 'Shift note' }).fill('Photo evidence of corridor cleaning.');
  await page.getByLabel('Add shift photos').setInputFiles(evidencePhoto);
  await expect(page.getByRole('status')).toHaveText('Reading photos...');
  await expect(page.getByRole('button', { name: 'End shift', exact: true })).toBeDisabled(); expect(work.endCalls).toBe(0);
  await expect(page.getByRole('button', { name: 'Remove photo' })).toHaveCount(1);
  await page.getByRole('textbox', { name: 'Photo 1 note' }).fill('Corridor photo');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible(); expect(work.submitted.photos).toHaveLength(1);
});

test('failed photo reads and unsupported files are visible and do not stop note-only completion', async ({ page }) => {
  await page.addInitScript(() => {
    const Reader = window.FileReader;
    window.FileReader = class extends Reader { readAsDataURL() { queueMicrotask(() => this.dispatchEvent(new Event('error'))); } };
  });
  const work = await setupWork(page);
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByLabel('Add shift photos').setInputFiles(evidencePhoto);
  await expect(page.getByRole('alert', { name: 'Shift error' })).toHaveText('Could not read the photo. Please select it again.');
  await page.getByLabel('Add shift photos').setInputFiles({ name: 'document.txt', mimeType: 'text/plain', buffer: Buffer.from('not a photo') });
  await expect(page.getByRole('alert', { name: 'Shift error' })).toHaveText('Choose a PNG, JPEG, WebP or BMP photo.');
  await page.getByRole('textbox', { name: 'Shift note' }).fill('No usable photo today.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible(); expect(work.submitted.photos).toEqual([]);
});

test('failed start and end retries retain selected activities and notes', async ({ page }) => {
  const work = await setupWork(page, { failStart: true, failEnd: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText('Could not start shift. Please retry.')).toBeVisible();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await page.getByPlaceholder('Notes (optional)').fill('Gym was not done today.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Could not end shift. Your selections are kept; please retry.')).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Corridor', exact: true })).toBeChecked();
  await expect(page.getByPlaceholder('Notes (optional)')).toHaveValue('Gym was not done today.');
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible(); expect(work.startCalls).toBe(2); expect(work.endCalls).toBe(2);
});

test('lost start and end responses recover the already saved shift state', async ({ page }) => {
  const work = await setupWork(page, { lostStart: true, lostEnd: true });
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('checkbox', { name: 'Gym', exact: true }).check();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible();
  expect(work.startCalls).toBe(1); expect(work.endCalls).toBe(1); expect(work.submitted.items[1].isChecked).toBe(true);
});

test('failed work read is an error, not an empty schedule, and can be retried', async ({ page }) => {
  await setupWork(page, { loadFailure: true }); await page.goto('/me/shifts/');
  await expect(page.getByText('Failed to load your shifts.')).toBeVisible();
  await expect(page.getByText('No shifts scheduled.')).toHaveCount(0);
  await page.getByRole('button', { name: 'Retry loading shifts' }).click();
  await expect(page.getByRole('heading', { name: 'AAA1 weekday shift' })).toBeVisible();
});

test('anonymous employee must sign in before loading shifts', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: false }); let requests = 0;
  page.on('request', request => { if (request.url().includes('/api/v1/me/shifts')) requests++; });
  await page.goto('/me/shifts/'); await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible(); expect(requests).toBe(0);
});

test('a future shift becomes startable on its scheduled local date', async ({ page }) => {
  const now = Date.now(); await page.clock.install({ time: new Date(now) });
  const work = await setupWork(page, { future: true }); await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeDisabled();
  await expect(page.getByText('Available on the scheduled date. The actual start time will be recorded.')).toBeVisible(); expect(work.startCalls).toBe(0);
  await page.clock.fastForward(86405000);
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Record activities & end' })).toBeVisible(); expect(work.startCalls).toBe(1);
});

test('a shift can be started before its scheduled time on the scheduled date', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-16T06:00:00Z') });
  const work = await setupWork(page, { startAt: '2026-10-16T18:00:00Z' }); await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
  await expect(page.getByText(/Scheduled:.*Oct 16, 2026/)).toBeVisible();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText(/^Actual start: /)).toBeVisible(); expect(work.startCalls).toBe(1);
});

test('a shift can be started after its scheduled time on the scheduled date', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-16T20:00:00Z') });
  const work = await setupWork(page, { startAt: '2026-10-16T18:00:00Z' }); await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText(/^Actual start: /)).toBeVisible(); expect(work.startCalls).toBe(1);
});

test('clock start displays the actual API rejection reason', async ({ page }) => {
  await setupWork(page);
  await page.route(new RegExp(`/api/v1/me/shifts/${shiftId}/start$`), route => route.fulfill({ status: 409, headers, json: { detail: 'Choose an actual scheduled shift occurrence.' } }));
  await page.goto('/me/shifts/'); await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText('Could not start shift. Choose an actual scheduled shift occurrence.')).toBeVisible();
});

for (const role of ['admin', 'manager'] as const) {
  test(`${role} reaches own shifts from management, records work and returns to management`, async ({ page }) => {
    const work = await setupWork(page, { role });
    await page.route(/\/api\/v1\/(admin|manager)\//, route => route.fulfill({ status: 200, headers,
      json: route.request().url().endsWith('/companies') ? { items: [], total: 0 } : [] }));
    await page.goto(role === 'admin' ? '/admin/employees/' : '/manager/');
    await page.getByRole('link', { name: 'My shifts', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'My Shifts', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'AAA1 weekday shift' })).toBeVisible();
    await page.getByRole('button', { name: 'Start', exact: true }).click();
    await page.getByRole('button', { name: 'Record activities & end', exact: true }).click();
    await page.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
    await page.getByRole('button', { name: 'End shift', exact: true }).click();
    await expect(page.getByText('Done', { exact: true })).toBeVisible();
    expect(work.startCalls).toBe(1); expect(work.endCalls).toBe(1);
    expect(work.submitted.items).toEqual([{ itemText: 'Corridor', isChecked: true }, { itemText: 'Gym', isChecked: false }]);
    expect(work.submitted.employeeId).toBeUndefined();
    await page.getByRole('link', { name: 'My account', exact: true }).click();
    await expect(page).toHaveURL(/account\/\?tenant=readysetsiivous/);
    await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();
    await page.getByRole('link', { name: role === 'admin' ? 'Continue to administration' : 'Manage tenant operations' }).click();
    await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toBeVisible();
    if (role === 'admin') {
      await page.getByRole('link', { name: 'My account', exact: true }).click();
      await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible();
    }
  });

  test(`${role} own-shift navigation remains available with no assignments`, async ({ page }) => {
    await setupWork(page, { role });
    await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({ status: 200, headers, json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-10-17', toLocalDate: '2026-10-18' } }));
    await page.goto('/account/?tenant=readysetsiivous');
    await page.getByRole('link', { name: 'My shifts', exact: true }).click();
    await expect(page.getByText('No shifts scheduled.')).toBeVisible();
  });
}

test('a past visit can be started and ended with actual times recorded today', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-10-17T12:00:00Z') });
  const work = await setupWork(page, { startAt: '2026-10-01T08:00:00Z' });
  await page.goto('/me/shifts/');
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByText(/^Actual start: /)).toBeVisible();
  await page.getByRole('button', { name: 'Record activities & end' }).click();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Done', { exact: true })).toBeVisible();
  expect(work.startCalls).toBe(1); expect(work.endCalls).toBe(1);
});
