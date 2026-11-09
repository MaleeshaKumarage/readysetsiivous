import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function employee(): any {
  return {
    id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', phone: '',
    role: 'employee', isActive: true, skills: [], serviceAreas: [], colorHex: '', payRate: 18.5, notes: '',
    certifications: [], defaultHours: {}, keycloakUserId: 'user-a',
  };
}

type Profile = { employeeId: string; rates: any[]; legacy: { amount: number | null; confirmed: boolean } };

function rate(id: string, hourlyRate: number, effectiveFrom: string, source = 'manual') {
  return { id, hourlyRate, effectiveFrom, source, createdUtc: '2026-11-09T00:00:00Z' };
}

async function mockPayProfile(page: any, initial: Profile, options: { postStatus?: number; postDetail?: string; failLoads?: number } = {}) {
  const state = { profile: initial, posts: [] as any[], legacyPosts: [] as any[] };
  let failLoads = options.failLoads ?? 0;
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, (route: any) => route.fulfill({ status: 200, headers, json: [employee()] }));
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/pay-profile$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (failLoads > 0) { failLoads -= 1; return route.fulfill({ status: 500, headers, json: { detail: 'Temporarily unavailable' } }); }
    return route.fulfill({ status: 200, headers, json: state.profile });
  });
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/pay-rates$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = route.request().postDataJSON();
    state.posts.push(body);
    if (options.postStatus) return route.fulfill({ status: options.postStatus, headers, json: { detail: options.postDetail } });
    state.profile = { ...state.profile, rates: [rate(`r-${body.effectiveFrom}`, body.hourlyRate, body.effectiveFrom), ...state.profile.rates] };
    return route.fulfill({ status: 200, headers, json: state.profile });
  });
  await page.route(/\/api\/v1\/admin\/employees\/employee-a\/pay-profile\/legacy-rate$/, (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const body = route.request().postDataJSON();
    state.legacyPosts.push(body);
    const amount = state.profile.legacy.amount as number;
    state.profile = {
      ...state.profile,
      rates: [rate(`legacy-${body.effectiveFrom}`, amount, body.effectiveFrom, 'legacy-confirmed'), ...state.profile.rates],
      legacy: { amount, confirmed: true },
    };
    return route.fulfill({ status: 200, headers, json: state.profile });
  });
  return state;
}

async function openPayProfile(page: any) {
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByTestId('pay-profile')).toBeVisible();
}

test('admin adds a dated rate and sees it in the history', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const state = await mockPayProfile(page, { employeeId: 'employee-a', rates: [rate('r1', 18, '2026-01-01')], legacy: { amount: 18.5, confirmed: true } });
  await openPayProfile(page);

  const history = page.getByRole('list', { name: 'Pay rate history' });
  await expect(history.getByText('From 2026-01-01')).toBeVisible();
  await expect(history.getByText('€18.00 / h')).toBeVisible();

  await page.getByLabel('New hourly rate').fill('21.25');
  await page.getByLabel('Effective from').fill('2027-01-01');
  await page.getByRole('button', { name: 'Add rate', exact: true }).click();

  await expect(history.getByText('From 2027-01-01')).toBeVisible();
  await expect(history.getByText('€21.25 / h')).toBeVisible();
  await expect(page.getByTestId('pay-profile').getByRole('status')).toHaveText('Pay rate added.');
  await expect(page.getByLabel('New hourly rate')).toHaveValue('');
  expect(state.posts).toEqual([{ hourlyRate: 21.25, effectiveFrom: '2027-01-01' }]);
});

test('a legacy rate without a start date is confirmed with the chosen date', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const state = await mockPayProfile(page, { employeeId: 'employee-a', rates: [], legacy: { amount: 18.5, confirmed: false } });
  await openPayProfile(page);

  await expect(page.getByText('has no start date')).toBeVisible();
  await page.getByLabel('Legacy rate starts').fill('2026-01-01');
  await page.getByRole('button', { name: 'Confirm legacy rate', exact: true }).click();

  await expect(page.getByText('has no start date')).toHaveCount(0);
  await expect(page.getByRole('list', { name: 'Pay rate history' }).getByText('Confirmed legacy rate')).toBeVisible();
  expect(state.legacyPosts).toEqual([{ effectiveFrom: '2026-01-01' }]);
});

test('invalid rates and a missing start date are blocked before any request', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const state = await mockPayProfile(page, { employeeId: 'employee-a', rates: [], legacy: { amount: null, confirmed: false } });
  await openPayProfile(page);

  await page.getByLabel('New hourly rate').fill('-1');
  await page.getByLabel('Effective from').fill('2027-01-01');
  await page.getByRole('button', { name: 'Add rate', exact: true }).click();
  await expect(page.getByTestId('pay-profile').getByRole('alert')).toHaveText('Hourly rate cannot be negative. Enter 0 or a positive amount.');

  await page.getByLabel('New hourly rate').fill('18');
  await page.getByLabel('Effective from').fill('');
  await page.getByRole('button', { name: 'Add rate', exact: true }).click();
  await expect(page.getByTestId('pay-profile').getByRole('alert')).toHaveText('Choose the date this rate starts.');

  expect(state.posts).toEqual([]);
});

test('a refused duplicate date shows the server reason and adds nothing', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const state = await mockPayProfile(page, { employeeId: 'employee-a', rates: [rate('r1', 18, '2026-01-01')], legacy: { amount: null, confirmed: false } },
    { postStatus: 409, postDetail: 'A pay rate already starts on this date. Choose another date.' });
  await openPayProfile(page);

  await page.getByLabel('New hourly rate').fill('21');
  await page.getByLabel('Effective from').fill('2026-01-01');
  await page.getByRole('button', { name: 'Add rate', exact: true }).click();

  await expect(page.getByTestId('pay-profile').getByRole('alert')).toHaveText('A pay rate already starts on this date. Choose another date.');
  await expect(page.getByRole('list', { name: 'Pay rate history' }).getByText('From 2026-01-01')).toHaveCount(1);
  expect(state.posts).toHaveLength(1);
});

test('a failed profile load offers a retry that recovers', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockPayProfile(page, { employeeId: 'employee-a', rates: [rate('r1', 18, '2026-01-01')], legacy: { amount: null, confirmed: false } }, { failLoads: 1 });
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();

  await expect(page.getByTestId('pay-profile').getByRole('alert')).toHaveText('Pay profile could not be loaded. Reload and retry.');
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Pay rate history' }).getByText('From 2026-01-01')).toBeVisible();
});

test('an employee with no dated rate is told visits are exceptions, not zero pay', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await mockPayProfile(page, { employeeId: 'employee-a', rates: [], legacy: { amount: null, confirmed: false } });
  await openPayProfile(page);

  await expect(page.getByText('Visits without a rate are shown as exceptions and are never paid as zero.')).toBeVisible();
  await expect(page.getByText('has no start date')).toHaveCount(0);
});
