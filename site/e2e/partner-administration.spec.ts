import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
const initialCompany = { id: 'company-owner', name: 'Property owner', businessId: '1234567-8', isActive: true, isPartnerEnabled: false };

test('company partner access persists through create and edit', async ({ page }) => {
  await setupAdminMocks(page);
  let company: any = null; const writes: any[] = [];
  await page.route(/\/api\/v1\/admin\/companies(?:\/[^/?]+)?(?:\?.*)?$/, route => {
    const req = route.request();
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: { items: company ? [company] : [], total: company ? 1 : 0 } });
    const body = req.postDataJSON(); writes.push({ method: req.method(), body });
    company = { ...initialCompany, ...body }; return route.fulfill({ headers, json: company });
  });
  await page.goto('/admin/companies/'); await page.getByRole('button', { name: 'Add company', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByLabel('Enable partner access')).not.toBeChecked();
  await dialog.getByLabel('Business ID').fill(initialCompany.businessId); await dialog.getByLabel(/^Name/).fill(initialCompany.name);
  await dialog.getByLabel('Enable partner access').check(); await dialog.getByRole('button', { name: 'Create company' }).click();
  await expect(dialog).not.toBeVisible(); expect(writes[0]).toMatchObject({ method: 'POST', body: { isPartnerEnabled: true } });
  await page.getByRole('row').filter({ has: page.getByRole('cell', { name: initialCompany.name, exact: true }) }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(dialog.getByLabel('Enable partner access')).toBeChecked();
  await dialog.getByLabel('Enable partner access').uncheck(); await dialog.getByRole('button', { name: 'Save changes' }).click();
  await expect(dialog).not.toBeVisible(); expect(writes[1]).toMatchObject({ method: 'PUT', body: { id: initialCompany.id, isPartnerEnabled: false } });
  await page.getByRole('button', { name: 'Edit', exact: true }).click(); await expect(dialog.getByLabel('Enable partner access')).not.toBeChecked();
});

test('branch property counts and fixed pricing round trip through create and edit', async ({ page }) => {
  await setupAdminMocks(page); const writes: any[] = []; let branch: any = null;
  await page.route(/\/api\/v1\/admin\/companies(?:\/[^?]+)?(?:\?.*)?$/, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: path.endsWith(initialCompany.id) ? { company: initialCompany, branches: branch ? [branch] : [] } : { items: [initialCompany], total: 1 } });
    const body = req.postDataJSON(); writes.push({ path, body });
    branch = { ...branch, id: 'branch-apartment', companyId: initialCompany.id, isActive: true, pricingReady: true, ...body };
    return route.fulfill({ headers, json: branch });
  });
  await page.goto('/admin/companies/'); await page.getByRole('button', { name: 'Branches', exact: true }).click();
  await page.getByRole('button', { name: 'Add branch', exact: true }).click(); const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Name/).fill('City apartment');
  for (const [label, value] of [['Rooms (optional)', '2'], ['Beds (optional)', '3'], ['Fixed cleaning price, excl. VAT (€)', '85.5'], ['VAT (%)', '25.5'], ['Estimated cleaning duration (minutes)', '90']]) await dialog.getByLabel(label, { exact: true }).fill(value);
  await dialog.getByRole('button', { name: 'Add branch', exact: true }).click(); await expect(dialog).not.toBeVisible();
  expect(writes[0]).toMatchObject({ path: `/api/v1/admin/companies/${initialCompany.id}/branches`, body: { roomCount: 2, bedCount: 3, priceNet: 85.5, vatRatePercent: 25.5, estimatedDurationMinutes: 90 } });
  await expect(page.getByText('€85.50 excl. VAT', { exact: false })).toBeVisible(); await expect(page.getByText('Partner booking ready')).toBeVisible();
  await page.getByRole('button', { name: 'Edit', exact: true }).last().click();
  for (const [label, value] of [['Rooms (optional)', '2'], ['Beds (optional)', '3'], ['Fixed cleaning price, excl. VAT (€)', '85.5'], ['VAT (%)', '25.5'], ['Estimated cleaning duration (minutes)', '90']]) await expect(dialog.getByLabel(label, { exact: true })).toHaveValue(value);
  for (const [label, value] of [['Rooms (optional)', '4'], ['Beds (optional)', '5'], ['Fixed cleaning price, excl. VAT (€)', '120'], ['VAT (%)', '14'], ['Estimated cleaning duration (minutes)', '150']]) await dialog.getByLabel(label, { exact: true }).fill(value);
  await dialog.getByRole('button', { name: 'Save changes' }).click(); await expect(dialog).not.toBeVisible();
  expect(writes[1]).toMatchObject({ path: '/api/v1/admin/companies/branches/branch-apartment', body: { roomCount: 4, bedCount: 5, priceNet: 120, vatRatePercent: 14, estimatedDurationMinutes: 150 } });
  await expect(page.getByText('€120.00 excl. VAT', { exact: false })).toBeVisible();
});

