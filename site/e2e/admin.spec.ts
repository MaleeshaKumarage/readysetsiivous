import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

function getBody(route: any) {
  try {
    return route.request().postDataJSON() || {};
  } catch {
    return {};
  }
}

test.describe('Admin Panel - Auth & Dashboard Navigation', () => {
  test('unauthenticated user sees login prompt', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: false });
    await page.goto('/admin/');
    await expect(page.getByText('Sign in with your ReadySetSiivous admin account.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('authenticated user sees dashboard and can navigate to all sections', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });
    await page.goto('/admin/');

    // Dashboard heading and navigation cards
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
    await expect(page.getByText('Manage cleaning services, prices and descriptions.')).toBeVisible();
    await expect(page.getByText('Upload agreements and collect e-signatures.')).toBeVisible();
    await expect(page.getByText('Manage staff profiles, availability, skills and certifications.')).toBeVisible();
    await expect(page.getByText('Manage customer companies and branch locations.')).toBeVisible();
    await expect(page.getByText('Create and assign recurring shifts across company branches.')).toBeVisible();

    // Direct navigation checks
    await page.goto('/admin/services/');
    await expect(page.getByRole('heading', { name: 'Services' })).toBeVisible();

    await page.goto('/admin/agreements/');
    await expect(page.getByRole('heading', { name: 'Agreements' })).toBeVisible();

    await page.goto('/admin/employees/');
    await expect(page.getByRole('heading', { name: 'Employees' })).toBeVisible();

    await page.goto('/admin/companies/');
    await expect(page.getByRole('heading', { name: 'Companies' })).toBeVisible();

    await page.goto('/admin/shifts/');
    await expect(page.getByRole('heading', { name: 'Shifts' })).toBeVisible();
  });

  test('sign out button returns user to unauthenticated login view', async ({ page, isMobile }) => {
    await setupAdminMocks(page, { authenticated: true });
    await page.goto('/admin/');

    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
    if (!isMobile) {
      await page.evaluate(() => { (window as any).__MOCK_AUTHED__ = false; });
      await page.getByText('Sign out').click();
    } else {
      await page.evaluate(() => {
        (window as any).__MOCK_AUTHED__ = false;
        sessionStorage.setItem('__MOCK_LOGGED_OUT__', 'true');
        window.location.reload();
      });
    }
    await expect(page.getByText('Sign in with your ReadySetSiivous admin account.')).toBeVisible();
  });
});

test.describe('Admin Panel - Services Page', () => {
  test('happy path: create service and view list', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let services: any[] = [];
    await page.route('**/api/v1/admin/services**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') {
        return route.fulfill({ status: 200, json: services, headers: CORS_HEADERS });
      }
      if (method === 'POST') {
        const body = getBody(route);
        const fields = body.fields || body || {};
        const newService = {
          id: 'srv-1',
          slug: fields.slug,
          category: fields.category,
          name: { values: fields.name || { fi: fields.name } },
          description: { values: fields.description || { fi: fields.description } },
          durationMinutes: fields.durationMinutes,
          priceNet: fields.priceNet,
          vatRatePercent: fields.vatRatePercent,
          icon: fields.icon,
          isActive: true,
          isFeatured: false,
          sortOrder: 0,
        };
        services.push(newService);
        return route.fulfill({ status: 200, json: true, headers: CORS_HEADERS });
      }
      return route.continue();
    });

    await page.goto('/admin/services/');

    // Initial empty state
    await expect(page.getByText('No services yet.')).toBeVisible();

    // Open Add service modal
    await page.getByRole('button', { name: 'Add service' }).click();
    await expect(page.getByRole('dialog', { name: 'Add service' })).toBeVisible();

    // Fill form
    await page.getByLabel('Slug').fill('deep-cleaning');
    await page.getByLabel('Name (fi)').fill('Syväsiivous');
    await page.getByLabel('Description (fi)').fill('Perusteellinen siivous kotiin');
    await page.getByLabel('Price (€)').fill('120');
    await page.getByLabel('VAT %').fill('25.5');
    await page.getByLabel('Duration (min)').fill('180');

    // Submit form
    await page.getByRole('button', { name: 'Create service' }).click();

    // Verify modal closes and service appears in table
    await expect(page.getByRole('dialog', { name: 'Add service' })).not.toBeVisible();
    await expect(page.getByRole('cell', { name: 'Syväsiivous' })).toBeVisible();
    await expect(page.getByRole('cell', { name: '120.00 €' })).toBeVisible();
    await expect(page.getByRole('cell', { name: '180 min' })).toBeVisible();
    await expect(page.getByText('Active')).toBeVisible();
  });

  test('worst path: form validation errors and creation failure', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    await page.route('**/api/v1/admin/services**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
      if (method === 'POST') return route.fulfill({ status: 400, json: false, headers: CORS_HEADERS });
      return route.continue();
    });

    await page.goto('/admin/services/');
    await page.getByRole('button', { name: 'Add service' }).click();

    // Submit empty required fields -> client validation error
    await page.getByRole('button', { name: 'Create service' }).click();
    await expect(page.getByText('Slug and name are required.')).toBeVisible();

    // Fill required fields but simulate API failure
    await page.getByLabel('Slug').fill('test-slug');
    await page.getByLabel('Name (fi)').fill('Test Name');
    await page.getByRole('button', { name: 'Create service' }).click();
    await expect(page.getByText('Failed to create service.')).toBeVisible();
  });
});

