import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': '*' };
const PDF_REFUSED = 'The PDF could not be read. Upload a PDF that opens normally, then create the agreement again.';

test('a refused agreement creation explains the fix as a toast and in the dialog, and keeps the entered title', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers, json: { items: [{ id: 'company', name: 'Fixture Oy', isActive: true }], total: 1 } }));
  await page.route('**/api/v1/admin/agreements**', route => {
    const path = new URL(route.request().url()).pathname;
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers, status: 200 });
    if (path.endsWith('/legacy-repairs')) return route.fulfill({ headers, json: { agreements: [], companies: [] } });
    if (route.request().method() === 'POST') return route.fulfill({ headers, status: 400, json: { title: 'Invalid agreement', detail: PDF_REFUSED } });
    return route.fulfill({ headers, json: [] });
  });
  await page.goto('/admin/agreements/');
  await page.getByRole('button', { name: 'New agreement', exact: true }).click();
  const creation = page.getByRole('dialog');
  await creation.getByRole('textbox', { name: /^Company/ }).click();
  await page.getByRole('option', { name: 'Fixture Oy' }).click();
  await creation.getByRole('textbox', { name: /^Title/ }).fill('Spring cleaning');
  await creation.getByPlaceholder('Name', { exact: true }).fill('Anna Signer');
  await creation.getByPlaceholder('Email', { exact: true }).fill('anna@example.test');
  await creation.locator('input[type="file"]').setInputFiles({ name: 'agreement.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 synthetic') });
  await creation.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByText('Agreement not created').first()).toBeVisible();
  await expect(creation.getByText(PDF_REFUSED).first()).toBeVisible();
  await expect(creation.getByRole('textbox', { name: /^Title/ })).toHaveValue('Spring cleaning');
});
