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

test.describe('Admin Panel - Quality Cycle Page', () => {
  test('navigation, tab switching, template creation, edit and deletion', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let templates: any[] = [];

    await page.route(/\/api\/v1\/admin\/quality-cycle\/templates(\/.*)?$/, async (route) => {
      const method = route.request().method();
      const url = route.request().url();

      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });

      if (method === 'GET') {
        const match = url.match(/\/templates\/([a-zA-Z0-9\-]+)($|\?)/);
        if (match && match[1] !== 'templates') {
          const t = templates.find(item => item.id === match[1]);
          return route.fulfill({ status: 200, json: t, headers: CORS_HEADERS });
        }
        return route.fulfill({ status: 200, json: templates, headers: CORS_HEADERS });
      }

      if (method === 'POST') {
        const body = getBody(route);
        const newTpl = {
          id: 'tpl-100',
          title: body.title,
          description: body.description,
          items: body.items || [],
          isActive: true,
          createdUtc: new Date().toISOString(),
          updatedUtc: new Date().toISOString(),
        };
        templates.push(newTpl);
        return route.fulfill({ status: 200, json: newTpl, headers: CORS_HEADERS });
      }

      if (method === 'PUT') {
        const body = getBody(route);
        const match = url.match(/\/templates\/([a-zA-Z0-9\-]+)($|\?)/);
        const id = match ? match[1] : 'tpl-100';
        const tplIndex = templates.findIndex(t => t.id === id);
        if (tplIndex !== -1) {
          templates[tplIndex] = {
            ...templates[tplIndex],
            title: body.title,
            description: body.description,
            items: body.items || [],
            isActive: body.isActive ?? true,
            updatedUtc: new Date().toISOString(),
          };
          return route.fulfill({ status: 200, json: templates[tplIndex], headers: CORS_HEADERS });
        }
      }

      if (method === 'DELETE') {
        const match = url.match(/\/templates\/([a-zA-Z0-9\-]+)($|\?)/);
        const id = match ? match[1] : '';
        templates = templates.filter(t => t.id !== id);
        return route.fulfill({ status: 204, headers: CORS_HEADERS });
      }

      return route.continue();
    });

    let deleteConfirmed = false;
    page.on('dialog', async dialog => {
      deleteConfirmed = true;
      await dialog.accept();
    });

    await page.goto('/admin/quality-cycle/');

    // Page header checks
    await expect(page.getByRole('heading', { name: 'Quality Cycle Management' })).toBeVisible();
    await expect(page.getByText('No quality cycle templates created yet.')).toBeVisible();

    // Fill form to create template
    await page.getByPlaceholder('e.g. Daily Office Cleaning Checklist').fill('Daily Office Checklist');
    await page.getByPlaceholder('Optional description or guidance...').fill('Standard office cleaning routines');

    const itemInputs = page.locator('input[placeholder^="Item "]');
    await itemInputs.nth(0).fill('Vacuum carpets');
    await itemInputs.nth(1).fill('Wipe desks and tables');
    await itemInputs.nth(2).fill('Empty trash bins');

    // Add another item
    await page.getByRole('button', { name: '+ Add Item' }).click();
    await itemInputs.nth(3).fill('Sanitize door handles');

    // Submit form
    await page.getByRole('button', { name: 'Create Template' }).click();

    // Verify created template is rendered
    await expect(page.getByRole('heading', { name: 'Daily Office Checklist' })).toBeVisible();
    await expect(page.getByText('Standard office cleaning routines')).toBeVisible();
    await expect(page.getByText('Vacuum carpets')).toBeVisible();
    await expect(page.getByText('Sanitize door handles')).toBeVisible();

    // Edit template
    await page.getByRole('button', { name: 'Edit' }).click({ force: true });
    await expect(page.getByRole('heading', { name: 'Edit Template' })).toBeVisible();
    await page.getByPlaceholder('e.g. Daily Office Cleaning Checklist').fill('Updated Office Checklist');
    await page.getByRole('button', { name: 'Update Template' }).click();

    await expect(page.getByRole('heading', { name: 'Updated Office Checklist' })).toBeVisible();

    // Delete template
    await page.getByRole('button', { name: 'Delete' }).click({ force: true });
    await expect.poll(() => deleteConfirmed).toBe(true);

    await expect(page.getByText('No quality cycle templates created yet.')).toBeVisible();
  });

  test('submissions tab, dispatch forms, and PDF summary report export', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    const mockShifts = [
      { id: 'shift-1', name: 'Morning Office Shift', isActive: true },
    ];

    const mockForms = [
      {
        id: 'form-1',
        shiftId: 'shift-1',
        shiftName: 'Morning Office Shift',
        employeeId: 'emp-1',
        employeeName: 'Anna Cleaner',
        templateId: 'tpl-1',
        templateTitle: 'Daily Office Checklist',
        shiftOccurrenceUtc: new Date().toISOString(),
        token: 'token-abc',
        items: [
          { itemText: 'Vacuum carpets', isChecked: true },
          { itemText: 'Wipe desks', isChecked: true },
        ],
        photoUrls: ['https://example.com/photo.jpg'],
        cleanerNotes: 'Completed on time',
        isSubmitted: true,
        submittedUtc: new Date().toISOString(),
        createdUtc: new Date().toISOString(),
      },
    ];

    let lastDialogMessage = '';
    page.on('dialog', async dialog => {
      lastDialogMessage = dialog.message();
      await dialog.accept();
    });

    await page.route(/\/api\/v1\/admin\/shifts(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: mockShifts, headers: CORS_HEADERS });
    });

    await page.route(/\/api\/v1\/admin\/quality-cycle\/templates(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    });

    await page.route(/\/api\/v1\/admin\/quality-cycle\/forms(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: mockForms, headers: CORS_HEADERS });
    });

    let dispatchCalled = false;
    await page.route(/\/api\/v1\/admin\/quality-cycle\/dispatch(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      dispatchCalled = true;
      return route.fulfill({ status: 200, json: { dispatchedCount: 2 }, headers: CORS_HEADERS });
    });

    await page.route(/\/api\/v1\/admin\/quality-cycle\/summary-pdf(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({
        status: 200,
        contentType: 'application/pdf',
        body: Buffer.from('%PDF-1.4 mock pdf content'),
        headers: CORS_HEADERS,
      });
    });

    await page.goto('/admin/quality-cycle/');

    // Switch to Submissions & PDF Reports tab
    await page.getByRole('button', { name: 'Submissions & PDF Reports' }).click();

    await expect(page.getByRole('heading', { name: 'Quality Cycle Submissions Log' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Morning Office Shift' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Anna Cleaner' })).toBeVisible();
    await expect(page.getByText('Submitted')).toBeVisible();
    await expect(page.getByText('2 / 2')).toBeVisible();
    await expect(page.getByText('Note: Completed on time')).toBeVisible();

    // Trigger Dispatch Forms Now
    await page.getByRole('button', { name: 'Dispatch Forms Now' }).click();
    await expect.poll(() => dispatchCalled).toBe(true);
    await expect.poll(() => lastDialogMessage).toContain('Dispatched 2 Quality Cycle forms.');

    // Select shift for PDF summary download
    await page.locator('select').first().selectOption('shift-1');
    await page.getByRole('button', { name: 'Export PDF Summary' }).click();
  });
});