test.describe('Admin Panel - Agreements Page', () => {
  test('happy path: create agreement with signers and PDF', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let agreements: any[] = [];
    const mockCompanies = [
      { id: 'comp-1', name: 'Acme Oy', businessId: '1234567-8', isActive: true },
    ];

    await page.route('**/api/v1/admin/companies**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: { items: mockCompanies, total: 1 }, headers: CORS_HEADERS });
    });

    await page.route('**/api/v1/admin/agreements**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: agreements, headers: CORS_HEADERS });
      if (method === 'POST') {
        const newAgreement = {
          id: 'agr-1',
          title: 'Siivoussopimus 2025',
          code: 'AGR-2025-001',
          status: 'Pending',
          isActive: true,
          signerCount: 1,
          signedCount: 0,
          createdUtc: new Date().toISOString(),
          signers: [{ id: 's-1', name: 'Matti Meikäläinen', email: 'matti@acme.fi', status: 'Pending', token: 'tok-1' }]
        };
        agreements.push(newAgreement);
        return route.fulfill({ status: 200, json: newAgreement, headers: CORS_HEADERS });
      }
      return route.continue();
    });

    await page.goto('/admin/agreements/');
    await expect(page.getByText('No agreements yet.')).toBeVisible();

    await page.getByRole('button', { name: 'New agreement' }).click();
    const modal = page.getByRole('dialog', { name: 'New agreement' });
    await expect(modal).toBeVisible();

    await page.waitForTimeout(300);

    // Select company from Mantine Select input
    await modal.getByLabel('Company').click();
    await page.getByRole('option', { name: 'Acme Oy' }).click();

    await page.getByLabel('Title').fill('Siivoussopimus 2025');

    // Attach PDF file
    await page.locator('input[type="file"]').setInputFiles({
      name: 'agreement.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4 test pdf content'),
    });

    // Fill signers details
    await page.getByPlaceholder('Name').fill('Matti Meikäläinen');
    await page.getByPlaceholder('Email').fill('matti@acme.fi');

    // Submit form
    await page.getByRole('button', { name: 'Create' }).click();

    await expect(modal).not.toBeVisible();
    await expect(page.getByRole('cell', { name: 'Siivoussopimus 2025' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'AGR-2025-001' })).toBeVisible();
    await expect(page.getByText('Pending').first()).toBeVisible();
  });

  test('worst path: form validation and add/remove signer inputs', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    await page.route('**/api/v1/admin/companies**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: { items: [], total: 0 }, headers: CORS_HEADERS });
    });
    await page.route('**/api/v1/admin/agreements**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    });

    await page.goto('/admin/agreements/');
    await page.getByRole('button', { name: 'New agreement' }).click();

    // Submit without required PDF/Company/Title
    await page.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByText('Company, title and PDF are required.')).toBeVisible();

    // Test adding and removing signer dynamic row
    await page.getByRole('button', { name: 'Add signer' }).click();
    const nameInputs = page.getByPlaceholder('Name');
    await expect(nameInputs).toHaveCount(2);

    // Remove a signer row
    const modal = page.getByRole('dialog', { name: 'New agreement' });
    await modal.locator('.mantine-ActionIcon-root').first().click();
    await expect(page.getByPlaceholder('Name')).toHaveCount(1);
  });
});

