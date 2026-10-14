import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const people = [
  { id: 'alice', firstName: 'Alice', lastName: 'Cleaner', isActive: true },
  { id: 'bob', firstName: 'Bob', lastName: 'Cleaner', isActive: true },
  { id: 'inactive', firstName: 'Inactive', lastName: 'Cleaner', isActive: false },
];
type Failure = 'conflict' | 'server' | 'html' | 'network' | 'saved-network';
async function setup(page: Page, options: { failure?: Failure; partial?: boolean; assigned?: boolean; loadFailure?: boolean; removeFailure?: boolean; refreshFailure?: boolean } = {}) {
  await setupAdminMocks(page);
  let assigned: any[] = options.assigned ? [{ id: 'existing', employeeId: 'alice', isActive: true }] : [];
  let failure = options.failure; let loadFailure = options.loadFailure; let removeFailure = options.removeFailure;
  let refreshFailure = options.refreshFailure;
  let reads = 0; const writes: string[] = [];
  await page.route(/\/api\/v1\/admin\//, async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path.includes('/assignments')) {
      if (method === 'GET') {
        reads++;
        if (loadFailure || (refreshFailure && writes.length)) { loadFailure = false; refreshFailure = false; return route.fulfill({ status: 503, headers, body: 'unavailable' }); }
        return route.fulfill({ status: 200, headers, json: assigned });
      }
      if (method === 'DELETE') {
        if (removeFailure) { removeFailure = false; return route.fulfill({ status: 403, headers, json: { detail: 'You do not have permission to remove this assignment.' } }); }
        assigned = assigned.filter(a => !path.endsWith('/' + a.employeeId));
        return route.fulfill({ status: 204, headers });
      }
      const id = request.postDataJSON().employeeId; writes.push(id);
      const fail = options.partial && id === 'bob' ? 'conflict' : failure;
      failure = undefined;
      if (fail && fail !== 'saved-network') {
        if (fail === 'network') return route.abort('failed');
        return route.fulfill({ status: fail === 'conflict' ? 409 : 500, headers,
          ...(fail === 'html' ? { contentType: 'text/html', body: '<h1>Proxy failure</h1>' }
            : { json: { detail: fail === 'conflict' ? 'Employee is already assigned to an overlapping shift.' : 'Internal diagnostic that should not be displayed' } }) });
      }
      assigned.push({ id: 'assignment-' + id, employeeId: id, isActive: true });
      if (fail === 'saved-network') return route.abort('failed');
      return route.fulfill({ status: 200, headers, json: assigned.at(-1) });
    }
    const json = path.endsWith('/shifts') ? ['one', 'two'].map(id => ({ id, name: 'Shift ' + id, schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, isActive: true }))
      : path.endsWith('/employees') ? people : path.endsWith('/companies') ? { items: [], total: 0 } : [];
    return route.fulfill({ status: 200, headers, json });
  });
  await page.goto('/admin/shifts/');
  await page.getByRole('button', { name: 'Employees', exact: true }).first().click();
  return { writes, get reads() { return reads; } };
}
async function select(page: Page, name: string) {
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).click();
  await page.getByRole('option', { name, exact: true }).click();
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).press('Escape');
}
const alert = (page: Page) => page.getByRole('alert', { name: 'Assignment error' });

