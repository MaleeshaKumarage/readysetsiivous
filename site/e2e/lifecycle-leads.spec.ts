import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
const fields = () => ({ branches: undefined as { id: string; name: string; street: string; postalCode: string; city: string; country: string; contactPhone: string; contactEmail: string }[] | undefined, name: 'Prospect Oy', businessId: '1234567-8', contactName: 'Anna', contactPhone: '123', notes: 'Company note', description: 'Cleaning requirements', emails: ['one@example.test', 'two@example.test'], primaryEmail: 'one@example.test', status: 'New', branchName: 'Office', street: 'Street 1', postalCode: '00100', city: 'Helsinki', country: 'FI', branchPhone: '456', branchEmail: 'office@example.test' });
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jGZkAAAAASUVORK5CYII=', 'base64');
async function setup(page: Page) {
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/manager/leads**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: route.request().url().endsWith('/settings') ? { timeZoneId: 'Europe/Helsinki' } : { items: [], total: 0 } }));
  const state = { lead: { id: 'lead-1', version: 'v1', fields: fields(), isActive: true, status: 'New', imageCount: 0, companyId: null as string | null, branchId: null as string | null, convertedUtc: null as string | null, updatedUtc: '2026-10-21T09:00:00Z' }, images: [] as { id: string; extension: string }[], customerAvailable: false, conversionError: '', saveError: '', uploadError: false, imageError: false, detailError: false, listError: false, requests: [] as { method: string; path: string; body: unknown }[], queries: [] as URLSearchParams[], total: 1 };
  const detail = () => ({ lead: state.lead, images: state.images, customerAvailable: state.customerAvailable });
  await page.route('**/api/v1/admin/leads**', async route => {
    const request = route.request(); const url = new URL(request.url()); const path = url.pathname; const method = request.method();
    const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (method === 'GET' && /\/images\//.test(path)) return state.imageError ? route.fulfill({ headers, status: 503 }) : route.fulfill({ headers, contentType: 'image/png', body: png });
    if (method === 'GET' && path.endsWith('/leads')) {
      state.queries.push(url.searchParams);
      if (state.listError) return route.fulfill({ headers, status: 503, json: { detail: 'Lead list unavailable' } });
      const filter = url.searchParams.get('status');
      const matches = filter === 'Converted' ? !!state.lead.companyId : filter === 'Inactive' ? !state.lead.isActive : !state.lead.companyId && state.lead.isActive;
      return route.fulfill({ headers, json: { items: matches ? [state.lead] : [], total: state.total } });
    }
    if (method === 'GET') return route.fulfill({ headers, status: state.detailError ? 503 : 200, json: state.detailError ? { detail: 'Lead detail unavailable' } : detail() });
    const body = path.endsWith('/images') && method === 'POST' ? request.postData() : request.postDataJSON(); state.requests.push({ method, path, body });
    if (method === 'PUT') {
      if (state.saveError) return route.fulfill({ headers, status: 409, json: { detail: state.saveError } });
      state.lead.fields = body.fields; state.lead.status = body.fields.status;
    } else if (path.endsWith('/convert')) {
      if (state.conversionError) return route.fulfill({ headers, status: 409, json: { detail: state.conversionError } });
      state.lead.companyId = 'customer-1'; state.lead.branchId = 'branch-1'; state.lead.status = 'Converted'; state.lead.convertedUtc = '2026-10-21T10:00:00Z'; state.customerAvailable = true;
    } else if (path.endsWith('/deactivate')) state.lead.isActive = false;
    else if (path.endsWith('/activate')) state.lead.isActive = true;
    else if (method === 'POST' && path.endsWith('/images')) {
      expect(request.headers()['content-type']).toContain('multipart/form-data; boundary=');
      if (state.uploadError) return route.fulfill({ headers, status: 500, json: { detail: 'Upload failed' } });
      state.images.push({ id: `photo-${state.images.length + 1}`, extension: '.png' }); state.lead.imageCount = state.images.length;
    } else if (method === 'DELETE' && path.includes('/images/')) { state.images = state.images.filter(image => !path.endsWith('/' + image.id)); state.lead.imageCount = state.images.length; }
    else if (method === 'POST') state.lead.fields = body;
    else if (method === 'DELETE') { state.total = 0; return route.fulfill({ headers, status: 204 }); }
    state.lead.version = `v${state.requests.length + 1}`;
    return route.fulfill({ headers, status: method === 'POST' && path.endsWith('/leads') ? 201 : 200, json: detail() });
  });
  return state;
}
async function open(page: Page) { await page.goto('/admin/leads/'); await page.getByRole('button', { name: 'View lead', exact: true }).click(); await expect(page.getByRole('textbox', { name: 'Company name' })).toHaveValue('Prospect Oy'); }

