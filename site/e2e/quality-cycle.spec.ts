import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

test.describe('Admin Panel - Quality Cycle Page', () => {
  test('creates template and navigates tabs', async ({ page }) => {
    await setupAdminMocks(page, { authenticated: true });

    let templates: any[] = [];
    let forms: any[] = [];

    await page.route('**/api/v1/admin/quality-cycle/templates**', async (route) => {
      const method = route.request().method();
      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (method === 'GET') return route.fulfill({ status: 200, json: templates, headers: CORS_HEADERS });
      if (method === 'POST') {
        const body = route.request().postDataJSON() || {};
        const newTpl = {
          id: 'tpl-1',
          title: body.title,
          description: body.description,
          items: body.items,
          isActive: true,
        };
        templates.push(newTpl);
        return route.fulfill({ status: 200, json: newTpl, headers: CORS_HEADERS });
      }
      return route.continue();
    });

    await page.route('**/api/v1/admin/quality-cycle/forms**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: forms, headers: CORS_HEADERS });
    });

    await page.route('**/api/v1/admin/shifts**', async (route) => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      return route.fulfill({ status: 200, json: [], headers: CORS_HEADERS });
    });

    await page.goto('/admin/quality-cycle/');

    await expect(page.getByRole('heading', { name: 'Quality Cycle Management' })).toBeVisible();
    await expect(page.getByText('No quality cycle templates created yet.')).toBeVisible();

    // Fill new template form
    await page.getByPlaceholder('e.g. Daily Office Cleaning Checklist').fill('Office Checklist');
    await page.getByPlaceholder('Optional description or guidance...').fill('Daily tasks for office');

    const itemInputs = page.getByPlaceholder(/Item \d+ task\.\.\./);
    await itemInputs.nth(0).fill('Vacuum floors');
    await itemInputs.nth(1).fill('Wipe desks');

    await page.getByRole('button', { name: 'Create Template' }).click();

    await expect(page.getByText('Office Checklist')).toBeVisible();
    await expect(page.getByText('Vacuum floors')).toBeVisible();

    // Switch tab
    await page.getByRole('button', { name: 'Submissions & PDF Reports' }).click();
    await expect(page.getByRole('button', { name: 'Dispatch Forms Now' })).toBeVisible();
    await expect(page.getByText('No quality cycle forms logged for the selected filter.')).toBeVisible();
  });
});

test.describe('Public Quality Cycle Form', () => {
  test('cleaner can toggle items, attach photo and submit form', async ({ page }) => {
    let submittedData: any = null;

    const mockForm = {
      id: 'f-1',
      token: 'tok-test-123',
      shiftName: 'Morning Office Shift',
      employeeName: 'Lauri Siivooja',
      shiftOccurrenceUtc: new Date().toISOString(),
      isSubmitted: false,
      items: [
        { itemText: 'Empty trash cans', isChecked: false },
        { itemText: 'Clean restrooms', isChecked: false },
      ],
      cleanerNotes: '',
      photoUrls: [],
    };

    await page.route('**/api/v1/public/**/quality-cycle/**', async (route) => {
      const method = route.request().method();
      const url = route.request().url();

      if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });

      if (url.includes('/submit') && method === 'POST') {
        submittedData = route.request().postDataJSON() || {};
        return route.fulfill({ status: 200, json: true, headers: CORS_HEADERS });
      }

      if (method === 'GET') {
        return route.fulfill({ status: 200, json: mockForm, headers: CORS_HEADERS });
      }

      return route.continue();
    });

    await page.goto('/quality-cycle?token=tok-test-123');

    await expect(page.getByText('ReadySetSiivous Quality Cycle')).toBeVisible();
    await expect(page.getByText('Morning Office Shift')).toBeVisible();

    // Toggle checklist item
    await page.getByText('Empty trash cans').click();

    // Add notes
    await page.getByPlaceholder('Any additional notes or observations...').fill('Restrooms looked clean');

    // Attach sample photo file
    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles({
      name: 'photo.jpg',
      mimeType: 'image/jpeg',
      buffer: Buffer.from('fake image binary content'),
    });

    // Verify photo preview appears
    await expect(page.locator('img[alt="Upload 1"]')).toBeVisible();

    // Submit form
    await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();

    // Expect Thank You screen
    await expect(page.getByRole('heading', { name: 'Thank You!' })).toBeVisible();
    expect(submittedData).not.toBeNull();
    expect(submittedData.cleanerNotes).toBe('Restrooms looked clean');
  });
});