test.describe('Public Quality Cycle Page', () => {
  test('happy path: complete and submit quality cycle form', async ({ page }) => {
    const mockForm = {
      id: 'form-100',
      shiftId: 'shift-100',
      shiftName: 'Evening Office Cleaning',
      employeeId: 'emp-200',
      employeeName: 'Matti Meikäläinen',
      templateId: 'tpl-300',
      templateTitle: 'Evening Checklist',
      shiftOccurrenceUtc: '2025-05-15T18:00:00Z',
      token: 'valid-test-token',
      items: [
        { itemText: 'Empty trash cans', isChecked: false },
        { itemText: 'Mop floor', isChecked: false },
      ],
      photoUrls: [],
      cleanerNotes: '',
      isSubmitted: false,
      submittedUtc: null,
      createdUtc: new Date().toISOString(),
    };

    await page.route(/\/api\/v1\/public\/[^\/]+\/quality-cycle\/valid-test-token$/, async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: mockForm, headers: CORS_HEADERS });
      return route.continue();
    });

    await page.route(/\/api\/v1\/public\/[^\/]+\/quality-cycle\/valid-test-token\/submit$/, async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'POST') {
        const body = getBody(route);
        return route.fulfill({
          status: 200,
          json: {
            ...mockForm,
            isSubmitted: true,
            submittedUtc: new Date().toISOString(),
            cleanerNotes: body.cleanerNotes,
          },
          headers: CORS_HEADERS,
        });
      }
      return route.continue();
    });

    await page.goto('/quality-cycle?token=valid-test-token');

    // Header and shift info check
    await expect(page.getByText('ReadySetSiivous Quality Cycle')).toBeVisible();
    await expect(page.getByText('Evening Office Cleaning')).toBeVisible();
    await expect(page.getByText('Cleaner: Matti Meikäläinen')).toBeVisible();

    // Toggle items
    await page.getByText('Empty trash cans').click();
    await page.getByText('Mop floor').click();

    // Enter notes
    await page.getByPlaceholder('Any additional notes or observations...').fill('Floor was extra dirty, mopped twice.');

    // Submit form
    await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();

    // Verify submission thank-you screen
    await expect(page.getByRole('heading', { name: 'Thank You!' })).toBeVisible();
    await expect(page.getByText('Your Quality Cycle Form for Evening Office Cleaning has been successfully submitted.')).toBeVisible();
  });

  test('worst path: missing or invalid form token', async ({ page }) => {
    // Missing token
    await page.goto('/quality-cycle');
    await expect(page.getByRole('heading', { name: 'Invalid Form Link' })).toBeVisible();
    await expect(page.getByText('Missing Quality Cycle Form token.')).toBeVisible();

    // Invalid token mock API 404
    await page.route(/\/api\/v1\/public\/[^\/]+\/quality-cycle\/invalid-token$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 404, json: null, headers: CORS_HEADERS });
    });

    await page.goto('/quality-cycle?token=invalid-token');
    await expect(page.getByRole('heading', { name: 'Invalid Form Link' })).toBeVisible();
  });
});