test('overlap error names employee and keeps selection for a successful retry', async ({ page }) => {
  const state = await setup(page, { failure: 'conflict' });
  await select(page, 'Alice Cleaner');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(alert(page)).toHaveText('Alice Cleaner: Employee is already assigned to an overlapping shift.');
  await expect(page.getByRole('button', { name: 'Assign', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Assigned 1 employee.');
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner' })).toBeVisible();
  await expect(alert(page)).toHaveCount(0); expect(state.writes).toEqual(['alice', 'alice']);
});

for (const failure of ['server', 'html', 'network'] as const) test(`${failure} failure is visible and retry retains employee`, async ({ page }) => {
  const state = await setup(page, { failure }); await select(page, 'Alice Cleaner');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(alert(page)).toContainText(failure === 'network' ? 'Could not confirm the operation.' : 'Could not assign employee. Please retry.');
  await expect(alert(page)).not.toContainText('Internal diagnostic');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner' })).toBeVisible();
  expect(state.writes).toEqual(['alice', 'alice']);
});

test('partial success retains only failed employee and reports both outcomes', async ({ page }) => {
  const state = await setup(page, { partial: true }); await select(page, 'Alice Cleaner'); await select(page, 'Bob Cleaner');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Assigned 1 employee. 1 assignment could not be confirmed.');
  await expect(alert(page)).toContainText('Bob Cleaner: Employee is already assigned to an overlapping shift.');
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner' })).toBeVisible();
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect.poll(() => state.writes.length).toBe(3); expect(state.writes).toEqual(['alice', 'bob', 'bob']);
});

test('inactive and already assigned employees are omitted; removed employee becomes selectable', async ({ page }) => {
  const state = await setup(page, { assigned: true });
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).click();
  await expect(page.getByRole('option', { name: 'Bob Cleaner' })).toBeVisible();
  await expect(page.getByRole('option', { name: 'Alice Cleaner' })).toHaveCount(0);
  await expect(page.getByRole('option', { name: 'Inactive Cleaner' })).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Assign employees', exact: true }).press('Escape');
  await page.getByRole('button', { name: 'Unassign Alice Cleaner' }).click();
  await expect(page.getByText('No employees assigned.')).toBeVisible();
  await select(page, 'Alice Cleaner');
  await page.getByRole('button', { name: 'Employees', exact: true }).first().click();
  await page.getByRole('button', { name: 'Employees', exact: true }).first().click();
  await expect(page.getByText('No employees assigned.')).toBeVisible(); expect(state.reads).toBe(3);
});

test('failed roster load is not presented as an empty roster and can be refreshed', async ({ page }) => {
  await setup(page, { loadFailure: true }); await expect(alert(page)).toContainText('Could not load assignments.');
  await expect(page.getByText('No employees assigned.')).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Refresh assignments' }).click();
  await expect(page.getByText('No employees assigned.')).toBeVisible(); await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeEnabled();
});

test('failed removal keeps the roster and explains the error', async ({ page }) => {
  await setup(page, { assigned: true, removeFailure: true });
  await page.getByRole('button', { name: 'Unassign Alice Cleaner' }).click();
  await expect(alert(page)).toContainText('Alice Cleaner: You do not have permission to remove this assignment.');
  await page.getByRole('button', { name: 'Unassign Alice Cleaner' }).click(); await expect(page.getByText('No employees assigned.')).toBeVisible();
});

test('lost response after server saves is reconciled without retrying the saved employee', async ({ page }) => {
  const state = await setup(page, { failure: 'saved-network' }); await select(page, 'Alice Cleaner');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(alert(page)).toContainText('Could not confirm the operation.');
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Assign', exact: true })).toBeDisabled(); expect(state.writes).toEqual(['alice']);
});

test('successful assignment with failed refresh blocks further changes until refresh', async ({ page }) => {
  await setup(page, { refreshFailure: true }); await select(page, 'Alice Cleaner');
  await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(alert(page)).toContainText('Could not refresh assignments.');
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Refresh assignments' }).click();
  await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner' })).toBeVisible();
});

test('pending assignment locks mutations and row navigation', async ({ page }) => {
  const state = await setup(page); let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  await page.route(/\/shifts\/one\/assignments$/, async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await held; return route.fallback();
  });
  await select(page, 'Alice Cleaner'); await page.getByRole('button', { name: 'Assign', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeDisabled();
  for (const name of ['Employees', 'Deactivate']) {
    for (const button of await page.getByRole('button', { name, exact: true }).all()) await expect(button).toBeDisabled();
  }
  await expect(page.getByRole('button', { name: 'Refresh assignments' })).toBeDisabled();
  release(); await expect(page.getByRole('button', { name: 'Unassign Alice Cleaner' })).toBeVisible();
  expect(state.writes).toEqual(['alice']);
});

test('late failed roster read does not overwrite another opened row', async ({ page }) => {
  await setup(page); let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  await page.getByRole('button', { name: 'Employees', exact: true }).first().click();
  await page.route(/\/shifts\/one\/assignments$/, async route => {
    if (route.request().method() !== 'GET') return route.fallback();
    await held; return route.fulfill({ status: 500, headers, body: 'old failure' });
  });
  await page.getByRole('button', { name: 'Employees', exact: true }).first().click();
  await expect(page.getByText('Loading assignments…')).toBeVisible();
  await page.getByRole('button', { name: 'Employees', exact: true }).nth(1).click();
  await expect(page.getByText('No employees assigned.')).toBeVisible();
  const response = page.waitForResponse(r => r.url().endsWith('/shifts/one/assignments') && r.status() === 500);
  release(); await response;
  await expect(alert(page)).toHaveCount(0); await expect(page.getByRole('textbox', { name: 'Assign employees', exact: true })).toBeEnabled();
});