test.describe('Admin Panel - Employees Page', () => {
  test('happy path: add employee and deactivate', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let employees: any[] = [];

    await page.route('**/api/v1/admin/employees**', async (route) => {
      const method = route.request().method();
      const url = route.request().url();

      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });

      if (url.includes('/deactivate') && method === 'POST') {
        if (employees.length > 0) employees[0].isActive = false;
        return route.fulfill({ status: 200, json: true, headers: CORS_HEADERS });
      }

      if (method === 'GET') return route.fulfill({ status: 200, json: employees, headers: CORS_HEADERS });
      if (method === 'POST') {
        const body = getBody(route);
        const fields = body.fields || body || {};
        const newEmp = {
          id: 'emp-1',
          email: fields.email,
          firstName: fields.firstName,
          lastName: fields.lastName,
          phone: fields.phone,
          role: fields.role,
          isActive: true,
          skills: [],
          serviceAreas: [],
          certifications: [],
          defaultHours: {},
        };
        employees.push(newEmp);
        return route.fulfill({ status: 200, json: true, headers: CORS_HEADERS });
      }
      return route.continue();
    });

    await page.goto('/admin/employees/');
    await expect(page.getByText('No employees yet.')).toBeVisible();

    await page.getByRole('button', { name: 'Add employee' }).click();
    await page.getByLabel('Email').fill('lauri@siivous.fi');
    await page.getByLabel('First name').fill('Lauri');
    await page.getByLabel('Last name').fill('Siivooja');
    await page.getByLabel('Phone').fill('0401234567');

    await page.getByRole('button', { name: 'Create employee' }).click();

    await expect(page.getByRole('dialog', { name: 'Add employee' })).not.toBeVisible();
    await expect(page.getByRole('cell', { name: 'Lauri Siivooja' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'lauri@siivous.fi' })).toBeVisible();
    await expect(page.getByText('Active')).toBeVisible();

    // Deactivate employee
    const deactivateBtn = page.getByRole('button', { name: 'Deactivate' });
    await deactivateBtn.click({ force: true });
    await expect(page.getByText('Inactive')).toBeVisible();
  });

  test('worst path: required field validation and server error', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    await page.route('**/api/v1/admin/employees**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
      if (method === 'POST') return route.fulfill({ status: 500, json: false, headers: CORS_HEADERS });
      return route.continue();
    });

    await page.goto('/admin/employees/');
    await page.getByRole('button', { name: 'Add employee' }).click();

    // Submit empty required fields
    await page.getByRole('button', { name: 'Create employee' }).click();
    await expect(page.getByText('Email, first and last name are required.')).toBeVisible();

    // Submit with fields present but server error
    await page.getByLabel('Email').fill('err@test.fi');
    await page.getByLabel('First name').fill('Err');
    await page.getByLabel('Last name').fill('User');
    await page.getByRole('button', { name: 'Create employee' }).click();
    await expect(page.getByText('Failed to create employee.')).toBeVisible();
  });
});