test('partner employee requires one active partner-enabled company and can be rebound through edit', async ({ page }) => {
  await setupAdminMocks(page); const writes: any[] = []; const employees: any[] = [];
  const companies = [{ ...initialCompany, isPartnerEnabled: true }, { ...initialCompany, id: 'second-owner', name: 'Second property owner', isPartnerEnabled: true }, { ...initialCompany, id: 'disabled-partner', name: 'Partner disabled', isPartnerEnabled: false }, { ...initialCompany, id: 'inactive-partner', name: 'Inactive partner', isActive: false, isPartnerEnabled: true }];
  await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/, route => route.fulfill({ headers, json: { items: companies, total: companies.length } }));
  await page.route(/\/api\/v1\/admin\/employees(?:\/new-partner)?(?:\?.*)?$/, route => {
    const req = route.request(); if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: employees });
    const body = req.postDataJSON(); writes.push(body);
    if (req.method() === 'PUT') { employees[0] = { ...employees[0], ...body.fields, isActive: body.isActive }; return route.fulfill({ status: 204, headers }); }
    employees.push({ id: 'new-partner', ...body.fields, isActive: true }); return route.fulfill({ headers, json: 'new-partner' });
  });
  await page.goto('/admin/employees/'); await page.getByRole('button', { name: 'Add employee', exact: true }).click(); const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/^Email/).fill('owner@example.invalid'); await dialog.getByLabel('First name').fill('Property'); await dialog.getByLabel('Last name').fill('Owner');
  await expect(dialog.getByLabel(/^Partner company/)).toHaveCount(0);
  await dialog.getByLabel('Role', { exact: true }).click(); await page.getByRole('option', { name: 'partner', exact: true }).click();
  await expect(dialog.getByLabel(/^Partner company/)).toHaveAttribute('required', '');
  await dialog.getByRole('button', { name: 'Create employee' }).click(); await expect(dialog.getByRole('alert')).toContainText('Select an active company with partner access enabled'); expect(writes).toHaveLength(0);
  await dialog.getByLabel(/^Partner company/).click();
  await expect(page.getByRole('option', { name: initialCompany.name, exact: true })).toBeVisible(); await expect(page.getByRole('option', { name: 'Partner disabled', exact: true })).toHaveCount(0); await expect(page.getByRole('option', { name: 'Inactive partner', exact: true })).toHaveCount(0);
  await page.getByRole('option', { name: initialCompany.name, exact: true }).click(); await dialog.getByRole('button', { name: 'Create employee' }).click();
  await expect(dialog).not.toBeVisible(); expect(writes).toHaveLength(1); expect(writes[0].fields).toMatchObject({ role: 'partner', partnerCompanyId: initialCompany.id });
  await expect(page.getByRole('row').filter({ has: page.getByRole('gridcell', { name: 'Property Owner', exact: true }) })).toContainText(initialCompany.name);
  const row = page.getByRole('row').filter({ has: page.getByRole('gridcell', { name: 'Property Owner', exact: true }) });
  await row.getByRole('button', { name: 'Edit', exact: true }).click(); await expect(dialog.getByLabel(/^Partner company/)).toHaveValue(initialCompany.name);
  await dialog.getByLabel(/^Partner company/).click(); await page.getByRole('option', { name: 'Second property owner', exact: true }).click(); await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).not.toBeVisible(); expect(writes).toHaveLength(2); expect(writes[1]).toMatchObject({ fields: { role: 'partner', partnerCompanyId: 'second-owner' }, isActive: true });
  await expect(row).toContainText('Second property owner'); await row.getByRole('button', { name: 'Edit', exact: true }).click(); await expect(dialog.getByLabel(/^Partner company/)).toHaveValue('Second property owner');
  await dialog.getByLabel('Role', { exact: true }).click(); await page.getByRole('option', { name: 'employee', exact: true }).click(); await expect(dialog.getByLabel(/^Partner company/)).toHaveCount(0);
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click(); await expect(dialog).not.toBeVisible(); expect(writes).toHaveLength(3); expect(writes[2]).toMatchObject({ fields: { role: 'employee', partnerCompanyId: null }, isActive: true });
  await expect(row).not.toContainText('Second property owner'); await row.getByRole('button', { name: 'Edit', exact: true }).click(); await expect(dialog.getByLabel('Role', { exact: true })).toHaveValue('employee'); await expect(dialog.getByLabel(/^Partner company/)).toHaveCount(0);
});

