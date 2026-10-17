import { test, expect, type Page } from '@playwright/test';
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

async function setupReportWorkspace(page: Page) {
  await setupAdminMocks(page);
  await page.clock.setFixedTime(new Date('2026-10-17T12:00:00Z'));
  const rows = [
    { shiftId: 'weekday', shiftName: 'Weekday 2 hours', companyName: 'AAA', branchId: 'aaa1', branchName: 'AAA1', scheduledOccurrences: 22, scheduledHours: 44, completedOccurrences: 20, recordedStaffHours: 43.5 },
    { shiftId: 'biweekly', shiftName: 'Biweekly 3 hours', companyName: 'AAA', branchId: 'aaa1', branchName: 'AAA1', scheduledOccurrences: 2, scheduledHours: 6, completedOccurrences: 2, recordedStaffHours: null, incompleteTimingRecords: 1 },
  ];
  const calls = { previews: [] as any[], exports: [] as any[], sends: [] as any[], retry: 0 };
  let shares: any[] = [];
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ headers: CORS_HEADERS, json: rows.map(row => ({ id: row.shiftId, name: row.shiftName, isActive: true })) }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/templates$/, route => route.fulfill({ headers: CORS_HEADERS, json: [] }));
  await page.route(/\/api\/v1\/admin\/quality-cycle\/(reports|report-shares)(\/.*)?$/, async route => {
    const method = route.request().method(); const url = route.request().url(); const body = getBody(route);
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    if (url.endsWith('/preview')) {
      calls.previews.push(body);
      return route.fulfill({ headers: CORS_HEADERS, json: { year: 2026, month: 10, timeZoneId: 'Europe/Helsinki', periods: [{ from: '2026-10-01', to: '2026-10-31' }], shifts: rows.filter(row => body.shiftIds.includes(row.shiftId)) } });
    }
    if (url.endsWith('/pdf')) { calls.exports.push(body); return route.fulfill({ headers: CORS_HEADERS, contentType: 'application/pdf', body: '%PDF-1.4 synthetic report' }); }
    if (url.endsWith('/recipients')) return route.fulfill({ headers: CORS_HEADERS, json: [{ label: 'Company: AAA', email: 'company@example.test' }, { label: 'Branch: AAA1', email: 'branch@example.test' }] });
    if (url.endsWith('/send')) {
      calls.sends.push(body);
      const share = { id: 'share', token: 'synthetic-token', tenantId: 'tenant-a', year: 2026, month: 10, recipientEmail: body.email, language: body.selection.language, expiresUtc: new Date(Date.now() + 30 * 86400000).toISOString(), isRevoked: false, emailError: 'Email could not be sent. Check email configuration and retry.' };
      shares = [share]; return route.fulfill({ headers: CORS_HEADERS, json: share });
    }
    if (url.endsWith('/retry-email')) { calls.retry++; shares[0] = { ...shares[0], emailError: null, emailSentUtc: new Date().toISOString() }; return route.fulfill({ headers: CORS_HEADERS, json: shares[0] }); }
    if (method === 'DELETE') { shares[0].isRevoked = true; return route.fulfill({ status: 204, headers: CORS_HEADERS }); }
    return route.fulfill({ headers: CORS_HEADERS, json: shares });
  });
  await page.goto('/admin/quality-cycle/');
  await page.getByText('Reports', { exact: true }).click();
  return calls;
}

async function selectReportShifts(page: Page) {
  await page.getByRole('textbox', { name: 'Shifts', exact: true }).click();
  await page.getByRole('option', { name: 'Weekday 2 hours', exact: true }).click();
  await page.getByRole('option', { name: 'Biweekly 3 hours', exact: true }).click();
  await page.keyboard.press('Escape');
}

