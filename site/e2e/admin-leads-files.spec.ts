import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);

test('selected lead shows saved files and accepts one more upload', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const lead = {
    id: 'lead-1',
    name: 'File Prospect',
    emails: [] as string[],
    contactPersons: [] as { name: string }[],
    isActive: true,
    attachments: [
      { id: 'file-1', fileName: 'site.png', contentType: 'image/png', sizeBytes: 1200 },
    ],
  };

  await page.route('**/api/v1/admin/leads**', async (route) => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    const path = url.pathname;

    if (method === 'GET' && path.endsWith('/leads')) {
      return route.fulfill({ headers, json: { items: [lead], total: 1 } });
    }

    const download = path.match(/\/leads\/([^/]+)\/attachments\/([^/]+)$/);
    if (download && method === 'GET') {
      return route.fulfill({ headers: { ...headers, 'Content-Type': 'image/png' }, body: png });
    }

    if (method === 'POST' && path.endsWith('/attachments')) {
      if (lead.attachments.length >= 3) {
        return route.fulfill({ status: 400, headers, json: { errors: { File: ['A lead can have at most 3 files.'] } } });
      }
      lead.attachments.push({
        id: 'file-2', fileName: 'notes.pdf', contentType: 'application/pdf', sizeBytes: 4096,
      });
      return route.fulfill({ headers, json: lead.attachments[1] });
    }

    return route.fulfill({ status: 404, headers, json: {} });
  });

  await page.goto('/admin/leads/');
  await expect(page.getByRole('cell', { name: '1', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'View', exact: true }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('site.png', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('img', { name: 'site.png' })).toBeVisible();

  await dialog.locator('input[type="file"]').setInputFiles({
    name: 'notes.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4'),
  });

  await expect(dialog.getByText('notes.pdf', { exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: '2', exact: true })).toBeVisible();
});