test('lead create and edit preserves multiple emails, primary contact and description', async ({ page }) => {
  const state = await setup(page); await page.goto('/admin/leads/'); await page.getByRole('button', { name: 'New lead' }).click();
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Company name is required' })).toBeVisible(); expect(state.requests).toHaveLength(0);
  await page.getByRole('textbox', { name: 'Company name' }).fill('New prospect');
  await page.getByRole('textbox', { name: 'Contact email addresses' }).fill('first@example.test\nsecond@example.test');
  await page.getByRole('textbox', { name: 'Primary company email' }).click(); await page.getByRole('option', { name: 'second@example.test' }).click();
  await page.getByRole('textbox', { name: 'Description', exact: true }).fill('Need gym and corridor cleaning');
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect(page.getByRole('status')).toContainText('Lead saved.');
  expect(state.lead.fields.emails).toEqual(['first@example.test', 'second@example.test']); expect(state.lead.fields.primaryEmail).toBe('second@example.test');
  await page.getByRole('textbox', { name: 'Company notes' }).fill('Follow up');
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect.poll(() => state.lead.fields.notes).toBe('Follow up');
  expect(state.lead.fields.description).toBe('Need gym and corridor cleaning');
});
test('lead images reject a fourth, upload privately, remove and retry failure', async ({ page }) => {
  const state = await setup(page); await open(page);
  await expect(page.getByRole('tab', { name: 'View images', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('button', { name: 'Remove image', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Add images', { exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: 'Manage images', exact: true }).click();
  await page.getByLabel('Add images', { exact: true }).setInputFiles({ name: 'bad.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg/>') }); await expect(page.getByRole('alert').filter({ hasText: 'Choose JPEG' })).toBeVisible();
  const image = (i: number) => ({ name: `${i}.png`, mimeType: 'image/png', buffer: png });
  await page.getByLabel('Add images', { exact: true }).setInputFiles([1, 2, 3, 4].map(image));
  await expect(page.getByRole('alert').filter({ hasText: 'at most 3' })).toBeVisible(); expect(state.requests).toHaveLength(0);
  state.uploadError = true; await page.getByLabel('Add images', { exact: true }).setInputFiles(image(1)); await expect(page.getByRole('alert').filter({ hasText: 'Upload failed' })).toBeVisible();
  state.uploadError = false; await page.getByLabel('Add images', { exact: true }).setInputFiles([1, 2, 3].map(image));
  await expect(page.getByRole('status').filter({ hasText: 'Images uploaded.' })).toBeVisible(); await expect(page.getByAltText('Lead image')).toHaveCount(3);
  await expect(page.getByLabel('Add images', { exact: true })).toBeDisabled();
  await page.getByRole('tab', { name: 'View images', exact: true }).click(); await expect(page.getByRole('button', { name: 'Remove image', exact: true })).toHaveCount(0); await expect(page.getByLabel('Add images', { exact: true })).toHaveCount(0);
  await page.getByRole('tab', { name: 'Manage images', exact: true }).click();
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Remove image', exact: true }).first().click();
  await expect(page.getByAltText('Lead image')).toHaveCount(2); await expect(page.getByLabel('Add images', { exact: true })).toBeEnabled();
});
test('lead conversion retains snapshot, handles conflict, supports cancel and links to customer', async ({ page }) => {
  const state = await setup(page); await open(page);
  await page.getByRole('button', { name: 'Convert to customer', exact: true }).click();
  const confirmation = page.getByRole('dialog', { name: 'Convert lead to customer', exact: true });
  await confirmation.getByRole('button', { name: 'Cancel', exact: true }).click(); expect(state.requests).toHaveLength(0);
  state.conversionError = 'A customer with this business ID already exists.';
  await page.getByRole('button', { name: 'Convert to customer', exact: true }).click(); await confirmation.getByRole('button', { name: 'Confirm conversion' }).click();
  await expect(confirmation.getByRole('alert')).toContainText('already exists');
  state.conversionError = ''; await confirmation.getByRole('button', { name: 'Confirm conversion' }).click();
  await expect(page.getByRole('link', { name: 'Open customer' })).toHaveAttribute('href', '/admin/companies/?company=customer-1');
  await expect(page.getByRole('textbox', { name: 'Company name' })).toHaveAttribute('readonly', '');
  await expect(page.getByRole('textbox', { name: 'Contact email addresses' })).toHaveValue('one@example.test\ntwo@example.test');
  await expect(page.getByRole('button', { name: 'Convert to customer', exact: true })).toHaveCount(0);
});
test('lead stale save preserves input and inactive records can edit reactivate and delete', async ({ page }) => {
  const state = await setup(page); state.lead.isActive = false; await page.goto('/admin/leads/');
  await page.getByRole('textbox', { name: 'Lead filter' }).click(); await page.getByRole('option', { name: 'Inactive', exact: true }).click();
  await page.getByRole('button', { name: 'View lead', exact: true }).click();
  await page.getByRole('textbox', { name: 'Description', exact: true }).fill('Unsaved change'); state.saveError = 'This lead changed. Refresh before retrying.';
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'This lead changed' })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toHaveValue('Unsaved change'); await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toBeDisabled();
  state.saveError = ''; await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect(page.getByRole('button', { name: 'Reactivate', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Reactivate', exact: true }).click(); await expect(page.getByRole('button', { name: 'Deactivate', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Deactivate', exact: true }).click(); await expect(page.getByRole('button', { name: 'Delete lead', exact: true })).toBeVisible();
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('button', { name: 'Delete lead', exact: true }).click(); expect(state.requests.filter(x => x.method === 'DELETE')).toHaveLength(0);
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Delete lead', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('lead list and detail failures retry without showing a new-lead form and pagination sends correct filters', async ({ page }) => {
  const state = await setup(page); state.listError = true; await page.goto('/admin/leads/'); await expect(page.getByRole('alert').filter({ hasText: 'Lead list unavailable' })).toBeVisible();
  state.listError = false; state.total = 26; await page.getByRole('button', { name: 'Retry loading leads' }).click(); await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect.poll(() => state.queries.at(-1)?.get('skip')).toBe('25');
  await page.getByRole('button', { name: 'Previous', exact: true }).click(); await expect.poll(() => state.queries.at(-1)?.get('skip')).toBe('0');
  await page.getByRole('textbox', { name: 'Search leads' }).fill('one@example.test'); await expect.poll(() => state.queries.at(-1)?.get('skip')).toBe('0'); await expect.poll(() => state.queries.at(-1)?.get('search')).toBe('one@example.test');
  state.detailError = true; await page.getByRole('button', { name: 'View lead', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Lead detail unavailable' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save lead', exact: true })).toHaveCount(0); state.detailError = false; await page.getByRole('button', { name: 'Retry loading lead' }).click(); await expect(page.getByRole('textbox', { name: 'Company name' })).toHaveValue('Prospect Oy');
});

test('converted lead retains private images and handles deleted customer and image retry', async ({ page }) => {
  const state = await setup(page); state.lead.companyId = 'deleted-customer'; state.lead.branchId = 'deleted-branch'; state.lead.status = 'Converted'; state.images = [{ id: 'photo-1', extension: '.png' }]; state.lead.imageCount = 1; state.imageError = true;
  await page.goto('/admin/leads/'); await page.getByRole('textbox', { name: 'Lead filter' }).click(); await page.getByRole('option', { name: 'Converted', exact: true }).click(); await page.getByRole('button', { name: 'View lead', exact: true }).click();
  await expect(page.getByText('The converted customer or branch is no longer available.')).toBeVisible(); await expect(page.getByRole('link', { name: 'Open customer' })).toHaveCount(0);
  await expect(page.getByRole('alert').filter({ hasText: 'Image could not be loaded.' })).toBeVisible(); state.imageError = false; await page.getByRole('button', { name: 'Retry image' }).click(); await expect(page.getByAltText('Lead image')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open lead image' })).toHaveAttribute('href', /^blob:/); await expect(page.getByRole('button', { name: 'Remove image', exact: true })).toHaveCount(0);
});
test('customer deep link expands the converted company and branch including inactive customer', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const company = { id: 'customer-1', businessId: '123', name: 'Converted customer', isActive: false };
  await page.route('**/api/v1/admin/companies**', route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: new URL(route.request().url()).pathname.endsWith('/customer-1') ? { company, branches: [{ id: 'branch-1', companyId: company.id, name: 'Converted office', isActive: true, city: 'Helsinki' }] } : { items: [company], total: 1 } }));
  await page.goto('/admin/companies/?company=customer-1'); await expect(page.getByText('Converted office · Helsinki', { exact: true })).toBeVisible(); await expect(page.getByRole('row').filter({ hasText: 'Converted customer' }).getByRole('button', { name: 'Reactivate', exact: true })).toBeVisible();
});

