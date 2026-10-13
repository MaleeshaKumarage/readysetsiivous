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
    await page.getByRole('button', { name: 'Add Item', exact: true }).click();
    await itemInputs.nth(3).fill('Sanitize door handles');

    // Submit form
    await page.getByRole('button', { name: 'Create Template' }).click();

    // Verify created template is rendered
    await expect(page.getByText('Daily Office Checklist', { exact: true })).toBeVisible();
    await expect(page.getByText('Standard office cleaning routines')).toBeVisible();
    await expect(page.getByText('Vacuum carpets')).toBeVisible();
    await expect(page.getByText('Sanitize door handles')).toBeVisible();

    // Edit template
    await page.getByRole('button', { name: 'Edit' }).click({ force: true });
    await expect(page.getByText('Edit Template', { exact: true })).toBeVisible();
    await page.getByPlaceholder('e.g. Daily Office Cleaning Checklist').fill('Updated Office Checklist');
    await page.getByRole('button', { name: 'Update Template' }).click();

    await expect(page.getByText('Updated Office Checklist', { exact: true })).toBeVisible();

    // Delete template
    await page.getByRole('button', { name: 'Delete template' }).click();
    await expect.poll(() => deleteConfirmed).toBe(true);

    await expect(page.getByText('No quality cycle templates created yet.')).toBeVisible();
  });

  test('submissions tab and PDF summary report export', async ({ page }) => {
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

    await page.route(/\/api\/v1\/admin\/shifts(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: mockShifts, headers: CORS_HEADERS });
    });

    await page.route(/\/api\/v1\/admin\/quality-cycle\/templates(\/.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    });

    await page.route(/\/api\/v1\/admin\/quality-cycle\/forms(\/.*|\?.*)?$/, async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: mockForms, headers: CORS_HEADERS });
    });

    await page.route(/\/api\/v1\/admin\/quality-cycle\/summary-pdf(\/.*|\?.*)?$/, async (route) => {
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
    await page.getByText('Submissions & PDF Reports', { exact: true }).click();

    await expect(page.getByText('Quality Cycle Submissions Log', { exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Morning Office Shift' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Anna Cleaner' })).toBeVisible();
    await expect(page.getByText('Submitted')).toBeVisible();
    await expect(page.getByText('2 / 2')).toBeVisible();
    await expect(page.getByText('Note: Completed on time')).toBeVisible();

    // Select shift for PDF summary download
    await page.getByRole('textbox', { name: 'Filter by Shift' }).click();
    await page.getByRole('option', { name: 'Morning Office Shift' }).click();
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export PDF Summary' }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/^QualityCycle_Summary_shift-1_\d{4}_\d{2}\.pdf$/);
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
    await expect(page.getByRole('heading', { name: 'Evening Office Cleaning' })).toBeVisible();
    await expect(page.getByText('Matti Meikäläinen', { exact: true })).toBeVisible();

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


test('employee cannot open admin template or report controls', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['employee']; });
  let qcRequests = 0;
  await page.route(/\/api\/v1\/admin\/quality-cycle\//, route => { qcRequests++; return route.fulfill({ status: 403, headers: CORS_HEADERS }); });
  await page.goto('/admin/quality-cycle/');
  await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create Template' })).toHaveCount(0);
  expect(qcRequests).toBe(0);
});

test('monthly report keeps weekday and biweekly shifts separate under one branch', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/quality-cycle\/monthly-summary\?/, route => route.fulfill({ status: 200, headers: CORS_HEADERS, json: {
    year: 2026, month: 10, timeZoneId: 'Europe/Helsinki', generatedUtc: new Date().toISOString(), shifts: [
      { shiftId: 'weekday', shiftName: 'Weekday 2 hours', companyName: 'AAA', branchId: 'aaa1', branchName: 'AAA1', scheduledOccurrences: 22, scheduledHours: 44, completedOccurrences: 20, missingOccurrences: 2, pendingOccurrences: 0, submittedTasks: 20, totalTasks: 22, items: [{ itemText: 'Corridor', daysPerformed: 12, recordedOccurrences: 12, recordedDates: [] }] },
      { shiftId: 'biweekly', shiftName: 'Biweekly 3 hours', companyName: 'AAA', branchId: 'aaa1', branchName: 'AAA1', scheduledOccurrences: 2, scheduledHours: 6, completedOccurrences: 2, missingOccurrences: 0, pendingOccurrences: 0, submittedTasks: 2, totalTasks: 2, items: [{ itemText: 'Gym', daysPerformed: 4, recordedOccurrences: 4, recordedDates: [] }] },
    ]
  } }));
  await page.goto('/admin/quality-cycle/');
  await page.getByText('Submissions & PDF Reports', { exact: true }).click();
  await page.getByRole('button', { name: 'Load monthly report' }).click();
  const weekdays = page.getByRole('row').filter({ hasText: 'Weekday 2 hours' });
  await expect(weekdays).toContainText('22 / 44');
  await expect(weekdays).toContainText('Corridor: 12 days');
  const biweekly = page.getByRole('row').filter({ hasText: 'Biweekly 3 hours' });
  await expect(biweekly).toContainText('2 / 6');
  await expect(biweekly).toContainText('Gym: 4 days');
  await expect(page.getByRole('button', { name: 'Shift PDF', exact: true })).toHaveCount(2);
});