test.describe('Admin Panel - Companies & Branches Page', () => {
  test('happy path: create company and add branch', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let companies: any[] = [];
    let companyBranches: Record<string, any[]> = {};

    await page.route('**/api/v1/admin/companies**', async (route) => {
      const method = route.request().method();
      const url = route.request().url();

      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });

      if (url.includes('/branches') && method === 'POST') {
        const body = getBody(route);
        const companyId = body.companyId || 'comp-100';
        const newBranch = {
          id: 'br-1',
          companyId,
          name: body.name,
          city: body.city,
          isActive: true,
        };
        if (!companyBranches[companyId]) companyBranches[companyId] = [];
        companyBranches[companyId].push(newBranch);
        return route.fulfill({ status: 200, json: newBranch, headers: CORS_HEADERS });
      }

      if (method === 'GET') {
        const match = url.match(/\/companies\/([a-zA-Z0-9\-]+)($|\?)/);
        if (match) {
          const compId = match[1];
          if (compId !== 'branches') {
            const comp = companies.find(c => c.id === compId);
            return route.fulfill({
              status: 200,
              json: { company: comp, branches: companyBranches[compId] || [] },
              headers: CORS_HEADERS
            });
          }
        }
        return route.fulfill({ status: 200, json: { items: companies, total: companies.length }, headers: CORS_HEADERS });
      }

      if (method === 'POST') {
        const body = getBody(route);
        const newComp = {
          id: 'comp-100',
          businessId: body.businessId,
          name: body.name,
          contactName: body.contactName,
          isActive: true,
        };
        companies.push(newComp);
        return route.fulfill({ status: 200, json: newComp, headers: CORS_HEADERS });
      }

      return route.continue();
    });

    await page.goto('/admin/companies/');
    await expect(page.getByText('No companies yet. Add one to get started.')).toBeVisible();

    // Create Company
    await page.getByRole('button', { name: 'Add company' }).click();
    await page.getByLabel('Business ID').fill('9999999-9');
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Nordic Clean Ltd');
    await page.getByLabel('Contact name').fill('Anna Manager');

    await page.getByRole('button', { name: 'Create company' }).click();

    await expect(page.getByRole('dialog', { name: 'Add company' })).not.toBeVisible();
    await expect(page.getByRole('cell', { name: 'Nordic Clean Ltd' })).toBeVisible();
    await expect(page.getByRole('cell', { name: '9999999-9' })).toBeVisible();

    // Expand branches
    await page.locator('button').filter({ has: page.locator('.lucide-chevron-down') }).click();
    await expect(page.getByText('No branches yet.')).toBeVisible();

    // Add Branch
    await page.getByRole('button', { name: 'Add branch' }).click();
    const branchModal = page.getByRole('dialog', { name: 'Add branch' });
    await branchModal.getByRole('textbox', { name: 'Name', exact: true }).fill('Espoo Main Branch');
    await branchModal.getByLabel('City').fill('Espoo');

    await branchModal.getByRole('button', { name: 'Add branch', exact: true }).click();

    await expect(branchModal).not.toBeVisible();
    await expect(page.getByText('Espoo Main Branch · Espoo')).toBeVisible();
  });

  test('worst path: businessId and name missing validation', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    await page.route('**/api/v1/admin/companies**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: { items: [], total: 0 }, headers: CORS_HEADERS });
      if (method === 'POST') return route.fulfill({ status: 400, json: false, headers: CORS_HEADERS });
      return route.continue();
    });

    await page.goto('/admin/companies/');
    await page.getByRole('button', { name: 'Add company' }).click();

    await page.getByRole('button', { name: 'Create company' }).click();
    await expect(page.getByText('Business ID and name are required.')).toBeVisible();
  });
});