test('lead reload confirms discarded edits and status persists after saving', async ({ page }) => {
  const state = await setup(page); await open(page);
  const description = page.getByRole('textbox', { name: 'Description', exact: true });
  await description.fill('Unsaved draft');
  page.once('dialog', dialog => dialog.dismiss());
  await page.getByRole('button', { name: 'Reload lead', exact: true }).click();
  await expect(description).toHaveValue('Unsaved draft');
  state.lead.fields.description = 'Server update';
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Reload lead', exact: true }).click();
  await expect(description).toHaveValue('Server update');
  await page.getByRole('textbox', { name: 'Lead status', exact: true }).click();
  await page.getByRole('option', { name: 'Contacted', exact: true }).click();
  await page.getByRole('button', { name: 'Save lead', exact: true }).click();
  await expect.poll(() => state.lead.status).toBe('Contacted');
  await page.getByRole('button', { name: 'Reload lead', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Lead status', exact: true })).toHaveValue('Contacted');
});


test('leads start without branches and admins explicitly add edit remove and page through multiple branches', async ({ page }) => {
  const state = await setup(page); await page.goto('/admin/leads/'); await page.getByRole('button', { name: 'New lead', exact: true }).click();
  await expect(page.getByText('No branches added. A lead can be saved and converted without a branch.')).toBeVisible();
  await page.getByRole('textbox', { name: 'Company name', exact: true }).fill('Many offices');
  await page.getByRole('button', { name: 'Add branch', exact: true }).click();
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Enter a name for each added branch' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Branch 1 name', exact: true }).fill('Office one');
  await page.getByRole('button', { name: 'Add branch', exact: true }).click(); await page.getByRole('textbox', { name: 'Branch 2 name', exact: true }).fill('Office two');
  await page.getByRole('textbox', { name: 'Branch 2 email', exact: true }).fill('two@example.test');
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect.poll(() => state.lead.fields.branches?.length).toBe(2);
  await page.getByRole('button', { name: 'Reload lead', exact: true }).click(); await expect(page.getByRole('textbox', { name: 'Branch 2 email', exact: true })).toHaveValue('two@example.test');
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('button', { name: 'Remove branch 2', exact: true }).click(); await expect(page.getByRole('textbox', { name: 'Branch 2 name', exact: true })).toHaveCount(1);
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Remove branch 2', exact: true }).click(); await expect(page.getByRole('textbox', { name: 'Branch 2 name', exact: true })).toHaveCount(0);
  for (let i = 2; i <= 6; i++) { await page.getByRole('button', { name: 'Add branch', exact: true }).click(); await page.getByRole('textbox', { name: `Branch ${i} name`, exact: true }).fill(`Office ${i}`); }
  await expect(page.getByRole('button', { name: 'Next branches', exact: true })).toBeDisabled(); await page.getByRole('button', { name: 'Previous branches', exact: true }).click(); await expect(page.getByRole('textbox', { name: 'Branch 1 name', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Next branches', exact: true }).click(); await expect(page.getByRole('textbox', { name: 'Branch 6 name', exact: true })).toHaveValue('Office 6');
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect.poll(() => state.lead.fields.branches?.length).toBe(6);
});
test('zero branch conversion review and saved lead retain an explicit empty branch list', async ({ page }) => {
  const state = await setup(page); await page.goto('/admin/leads/'); await page.getByRole('button', { name: 'New lead', exact: true }).click();
  await page.getByRole('textbox', { name: 'Company name', exact: true }).fill('No branch company'); await page.getByRole('textbox', { name: 'Business ID (required for conversion)' }).fill('ZERO-1');
  await page.getByRole('button', { name: 'Save lead', exact: true }).click(); await expect.poll(() => state.lead.fields.branches?.length).toBe(0);
  await page.getByRole('button', { name: 'Convert to customer', exact: true }).click(); const confirm = page.getByRole('dialog', { name: 'Convert lead to customer', exact: true }); await expect(confirm.getByText('0 branches will be created.')).toBeVisible();
  await confirm.getByRole('button', { name: 'Confirm conversion', exact: true }).click(); await expect(page.getByRole('link', { name: 'Open customer' })).toBeVisible(); await expect(page.getByRole('tab', { name: 'Manage images', exact: true })).toHaveCount(0);
});