for (const [language, title, acknowledge, done] of [
  ['en', 'Cleaning Quality Report', 'Acknowledge receipt', 'Receipt acknowledged'],
  ['fi', 'Siivouksen laaturaportti', 'Kuittaa vastaanotto', 'Vastaanotto kuitattu'],
  ['sv', 'Kvalitetsrapport för städning', 'Bekräfta mottagandet', 'Mottagandet bekräftat'],
]) {
  test(`public report ${language} requires explicit acknowledgment and survives reconnect`, async ({ page }, testInfo) => {
    let posts = 0; let acknowledgedUtc: string | null = null;
    await page.route(/\/api\/v1\/public\/tenant-a\/quality-cycle\/reports\/reader-token(?:\/acknowledge)?$/, route => {
      if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
      if (route.request().method() === 'POST') { posts++; acknowledgedUtc = '2026-10-17T12:00:00Z'; }
      return route.fulfill({ headers: CORS_HEADERS, json: { language, expiresUtc: '2099-11-01T00:00:00Z', acknowledgedUtc,
        year: 2026, month: 10, periods: [{ from: '2026-10-01', to: '2026-10-31' }], shifts: [{ companyName: 'AAA', branchName: 'AAA1', shiftName: 'Weekdays', scheduledOccurrences: 22, completedOccurrences: 20, scheduledHours: 44, actualHours: 43.5 }] } });
    });
    await page.goto('/quality-report/?tenant=tenant-a&token=reader-token');
    await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: '43.50', exact: true })).toBeVisible(); expect(posts).toBe(0);
    await expect(page.locator('main')).toHaveAttribute('lang', language);
    await page.screenshot({ path: testInfo.outputPath(`recipient-${language}.png`), fullPage: true });
    await page.getByRole('button', { name: acknowledge, exact: true }).click();
    await expect(page.getByRole('status')).toContainText(done); expect(posts).toBe(1);
    await page.reload(); await expect(page.getByRole('status')).toContainText(done);
    await expect(page.getByRole('button', { name: acknowledge, exact: true })).toHaveCount(0); expect(posts).toBe(1);
  });
}

test('unavailable public report offers retry without acknowledgment controls', async ({ page }) => {
  await page.route(/\/api\/v1\/public\/tenant-a\/quality-cycle\/reports\/expired-token$/, route => route.fulfill({ headers: CORS_HEADERS, status: 404, json: {} }));
  await page.goto('/quality-report/?tenant=tenant-a&token=expired-token');
  await expect(page.getByRole('alert').filter({ hasText: 'unavailable, expired or revoked' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Acknowledge receipt' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry', exact: true })).toBeVisible();
});

test('month multi-select keeps selections across years without treating intervening months as selected', async ({ page }) => {
  const calls = await setupReportWorkspace(page); await selectReportShifts(page);
  const year = page.getByRole('textbox', { name: 'Month selection year' });
  await year.fill(''); await year.pressSequentially('2027');
  await page.getByRole('textbox', { name: 'Months', exact: true }).click();
  await page.getByRole('option', { name: 'January 2027', exact: true }).click();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Load report', exact: true }).click();
  await expect.poll(() => calls.previews.length).toBe(1);
  expect(calls.previews[0].months).toEqual(['2026-10', '2027-01']);
  expect(calls.previews[0].from).toBeUndefined();
});