test('cleaner records selected activities without completing every item or adding notes', async ({ page }) => {
  let submitted: any;
  const form = { id: 'record', shiftName: 'Daily work', employeeName: 'Cleaner', shiftOccurrenceUtc: '2026-01-01T08:00:00Z', token: 'activity-token', items: [{ itemText: 'Corridor', isChecked: false }, { itemText: 'Gym', isChecked: false }], photoUrls: [], isSubmitted: false };
  await page.route(/\/api\/v1\/public\/tenant-a\/quality-cycle\/activity-token(\/submit)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    if (route.request().method() === 'POST') { submitted = getBody(route); return route.fulfill({ status: 200, headers: CORS_HEADERS, json: { ...form, isSubmitted: true, taskStatus: 'Recorded', submittedUtc: new Date().toISOString() } }); }
    return route.fulfill({ status: 200, headers: CORS_HEADERS, json: form });
  });
  await page.goto('/quality-cycle/?tenant=tenant-a&token=activity-token');
  await page.getByText('Corridor', { exact: true }).click();
  await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('heading', { name: 'Thank You!' })).toBeVisible();
  expect(submitted.items).toEqual([{ itemText: 'Corridor', isChecked: true }, { itemText: 'Gym', isChecked: false }]);
  expect(submitted.cleanerNotes).toBe('');
});

test('tenant admin creates a share link and revokes it', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route(/\/api\/v1\/admin\/quality-cycle\/monthly-summary\?/, route => route.fulfill({ status: 200, headers: CORS_HEADERS, json: {
    year: 2026, month: 10, timeZoneId: 'Europe/Helsinki', shifts: [{ shiftId: 'weekday', shiftName: 'Weekdays', companyName: 'AAA', branchId: 'aaa1', branchName: 'AAA1', scheduledOccurrences: 22, scheduledHours: 44, completedOccurrences: 22, missingOccurrences: 0, pendingOccurrences: 0, submittedTasks: 22, totalTasks: 22, items: [{ itemText: 'Corridor', daysPerformed: 12, recordedOccurrences: 12 }] }]
  } }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/report-shares(\/.*)?$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    if (route.request().method() === 'DELETE') return route.fulfill({ status: 204, headers: CORS_HEADERS });
    return route.fulfill({ status: 200, headers: CORS_HEADERS, json: { id: 'share', token: 'public-report-token', tenantId: 'tenant-a', expiresUtc: new Date(Date.now() + 30 * 86400000).toISOString(), year: 2026, month: 10, isRevoked: false } });
  });
  await page.goto('/admin/quality-cycle/'); await page.getByText('Submissions & PDF Reports', { exact: true }).click();
  await page.getByRole('button', { name: 'Load monthly report' }).click();
  await page.getByRole('button', { name: 'Share shift report', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Shareable report link' })).toHaveValue(/\/public\/tenant-a\/quality-cycle\/reports\/public-report-token\/pdf$/);
  await page.getByRole('button', { name: 'Revoke link' }).click(); await expect(page.getByText('Inactive link', { exact: true })).toBeVisible();
});


test('admin can attach a checklist to an existing shift', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const shift = { id: 'existing-shift', companyId: 'company-a', branchId: 'branch-a', name: 'Existing shift', schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, isActive: true, qualityCycleTemplateId: null };
  let saved: any;
  await page.route(/\/api\/v1\/admin\/shifts$/, route => route.fulfill({ status: 200, headers: CORS_HEADERS, json: [shift] }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/templates$/, route => route.fulfill({ status: 200, headers: CORS_HEADERS, json: [{ id: 'template-a', title: 'Activities checklist', companyId: 'company-a', branchId: 'branch-a', isActive: true, items: ['Corridor', 'Gym'] }] }));
  await page.route(/\/api\/v1\/admin\/shifts\/existing-shift\/quality-cycle-template$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    saved = getBody(route); return route.fulfill({ status: 200, headers: CORS_HEADERS, json: { ...shift, qualityCycleTemplateId: saved.templateId } });
  });
  await page.goto('/admin/shifts/'); await page.getByRole('button', { name: 'Checklist template', exact: true }).click();
  await page.getByRole('textbox', { name: 'Checklist template', exact: true }).click();
  await page.getByRole('option', { name: 'Activities checklist' }).click();
  await page.getByRole('button', { name: 'Save template assignment' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(saved).toEqual({ templateId: 'template-a' });
});
