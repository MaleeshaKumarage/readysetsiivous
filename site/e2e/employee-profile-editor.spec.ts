import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };

function baseEmployee(): any {
  return {
    id: 'employee-a', email: 'cleaner@example.invalid', firstName: 'Test', lastName: 'Cleaner', phone: '',
    role: 'employee', isActive: true, skills: ['Windows'], serviceAreas: ['Helsinki'], colorHex: '#123456',
    payRate: 18.5, notes: 'Keys in the lockbox', certifications: [{ name: 'Hygiene', expiresAtUtc: '2027-03-01T00:00:00Z' }],
    defaultHours: {}, keycloakUserId: 'user-a',
  };
}

async function mockEmployee(page: any, employee: any) {
  const puts: any[] = [];
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, (route: any) => route.fulfill({ status: 200, headers, json: [employee] }));
  await page.route(/\/api\/v1\/admin\/employees\/employee-a$/, async (route: any) => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON();
      puts.push(body);
      Object.assign(employee, body.fields, { certifications: body.fields.certifications.map((c: any) => ({ name: c.name, expiresAtUtc: c.expiresAtUtc })) });
      return route.fulfill({ status: 200, headers, json: employee });
    }
    return route.fulfill({ status: 405, headers, json: { detail: 'Unexpected method' } });
  });
  return puts;
}

test('admin edits advanced profile fields and they persist after reload', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  const puts = await mockEmployee(page, employee);
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Notes' })).toHaveValue('Keys in the lockbox');
  await expect(page.getByLabel('Pay rate')).toHaveValue('18.5');
  await expect(page.getByLabel('Certification 1 name')).toHaveValue('Hygiene');
  await expect(page.getByLabel('Certification 1 expiry')).toHaveValue('2027-03-01');

  await page.getByLabel('Pay rate').fill('21');
  await page.getByRole('textbox', { name: 'Notes' }).fill('Gate code 4411');
  await page.getByLabel('Certification 1 name').fill('First aid');
  await page.getByLabel('Certification 1 expiry').fill('2028-01-15');
  await page.getByRole('button', { name: 'Add certification', exact: true }).click();
  await page.getByLabel('Certification 2 name').fill('Forklift');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect.poll(() => puts.length).toBe(1);
  expect(puts[0].fields).toMatchObject({ payRate: 21, notes: 'Gate code 4411', colorHex: '#123456', skills: ['Windows'], serviceAreas: ['Helsinki'] });
  expect(puts[0].fields.certifications).toEqual([
    { name: 'First aid', expiresAtUtc: '2028-01-15T00:00:00Z' },
    { name: 'Forklift', expiresAtUtc: null },
  ]);

  await page.reload();
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByLabel('Pay rate')).toHaveValue('21');
  await expect(page.getByRole('textbox', { name: 'Notes' })).toHaveValue('Gate code 4411');
  await expect(page.getByLabel('Certification 2 name')).toHaveValue('Forklift');
});

test('admin adds a skill and service area, removes a certification, and sees them after reload', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  const puts = await mockEmployee(page, employee);
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();

  await page.getByRole('textbox', { name: 'Skills', exact: true }).fill('Forklift');
  await page.getByRole('textbox', { name: 'Skills', exact: true }).press('Enter');
  await page.getByRole('textbox', { name: 'Service areas', exact: true }).fill('Espoo');
  await page.getByRole('textbox', { name: 'Service areas', exact: true }).press('Enter');
  await page.getByRole('button', { name: 'Remove', exact: true }).click();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect.poll(() => puts.length).toBe(1);
  expect(puts[0].fields.skills).toEqual(['Windows', 'Forklift']);
  expect(puts[0].fields.serviceAreas).toEqual(['Helsinki', 'Espoo']);
  expect(puts[0].fields.certifications).toEqual([]);

  await page.reload();
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByText('Forklift', { exact: true })).toBeVisible();
  await expect(page.getByText('Espoo', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Certification 1 name')).toHaveCount(0);
});

test('admin removes a skill and service area with the tag remove buttons and the change persists', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  const puts = await mockEmployee(page, employee);
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();

  await page.locator('.mantine-Pill-root', { hasText: 'Windows' }).locator('.mantine-Pill-remove').click();
  await page.locator('.mantine-Pill-root', { hasText: 'Helsinki' }).locator('.mantine-Pill-remove').click();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect.poll(() => puts.length).toBe(1);
  expect(puts[0].fields.skills).toEqual([]);
  expect(puts[0].fields.serviceAreas).toEqual([]);

  await page.reload();
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByText('Windows', { exact: true })).toHaveCount(0);
  await expect(page.getByText('Helsinki', { exact: true })).toHaveCount(0);
});

test('admin changes colour to a new valid value and it persists after reload', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  const puts = await mockEmployee(page, employee);
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await page.getByLabel('Colour', { exact: true }).fill('#00ff00');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect.poll(() => puts.length).toBe(1);
  expect(puts[0].fields.colorHex).toBe('#00ff00');

  await page.reload();
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByLabel('Colour', { exact: true })).toHaveValue('#00ff00');
});

test('add employee after cancelled edit does not carry the previous profile into the new employee', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  await mockEmployee(page, employee);
  const posts: any[] = [];
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => {
    if (route.request().method() !== 'POST') return route.fallback();
    posts.push(route.request().postDataJSON());
    return route.fulfill({ status: 200, headers, json: { id: 'employee-new' } });
  });
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByLabel('Pay rate')).toHaveValue('18.5');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();

  await page.getByRole('button', { name: 'Add employee', exact: true }).click();
  await page.getByLabel(/^Email/).fill('new@example.invalid');
  await page.getByLabel(/^First name/).fill('New');
  await page.getByLabel(/^Last name/).fill('Person');
  await page.getByRole('button', { name: 'Create employee', exact: true }).click();

  await expect.poll(() => posts.length).toBe(1);
  expect(posts[0].fields).toMatchObject({ email: 'new@example.invalid', skills: [], serviceAreas: [], certifications: [], notes: null, payRate: null, colorHex: null });
});

test('invalid pay rate, colour and blank certification are blocked before saving', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  const puts = await mockEmployee(page, employee);
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();

  await page.getByLabel('Pay rate').fill('-5');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Pay rate must be zero or more.');

  await page.getByLabel('Pay rate').fill('18');
  await page.getByLabel('Colour').fill('red');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Colour must be a 6-digit hex value');

  await page.getByLabel('Colour').fill('#abcdef');
  await page.getByRole('button', { name: 'Add certification', exact: true }).click();
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.locator('p[role="alert"]')).toContainText('Each certification needs a name');

  await page.waitForTimeout(300);
  expect(puts).toHaveLength(0);
});

test('admin can cancel an edit without sending changes', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const employee = baseEmployee();
  const puts = await mockEmployee(page, employee);
  await page.goto('/admin/employees/');
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await page.getByLabel('Pay rate').fill('99');
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(page.getByLabel('Pay rate')).toHaveValue('18.5');
  expect(puts).toHaveLength(0);
});

test('manager is blocked from the employee profile editor', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['manager']; });
  await mockEmployee(page, baseEmployee());
  await page.goto('/admin/employees/');
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Pay rate')).toHaveCount(0);
});