test.describe('Admin Panel - Quality Cycle Page', () => {
  test('navigation, tab switching, template creation, edit and preserving deactivation', async ({ page }) => {
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

      if (method === 'POST' && /\/(deactivate|reactivate)$/.test(url)) {
        templates[0].isActive = url.endsWith('/reactivate');
        return route.fulfill({ status: 204, headers: CORS_HEADERS });
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

    // Deactivate while retaining the original template and its edited metadata.
    await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
    await expect(page.getByText('Updated Office Checklist', { exact: true })).not.toBeVisible();
    await page.getByRole('tab', { name: /^Inactive/ }).click();
    await expect(page.getByText('Updated Office Checklist', { exact: true })).toBeVisible();
    expect(templates).toHaveLength(1);
    expect(deleteConfirmed).toBe(false);
  });

  test('selected shift export uses the loaded period and selected PDF language', async ({ page }) => {
    const calls = await setupReportWorkspace(page);
    await selectReportShifts(page);
    await page.getByRole('button', { name: 'Load report', exact: true }).click();
    await expect(page.getByRole('cell', { name: 'Weekday 2 hours', exact: true })).toBeVisible();
    await page.getByRole('textbox', { name: 'Report language', exact: true }).click();
    await page.getByRole('option', { name: 'Finnish', exact: true }).click();
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export all selected shifts', exact: true }).click();
    expect((await downloadPromise).suggestedFilename()).toBe('Cleaning_Quality_Report_fi.pdf');
    expect(calls.exports[0]).toMatchObject({ shiftIds: ['weekday', 'biweekly'], language: 'fi' });
    const row = page.getByRole('row').filter({ hasText: 'Weekday 2 hours' });
    const single = page.waitForEvent('download'); await row.getByRole('button', { name: 'Export shift', exact: true }).click(); await single;
    expect(calls.exports[1].shiftIds).toEqual(['weekday']);
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

test('selected reports show summary columns, support a custom inclusive range and clear stale results', async ({ page }, testInfo) => {
  const calls = await setupReportWorkspace(page);
  await expect(page.getByRole('button', { name: 'Load report', exact: true })).toBeDisabled();
  await selectReportShifts(page);
  await page.getByRole('button', { name: 'Load report', exact: true }).click();
  const weekdays = page.getByRole('row').filter({ hasText: 'Weekday 2 hours' });
  await expect(weekdays).toContainText('44.00'); await expect(weekdays).toContainText('43.50');
  await expect(page.getByRole('columnheader')).toHaveText(['Company', 'Branch', 'Shift', 'Scheduled occurrences', 'Completed occurrences', 'Scheduled hours', 'Actual hours', 'Actions']);
  await expect(page.getByRole('row').filter({ hasText: 'Biweekly 3 hours' })).toContainText('Not available');
  await page.screenshot({ path: testInfo.outputPath('report-workspace.png'), fullPage: true });
  await page.getByText('Date range', { exact: true }).click();
  await expect(page.getByRole('button', { name: 'Export all selected shifts' })).toHaveCount(0);
  await page.getByLabel('From date', { exact: true }).fill('2026-10-02');
  await page.getByLabel('To date (inclusive)', { exact: true }).fill('2026-10-02');
  await page.getByRole('button', { name: 'Load report', exact: true }).click();
  await expect.poll(() => calls.previews.length).toBe(2);
  expect(calls.previews[1]).toMatchObject({ from: '2026-10-02', to: '2026-10-02' });
  expect(calls.previews[1].months).toBeUndefined();
});
test('legacy photo submission requires a note and retains evidence after server failure', async ({ page }) => {
  const form = { id: 'photo-form', shiftName: 'Daily work', employeeName: 'Cleaner', shiftOccurrenceUtc: '2026-01-01T08:00:00Z',
    token: 'photo-token', items: [{ itemText: 'Corridor', isChecked: false }], photoUrls: [], cleanerNotes: '', isSubmitted: false };
  let requests = 0; let submitted: any;
  await page.route(/\/api\/v1\/public\/[^/]+\/quality-cycle\/photo-token$/, route => route.fulfill({ json: form, headers: CORS_HEADERS }));
  await page.route(/\/api\/v1\/public\/[^/]+\/quality-cycle\/photo-token\/submit$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    requests++; submitted = route.request().postDataJSON();
    return route.fulfill(requests === 1 ? { status: 503, json: {}, headers: CORS_HEADERS } : { json: { ...form, ...submitted, isSubmitted: true }, headers: CORS_HEADERS });
  });
  await page.goto('/quality-cycle/?token=photo-token');
  await page.getByLabel('Add shift photos').setInputFiles({ name: 'corridor.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64') });
  await expect(page.getByRole('button', { name: 'Remove photo' })).toHaveCount(1);
  await page.getByRole('textbox', { name: 'Notes / Comments' }).fill('General shift note');
  const note = page.getByRole('textbox', { name: 'Photo 1 note' });
  await expect(note).toHaveAttribute('aria-required', 'true');
  await note.fill('  '); await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('alert', { name: 'Form error' })).toHaveText('Add a separate note for each photo.');
  await expect(note).toBeFocused(); expect(requests).toBe(0);
  await note.fill('Corridor entrance cleaned.'); await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('alert', { name: 'Form error' })).toHaveText('Failed to submit form. Please try again.');
  await expect(note).toHaveValue('Corridor entrance cleaned.');
  await expect(page.getByRole('button', { name: 'Remove photo' })).toHaveCount(1);
  await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('heading', { name: 'Thank You!' })).toBeVisible();
  expect(submitted.photos).toHaveLength(1); expect(submitted.photos[0].note).toBe('Corridor entrance cleaned.'); expect(submitted.cleanerNotes).toBe('General shift note');
});

test('token form preserves distinct notes for multiple photos', async ({ page }) => {
  const form = { id: 'multi-form', shiftOccurrenceUtc: '2026-01-01T08:00:00Z', items: [], photoUrls: [], isSubmitted: false };
  let submitted: any; let requests = 0;
  await page.route(/\/quality-cycle\/multi-token$/, route => route.fulfill({ json: form, headers: CORS_HEADERS }));
  await page.route(/\/quality-cycle\/multi-token\/submit$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    requests++; submitted = route.request().postDataJSON(); return route.fulfill({ json: { ...form, ...submitted, isSubmitted: true }, headers: CORS_HEADERS });
  });
  await page.goto('/quality-cycle/?token=multi-token');
  const photo = { name: 'corridor.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64') };
  await page.getByLabel('Add shift photos').setInputFiles([photo, { ...photo, name: 'gym.png' }]);
  await page.getByRole('textbox', { name: 'Photo 1 note' }).fill('Corridor cleaned');
  await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('textbox', { name: 'Photo 2 note' })).toBeFocused(); expect(requests).toBe(0);
  await page.getByRole('textbox', { name: 'Photo 2 note' }).fill('Gym cleaned');
  await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('heading', { name: 'Thank You!' })).toBeVisible();
  expect(submitted.photos.map((p: any) => p.note)).toEqual(['Corridor cleaned', 'Gym cleaned']); expect(submitted.cleanerNotes).toBe('');
});