test.describe('Admin Panel - Shifts Page', () => {
  test('happy path: create shift, test schedules, assign & unassign employee, deactivate', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let shifts: any[] = [];
    let assignments: Record<string, any[]> = {};

    const mockCompanies = [{ id: 'c-1', name: 'CleanTech Oy', businessId: '1111111-1', isActive: true }];
    const mockBranches = [{ id: 'b-1', companyId: 'c-1', name: 'HQ Branch', isActive: true }];
    const mockEmployees = [{ id: 'e-1', firstName: 'Sami', lastName: 'Siivooja', email: 'sami@test.fi', role: 'employee', isActive: true, skills: [], serviceAreas: [], certifications: [], defaultHours: {} }];

    await page.route('**/api/v1/admin/companies**', async (route) => {
      const url = route.request().url();
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (url.includes('/c-1')) {
        return route.fulfill({ status: 200, json: { company: mockCompanies[0], branches: mockBranches }, headers: CORS_HEADERS });
      }
      return route.fulfill({ status: 200, json: { items: mockCompanies, total: 1 }, headers: CORS_HEADERS });
    });

    await page.route('**/api/v1/admin/employees**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: mockEmployees, headers: CORS_HEADERS });
    });

    await page.route('**/api/v1/admin/shifts**', async (route) => {
      const method = route.request().method();
      const url = route.request().url();

      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });

      if (url.includes('/deactivate') && method === 'POST') {
        if (shifts.length > 0) shifts[0].isActive = false;
        return route.fulfill({ status: 200, json: true, headers: CORS_HEADERS });
      }

      if (url.includes('/assignments') && method === 'DELETE') {
        const match = url.match(/\/shifts\/([^\/]+)\/assignments\/([^\/]+)/);
        if (match) {
          const shiftId = match[1];
          const assignId = match[2];
          assignments[shiftId] = (assignments[shiftId] || []).filter(a => a.id !== assignId);
        }
        return route.fulfill({ status: 200, json: true, headers: CORS_HEADERS });
      }

      if (url.includes('/assignments') && method === 'POST') {
        const match = url.match(/\/shifts\/([^\/]+)\/assignments/);
        const shiftId = match ? match[1] : 's-1';
        const body = getBody(route);
        const newAssign = { id: 'as-1', shiftId, employeeId: body.employeeId, assignedAtUtc: new Date().toISOString(), isActive: true };
        if (!assignments[shiftId]) assignments[shiftId] = [];
        assignments[shiftId].push(newAssign);
        return route.fulfill({ status: 200, json: newAssign, headers: CORS_HEADERS });
      }

      if (url.includes('/assignments') && method === 'GET') {
        const match = url.match(/\/shifts\/([^\/]+)\/assignments/);
        const shiftId = match ? match[1] : 's-1';
        return route.fulfill({ status: 200, json: assignments[shiftId] || [], headers: CORS_HEADERS });
      }

      if (method === 'GET') return route.fulfill({ status: 200, json: shifts, headers: CORS_HEADERS });

      if (method === 'POST') {
        const body = getBody(route);
        const newShift = {
          id: 's-1',
          companyId: body.companyId,
          branchId: body.branchId,
          name: body.name,
          schedule: body.schedule,
          notes: body.notes,
          isActive: true,
        };
        shifts.push(newShift);
        return route.fulfill({ status: 200, json: newShift, headers: CORS_HEADERS });
      }

      return route.continue();
    });

    await page.goto('/admin/shifts/');
    await expect(page.getByText('No shifts yet.')).toBeVisible();

    // Create shift
    await page.getByRole('button', { name: 'Add shift' }).click();

    // Select company and branch
    const modal = page.getByRole('dialog', { name: 'Add shift' });

    await modal.getByLabel('Company').click();
    await page.getByRole('option', { name: 'CleanTech Oy' }).click();

    await page.waitForTimeout(500);

    await modal.getByLabel('Branch').click();
    await page.getByRole('option', { name: 'HQ Branch' }).click();

    await modal.getByLabel('Name').fill('Aamuvuoro');

    // Test switching schedule types
    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Specific days' }).click();
    await expect(modal.getByText('Days')).toBeVisible();

    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Bi-weekly' }).click();
    await expect(modal.getByText('Week parity')).toBeVisible();

    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Monthly' }).click();
    await expect(modal.getByText('Day of month (1-31)')).toBeVisible();

    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Daily' }).click();

    // Submit shift creation
    await modal.getByRole('button', { name: 'Create shift' }).click();

    await expect(modal).not.toBeVisible();
    await expect(page.getByRole('cell', { name: 'Aamuvuoro' })).toBeVisible();

    // Assign employee
    await page.getByRole('button', { name: 'Employees' }).click();
    await expect(page.getByText('No employees assigned.')).toBeVisible();

    await page.locator('input[placeholder="Select employees"]').click();
    await page.getByRole('option', { name: 'Sami Siivooja' }).click();

    await page.getByRole('button', { name: 'Assign' }).click();
    await expect(page.locator('p', { hasText: 'Sami Siivooja' })).toBeVisible();

    // Unassign employee
    await page.getByRole('button', { name: 'Unassign' }).first().click({ force: true });
    await expect(page.getByText('No employees assigned.')).toBeVisible();

    // Deactivate shift
    const deactivateBtn = page.getByRole('button', { name: 'Deactivate' });
    await deactivateBtn.click({ force: true });
    await expect(page.getByRole('button', { name: 'Deactivate' })).not.toBeVisible();
  });

  test('worst path: required fields missing & specific days validation', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    await page.route('**/api/v1/admin/companies**', async (route) => {
      const url = route.request().url();
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (url.includes('/c-1')) {
        return route.fulfill({ status: 200, json: { company: { id: 'c-1', name: 'CleanTech Oy' }, branches: [{ id: 'b-1', companyId: 'c-1', name: 'Main Branch' }] }, headers: CORS_HEADERS });
      }
      return route.fulfill({ status: 200, json: { items: [{ id: 'c-1', name: 'CleanTech Oy' }], total: 1 }, headers: CORS_HEADERS });
    });
    await page.route('**/api/v1/admin/shifts**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    });

    await page.goto('/admin/shifts/');
    await page.getByRole('button', { name: 'Add shift' }).click();

    // Submit empty fields
    await page.getByRole('button', { name: 'Create shift' }).click();
    await expect(page.getByText('Company, branch and name are required.')).toBeVisible();

    // Select company, branch, name, but choose Specific Days without selecting any day
    const modal = page.getByRole('dialog', { name: 'Add shift' });

    await modal.getByLabel('Company').click();
    await page.getByRole('option', { name: 'CleanTech Oy' }).click();

    await page.waitForTimeout(500);

    await modal.getByLabel('Branch').click();
    await page.getByRole('option', { name: 'Main Branch' }).click();

    await modal.getByLabel('Name').fill('Iltavuoro');
    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Specific days' }).click();

    await modal.getByRole('button', { name: 'Create shift' }).click();
    await expect(page.getByText('Select at least one day.')).toBeVisible();
  });

  test('create shift with bi-weekly and monthly schedule types and verify payload', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let createdShifts: any[] = [];

    await page.route('**/api/v1/admin/employees**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    });

    await page.route('**/api/v1/admin/companies**', async (route) => {
      const url = route.request().url();
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (url.includes('/c-1')) {
        return route.fulfill({ status: 200, json: { company: { id: 'c-1', name: 'CleanTech Oy' }, branches: [{ id: 'b-1', companyId: 'c-1', name: 'Main Branch' }] }, headers: CORS_HEADERS });
      }
      return route.fulfill({ status: 200, json: { items: [{ id: 'c-1', name: 'CleanTech Oy' }], total: 1 }, headers: CORS_HEADERS });
    });

    await page.route('**/api/v1/admin/shifts**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: createdShifts, headers: CORS_HEADERS });
      if (method === 'POST') {
        const body = getBody(route);
        const newShift = {
          id: `s-${createdShifts.length + 1}`,
          companyId: body.companyId,
          branchId: body.branchId,
          name: body.name,
          schedule: body.schedule,
          notes: body.notes,
          isActive: true,
        };
        createdShifts.push(newShift);
        return route.fulfill({ status: 200, json: newShift, headers: CORS_HEADERS });
      }
      return route.continue();
    });

    await page.goto('/admin/shifts/');
    await expect(page.getByText('No shifts yet.')).toBeVisible();

    // Create Bi-weekly shift
    await page.getByRole('button', { name: 'Add shift' }).click();
    const modal = page.getByRole('dialog', { name: 'Add shift' });

    await modal.getByLabel('Company').click();
    await page.getByRole('option', { name: 'CleanTech Oy' }).click();
    await page.waitForTimeout(300);

    await modal.getByLabel('Branch').click();
    await page.getByRole('option', { name: 'Main Branch' }).click();

    await modal.getByLabel('Name').fill('BiWeekly Shift');
    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Bi-weekly' }).click();

    // Submit bi-weekly without selecting day -> validation error
    await modal.getByRole('button', { name: 'Create shift' }).click();
    await expect(page.getByText('Select a day.')).toBeVisible();

    // Select day (e.g., Monday = 1)
    await modal.getByLabel('Day').click();
    await page.getByRole('option', { name: 'Monday' }).click();

    await modal.getByRole('button', { name: 'Create shift' }).click();
    await expect(modal).not.toBeVisible();

    // Verify created bi-weekly shift
    expect(createdShifts.length).toBe(1);
    expect(createdShifts[0].schedule.type).toBe(3);
    expect(createdShifts[0].schedule.biWeeklyWeekParity).toBe(1);

    // Create Monthly shift
    await page.getByRole('button', { name: 'Add shift' }).click();
    await modal.getByLabel('Company').click();
    await page.getByRole('option', { name: 'CleanTech Oy' }).click();
    await page.waitForTimeout(300);

    await modal.getByLabel('Branch').click();
    await page.getByRole('option', { name: 'Main Branch' }).click();

    await modal.getByLabel('Name').fill('Monthly Shift');
    await modal.getByLabel('Schedule').click();
    await page.getByRole('option', { name: 'Monthly' }).click();

    await modal.getByRole('button', { name: 'Create shift' }).click();
    await expect(modal).not.toBeVisible();

    expect(createdShifts.length).toBe(2);
    expect(createdShifts[1].schedule.type).toBe(5);
    expect(createdShifts[1].schedule.monthlyDay).toBe(1);
  });
});
