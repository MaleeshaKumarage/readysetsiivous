import { test, expect, type Page } from '@playwright/test';
import { CORS, PartnerFlowBackend, prepareRolePage } from './partner-flow-backend';

function gate() {
  let release!: () => void;
  const promise = new Promise<void>(resolve => { release = resolve; });
  return { promise, release };
}
async function owner(page: Page) {
  const backend = new PartnerFlowBackend({ priced: true });
  await prepareRolePage(page, backend, 'owner');
  return backend;
}
async function requestForm(page: Page) {
  await page.goto('/partner/');
  await page.getByRole('button', { name: 'Request cleaning', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('From date', { exact: true }).fill('2026-12-01');
  await dialog.getByLabel('To date', { exact: true }).fill('2026-12-02');
  return dialog;
}

test('a pending cleaning submission disables saving and sends only one POST', async ({ page }) => {
  const backend = await owner(page);
  const pending = gate();
  let calls = 0;
  await page.route(/\/api\/v1\/partner\/requests$/, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    calls++;
    await pending.promise;
    return route.fallback();
  });
  const dialog = await requestForm(page);
  const save = dialog.getByRole('button', { name: 'Save cleaning request', exact: true });
  try {
    await save.click();
    await expect.poll(() => calls).toBe(1);
    await expect(save).toBeDisabled();
    // A second submit event must be guarded as well as the disabled button.
    await dialog.locator('form').evaluate((form: HTMLFormElement) => form.requestSubmit());
    expect(calls).toBe(1);
  } finally { pending.release(); }
  await expect(dialog).toHaveCount(0);
  expect(backend.writes.filter(write => write.path === 'partner/requests')).toHaveLength(1);
  expect(backend.requests).toHaveLength(1);
});

test('closing a failed request and starting a new draft generates a new idempotency key', async ({ page }) => {
  await owner(page);
  const bodies: any[] = [];
  await page.route(/\/api\/v1\/partner\/requests$/, route => {
    if (route.request().method() !== 'POST') return route.fallback();
    bodies.push(route.request().postDataJSON());
    return route.fulfill({ headers: CORS, status: 503, json: { detail: 'Synthetic save unavailable.' } });
  });
  const dialog = await requestForm(page);
  await dialog.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('Synthetic save unavailable.');
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Request cleaning', exact: true }).click();
  await expect(dialog.getByLabel('From date', { exact: true })).toHaveValue('');
  await dialog.getByLabel('From date', { exact: true }).fill('2026-12-03');
  await dialog.getByLabel('To date', { exact: true }).fill('2026-12-04');
  await dialog.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('Synthetic save unavailable.');
  expect(bodies).toHaveLength(2);
  expect(bodies[0].clientRequestId).toMatch(/^[a-f0-9-]{36}$/);
  expect(bodies[1].clientRequestId).not.toBe(bodies[0].clientRequestId);
});

for (const timing of ['closed', 'replaced'] as const) {
  test(`a delayed completed-visit response cannot overwrite a ${timing} dialog`, async ({ page }) => {
    const backend = await owner(page);
    const second = backend.addBranch({ name: 'Harbour studio', companyId: backend.branches[0].companyId, priceNet: 90, estimatedDurationMinutes: 90 });
    const visits = [backend.branches[0], second].map((branch, index) => {
      const request = backend.seedAccepted(backend.seedRequest(branch, { fromDate: `2026-12-0${index + 1}`, toDate: `2026-12-0${index + 1}` }), 'cleaner', `2026-12-0${index + 1}T10:00`);
      backend.completeVisit(request, { tasks: [], cleanerNotes: index ? 'Second visit evidence' : 'First visit evidence' });
      return request;
    });
    const pending = gate();
    let received = false;
    await page.route(new RegExp(`/partner/requests/${visits[0].id}/completion$`), async route => {
      if (route.request().method() === 'OPTIONS') return route.fallback();
      received = true;
      await pending.promise;
      return route.fallback();
    });
    await page.goto('/partner/');
    await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
    await page.getByRole('tab', { name: /^Completed requests/ }).click();
    await page.getByRole('article', { name: `Lake apartment ${visits[0].fromDate}`, exact: true }).getByRole('button', { name: 'View visit' }).click();
    const dialog = page.getByRole('dialog', { name: 'Completed visit', exact: true });
    const lateResponse = page.waitForResponse(response => response.url().endsWith(`/requests/${visits[0].id}/completion`) && response.request().method() === 'GET');
    try {
      await expect.poll(() => received).toBe(true);
      await expect(dialog.getByRole('status')).toContainText('Loading');
      await page.keyboard.press('Escape');
      if (timing === 'replaced') {
        await page.getByRole('article', { name: `Harbour studio ${visits[1].fromDate}`, exact: true }).getByRole('button', { name: 'View visit' }).click();
        await expect(dialog).toContainText('Second visit evidence');
      }
    } finally { pending.release(); }
    await (await lateResponse).finished();
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    if (timing === 'closed') await expect(dialog).toHaveCount(0);
    else {
      await expect(dialog).toContainText('Second visit evidence');
      await expect(dialog).not.toContainText('First visit evidence');
    }
    expect(backend.writes).toEqual([]);
  });
}

test('failed completed-visit read recovers by reopening without mutating the visit', async ({ page }) => {
  const backend = await owner(page);
  const visit = backend.seedAccepted(backend.seedRequest(backend.branches[0], { fromDate: '2026-12-01', toDate: '2026-12-01' }), 'cleaner', '2026-12-01T10:00');
  backend.completeVisit(visit, { tasks: [], cleanerNotes: 'Retained completed evidence' });
  backend.failNext('GET', /completion$/, 503, 'Synthetic visit read unavailable.');
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('tab', { name: /^Completed requests/ }).click();
  const open = page.getByRole('button', { name: 'View visit', exact: true });
  await open.click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('alert')).toContainText('Synthetic visit read unavailable.');
  await page.keyboard.press('Escape');
  await open.click();
  await expect(dialog).toContainText('Retained completed evidence');
  await expect(dialog.getByRole('alert')).toHaveCount(0);
  expect(backend.writes).toEqual([]);
});

for (const failure of ['read', 'write'] as const) {
  test(`partner language remains usable when localStorage ${failure} throws`, async ({ page }) => {
    await owner(page);
    await page.addInitScript(failure => {
      const method = failure === 'read' ? 'getItem' : 'setItem';
      const original = Storage.prototype[method];
      Storage.prototype[method] = function (...args: any[]) {
        if (args[0] === 'readyset:partner-language') throw new DOMException('Storage blocked', 'SecurityError');
        return (original as any).apply(this, args);
      };
    }, failure);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/partner/');
    await page.getByRole('button', { name: 'Suomi', exact: true }).click();
    await expect(page.locator('main.partner-portal')).toHaveAttribute('lang', 'fi');
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'Properties', exact: true })).toBeVisible();
    expect(errors).toEqual([]);
  });
}