test('legacy form permits notes without images and removing the final image makes notes optional', async ({ page }) => {
  const form = { id: 'notes-form', shiftName: 'Daily work', employeeName: 'Cleaner', shiftOccurrenceUtc: '2026-01-01T08:00:00Z',
    token: 'notes-token', items: [], photoUrls: [], cleanerNotes: '', isSubmitted: false };
  let submitted: any;
  await page.route(/\/api\/v1\/public\/[^/]+\/quality-cycle\/notes-token$/, route => route.fulfill({ json: form, headers: CORS_HEADERS }));
  await page.route(/\/api\/v1\/public\/[^/]+\/quality-cycle\/notes-token\/submit$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS_HEADERS });
    submitted = route.request().postDataJSON(); return route.fulfill({ json: { ...form, ...submitted, isSubmitted: true }, headers: CORS_HEADERS });
  });
  await page.goto('/quality-cycle/?token=notes-token');
  await page.getByLabel('Add shift photos').setInputFiles({ name: 'corridor.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64') });
  await page.getByRole('button', { name: 'Remove photo' }).click();
  await expect(page.getByRole('textbox', { name: 'Notes / Comments' })).toHaveAttribute('aria-required', 'false');
  await page.getByRole('textbox', { name: 'Notes / Comments' }).fill('Gym unavailable today.');
  await page.getByRole('button', { name: 'Submit Quality Cycle Form' }).click();
  await expect(page.getByRole('heading', { name: 'Thank You!' })).toBeVisible();
  expect(submitted.photos).toEqual([]); expect(submitted.cleanerNotes).toBe('Gym unavailable today.');
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

test('tenant admin emails a selected snapshot, retries failed email and revokes the link', async ({ page }) => {
  const calls = await setupReportWorkspace(page); await selectReportShifts(page);
  await page.getByRole('button', { name: 'Load report', exact: true }).click();
  await page.getByRole('button', { name: 'Share all selected shifts' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: 'Recipient source' }).click();
  await page.getByRole('option', { name: /Branch: AAA1/ }).click();
  await expect(dialog.getByLabel('Recipient email')).toHaveValue('branch@example.test');
  await dialog.getByRole('textbox', { name: 'Recipient source' }).click();
  await page.getByRole('option', { name: 'Other email address', exact: true }).click();
  await dialog.getByLabel('Recipient email').fill('other@example.test');
  await dialog.getByRole('textbox', { name: 'PDF and email language' }).click();
  await page.getByRole('option', { name: 'Swedish', exact: true }).click();
  await dialog.getByRole('button', { name: 'Send report', exact: true }).click();
  await expect(page.getByText('Email failed', { exact: true })).toBeVisible();
  expect(calls.sends[0]).toMatchObject({ email: 'other@example.test', selection: { shiftIds: ['weekday', 'biweekly'], language: 'sv' } });
  await page.getByRole('button', { name: 'Retry email', exact: true }).click();
  await expect(page.getByText('Email sent', { exact: true })).toBeVisible(); expect(calls.retry).toBe(1);
  await expect(page.getByRole('link', { name: 'Open report', exact: true })).toHaveAttribute('href', /quality-report\/\?tenant=tenant-a&token=synthetic-token$/);
  await page.getByRole('button', { name: 'Revoke', exact: true }).click(); await expect(page.getByText('Inactive', { exact: true })).toBeVisible();
});
test('admin can attach a checklist to an existing shift', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const shift = { id: 'existing-shift', companyId: 'company-a', branchId: 'branch-a', name: 'Existing shift', schedule: { type: 0, dailyStart: '08:00', dailyEnd: '10:00' }, isActive: true, qualityCycleTemplateId: null };
  let saved: any;
  await page.route(/\/api\/v1\/admin\/shifts(?:\?.*)?$/, route => route.fulfill({ status: 200, headers: CORS_HEADERS, json: [shift] }));
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
