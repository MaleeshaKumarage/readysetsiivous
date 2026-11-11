import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const company = { id: 'company', name: 'AAA', businessId: '1234567-8', isActive: true, isPartnerEnabled: false, billingEmail: null, billingStreet: null, billingPostalCode: null, billingCity: null, billingCountry: null, invoiceNumberPrefix: null, paymentTermDays: 14 };

test('admin adds a property with its size and cleaning type, and both are saved on the property', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = []; let branches: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'POST' && path.endsWith('/branches')) {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      const saved = { id: 'branch', companyId: 'company', isActive: true, ...body }; branches = [saved]; return route.fulfill({ headers, json: saved });
    }
    const json = path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    // Anything this test does not model goes to the shared admin mocks.
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });

  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Add branch' });
  await modal.getByRole('textbox', { name: 'Name', exact: true }).fill('Imatra apartment');
  await modal.getByRole('textbox', { name: 'City', exact: true }).fill('Imatra');
  await modal.getByLabel('Size in m² (required to schedule)').fill('45');
  await modal.getByLabel('Cleaning type (required to schedule)').selectOption('Deep').catch(async () => { await modal.getByRole('textbox', { name: 'Cleaning type (required to schedule)' }).click(); await page.getByRole('option', { name: 'Deep', exact: true }).click(); });
  await modal.getByRole('button', { name: 'Add branch', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ name: 'Imatra apartment', sizeM2: 45, cleaningType: 'Deep' });
});

test('admin changes a property size and the new size is saved on the property', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = [];
  const existing = { id: 'branch', companyId: 'company', name: 'Imatra apartment', street: null, postalCode: null, city: 'Imatra', country: null, contactPhone: null, contactEmail: null, isActive: true, sizeM2: 45, cleaningType: 'Deep', roomCount: null, bedCount: null, priceNet: null, vatRatePercent: null, estimatedDurationMinutes: null, pricingReady: false, priceVersions: [] };
  let branches: Record<string, unknown>[] = [existing];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'PUT' && path.endsWith('/branches/branch')) {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      branches = [{ ...existing, ...body }]; return route.fulfill({ headers, json: branches[0] });
    }
    const json = path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    // Anything this test does not model goes to the shared admin mocks.
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });

  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  // The company's own Edit button comes first; the branch's Edit button is the last one.
  await page.getByRole('group', { name: 'Branch Imatra apartment' }).getByRole('button', { name: 'Edit', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Edit branch' });
  await expect(modal.getByLabel('Size in m² (required to schedule)')).toHaveValue('45');
  await modal.getByLabel('Size in m² (required to schedule)').fill('60');
  await modal.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ sizeM2: 60, cleaningType: 'Deep' });
});

test('admin adds a property without a size and no size is sent', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = []; let branches: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'POST' && path.endsWith('/branches')) {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      const saved = { id: 'branch', companyId: 'company', isActive: true, ...body }; branches = [saved]; return route.fulfill({ headers, json: saved });
    }
    const json = path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });

  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Add branch' });
  await modal.getByRole('textbox', { name: /^Name/ }).fill('Oulu flat');
  await modal.getByRole('button', { name: 'Add branch', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0].sizeM2 ?? null).toBeNull();
});

test('admin clears a property size and the cleared size is saved as empty', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = [];
  const existing = { id: 'branch', companyId: 'company', name: 'Imatra apartment', street: null, postalCode: null, city: 'Imatra', country: null, contactPhone: null, contactEmail: null, isActive: true, sizeM2: 45, roomCount: null, bedCount: null, priceNet: null, vatRatePercent: null, estimatedDurationMinutes: null, pricingReady: false, priceVersions: [] };
  let branches: Record<string, unknown>[] = [existing];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'PUT' && path.endsWith('/branches/branch')) {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      branches = [{ ...existing, ...body }]; return route.fulfill({ headers, json: branches[0] });
    }
    const json = path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });

  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  await page.getByRole('group', { name: 'Branch Imatra apartment' }).getByRole('button', { name: 'Edit', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Edit branch' });
  await modal.getByLabel('Size in m² (required to schedule)').fill('');
  await modal.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect(modal).toHaveCount(0);
  expect(writes).toHaveLength(1);
  expect(writes[0].sizeM2 ?? null).toBeNull();
});

test('a saved size survives a reload and is shown again in the branch form', async ({ page }) => {
  await setupAdminMocks(page);
  let branches: Record<string, unknown>[] = [{ id: 'branch', companyId: 'company', name: 'Imatra apartment', city: 'Imatra', isActive: true, sizeM2: 45, cleaningType: 'Normal', priceVersions: [] }];
  await page.route(/\/api\/v1\/admin\//, route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    const json = path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });
  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  await page.getByRole('group', { name: 'Branch Imatra apartment' }).getByRole('button', { name: 'Edit', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Edit branch' });
  await expect(modal.getByLabel('Size in m² (required to schedule)')).toHaveValue('45');
  await expect(modal.getByRole('textbox', { name: 'Cleaning type (required to schedule)' })).toHaveValue('Normal');
  await modal.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(modal).toHaveCount(0);
});

test('the cleaning type can be chosen and each saved choice is sent to the server', async ({ page }) => {
  await setupAdminMocks(page);
  const writes: Record<string, unknown>[] = [];
  let branches: Record<string, unknown>[] = [{ id: 'branch', companyId: 'company', name: 'Imatra apartment', city: 'Imatra', isActive: true, priceVersions: [] }];
  await page.route(/\/api\/v1\/admin\//, route => {
    const method = route.request().method(); const path = new URL(route.request().url()).pathname;
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'PUT' && path.endsWith('/branches/branch')) {
      const body = route.request().postDataJSON() as Record<string, unknown>; writes.push(body);
      branches = [{ ...branches[0], ...body }]; return route.fulfill({ headers, json: branches[0] });
    }
    const json = path.endsWith('/companies/company') ? { company, branches }
      : path.endsWith('/companies') ? { items: [company], total: 1 }
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : undefined;
    return json === undefined ? route.fallback() : route.fulfill({ headers, json });
  });
  await page.goto('/admin/companies/');
  await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).first().click();
  const openEdit = async () => { await page.getByRole('group', { name: 'Branch Imatra apartment' }).getByRole('button', { name: 'Edit', exact: true }).click(); return page.getByRole('dialog', { name: 'Edit branch' }); };
  const cleaning = (modal: import('@playwright/test').Locator) => modal.getByRole('textbox', { name: 'Cleaning type (required to schedule)' });

  let modal = await openEdit();
  await cleaning(modal).click(); await page.getByRole('option', { name: 'Deep', exact: true }).click();
  await modal.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(modal).toHaveCount(0);
  expect(writes[0]).toMatchObject({ cleaningType: 'Deep' });

  modal = await openEdit();
  await expect(cleaning(modal)).toHaveValue('Deep');
  await cleaning(modal).click(); await page.getByRole('option', { name: 'Normal', exact: true }).click();
  await modal.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(modal).toHaveCount(0);
  expect(writes[1]).toMatchObject({ cleaningType: 'Normal' });

  modal = await openEdit();
});