test('quality cycle option requires a template and persists through create and edit', async ({ page }) => {
  await setupAdminMocks(page);
  const template = { id: 'template-monthly', title: 'Monthly checklist', description: null, items: ['Corridor'], companyId: null, branchId: null, isActive: true, createdUtc: '2026-11-01T00:00:00Z', updatedUtc: '2026-11-01T00:00:00Z' };
  let company: any = null; const writes: any[] = [];
  const propertyTemplate = { ...template, id: 'template-property', title: 'Property only checklist', branchId: 'branch-apartment' };
  await page.route(/\/api\/v1\/admin\/quality-cycle\/templates(?:\?.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    return route.fulfill({ headers, json: [template, propertyTemplate] });
  });
  await page.route(/\/api\/v1\/admin\/companies(?:\/[^/?]+)?(?:\?.*)?$/, route => {
    const req = route.request();
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') return route.fulfill({ headers, json: { items: company ? [company] : [], total: company ? 1 : 0 } });
    const body = req.postDataJSON(); writes.push({ method: req.method(), body });
    company = { ...initialCompany, ...body }; return route.fulfill({ headers, json: company });
  });
  await page.goto('/admin/companies/'); await page.getByRole('button', { name: 'Add company', exact: true }).click();
  const dialog = page.getByRole('dialog');
  // Quality cycle is offered only for partner companies, so the checkbox is absent until partner access is on.
  await expect(dialog.getByLabel('Quality cycle', { exact: true })).toHaveCount(0);
  await expect(dialog.getByLabel('Quality cycle template')).toHaveCount(0);
  await dialog.getByLabel('Business ID').fill(initialCompany.businessId); await dialog.getByLabel(/^Name/).fill(initialCompany.name);
  await dialog.getByLabel('Enable partner access').check();
  await expect(dialog.getByLabel('Quality cycle', { exact: true })).not.toBeChecked();
  await dialog.getByLabel('Quality cycle', { exact: true }).check();
  await expect(dialog.getByLabel('Quality cycle template')).toBeVisible();
  // Turning partner access off hides the quality cycle choice and clears it.
  await dialog.getByLabel('Enable partner access').uncheck();
  await expect(dialog.getByLabel('Quality cycle', { exact: true })).toHaveCount(0);
  await expect(dialog.getByLabel('Quality cycle template')).toHaveCount(0);
  await dialog.getByLabel('Enable partner access').check();
  await expect(dialog.getByLabel('Quality cycle', { exact: true })).not.toBeChecked();
  await dialog.getByLabel('Quality cycle', { exact: true }).check();
  await expect(dialog.getByLabel('Quality cycle template')).toBeVisible();
  await dialog.getByRole('button', { name: 'Create company' }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Choose a quality cycle template.');
  expect(writes).toHaveLength(0);
  await dialog.getByLabel('Quality cycle template').click(); await expect(page.getByRole('option', { name: 'Property only checklist' })).toHaveCount(0);
  await page.getByRole('option', { name: 'Monthly checklist' }).click();
  await dialog.getByRole('button', { name: 'Create company' }).click();
  await expect(dialog).not.toBeVisible();
  expect(writes[0]).toMatchObject({ method: 'POST', body: { isPartnerEnabled: true, isQualityCycleEnabled: true, qualityCycleTemplateId: template.id } });
  await page.getByRole('row').filter({ has: page.getByRole('cell', { name: initialCompany.name, exact: true }) }).getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(dialog.getByLabel('Quality cycle', { exact: true })).toBeChecked();
  await expect(dialog.getByLabel('Quality cycle template')).toHaveValue('Monthly checklist');
  await dialog.getByLabel('Quality cycle', { exact: true }).uncheck(); await dialog.getByRole('button', { name: 'Save changes' }).click();
  await expect(dialog).not.toBeVisible();
  expect(writes[1]).toMatchObject({ method: 'PUT', body: { id: initialCompany.id, isQualityCycleEnabled: false, qualityCycleTemplateId: null } });
});
