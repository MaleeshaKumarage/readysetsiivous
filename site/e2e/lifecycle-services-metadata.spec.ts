import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' };

// A service with metadata the editor does not show: extra translation, image, icon, extra info, order and featured flag.
const stored = {
  id: 'srv-meta', slug: 'deep-cleaning', category: 'cleaning',
  name: { values: { fi: 'Syväsiivous', en: 'Deep cleaning', sv: 'Djupstädning', de: 'Tiefenreinigung' } },
  description: { values: { fi: 'Perusteellinen', en: 'Thorough', de: 'Gründlich' } },
  additionalInfo: { values: { fi: 'Lisätiedot' } },
  icon: 'Broom', imageUrl: '/uploads/deep.webp', currency: 'EUR',
  durationMinutes: 180, priceNet: 120, vatRatePercent: 25.5,
  isActive: true, isFeatured: true, sortOrder: 7,
};

const creates: any[] = [];

async function mockServices(page: Page) {
  const writes: any[] = [];
  creates.length = 0;
  await setupAdminMocks(page, { authenticated: true });
  await page.route('**/api/v1/admin/services**', async route => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
    if (method === 'GET') return route.fulfill({ status: 200, json: [stored], headers: CORS });
    if (method === 'PUT') { writes.push(route.request().postDataJSON()); return route.fulfill({ status: 200, json: true, headers: CORS }); }
    if (method === 'POST') { creates.push(route.request().postDataJSON()); return route.fulfill({ status: 200, json: true, headers: CORS }); }
    return route.continue();
  });
  return writes;
}

test('editing the price keeps every stored translation, image, icon, order and featured flag', async ({ page }) => {
  const writes = await mockServices(page);
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
  await page.getByLabel('Price (€)').fill('150');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect.poll(() => writes.length).toBe(1);
  const { fields } = writes[0];
  expect(fields.priceNet).toBe(150);
  expect(fields.name).toEqual({ fi: 'Syväsiivous', en: 'Deep cleaning', sv: 'Djupstädning', de: 'Tiefenreinigung' });
  expect(fields.description).toEqual({ fi: 'Perusteellinen', en: 'Thorough', de: 'Gründlich' });
  expect(fields.additionalInfo).toEqual({ fi: 'Lisätiedot' });
  expect(fields.icon).toBe('Broom');
  expect(fields.imageUrl).toBe('/uploads/deep.webp');
  expect(fields.currency).toBe('EUR');
  expect(fields.sortOrder).toBe(7);
  expect(fields.isFeatured).toBe(true);
  expect(fields.durationMinutes).toBe(180);
});

test('a zero VAT rate is saved as zero, not the 25.5% default', async ({ page }) => {
  const writes = await mockServices(page);
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
  await page.getByLabel('VAT %').fill('0');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0].fields.vatRatePercent).toBe(0);
});

test('clearing the English name removes only that translation', async ({ page }) => {
  const writes = await mockServices(page);
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
  await page.getByLabel('Name (English)').fill('');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0].fields.name).toEqual({ fi: 'Syväsiivous', sv: 'Djupstädning', de: 'Tiefenreinigung' });
});

test('adding a service sends the create defaults', async ({ page }) => {
  await mockServices(page);
  await page.goto('/admin/services/');
  await page.getByRole('button', { name: 'Add service', exact: true }).click();
  await page.getByLabel('Slug').fill('window-cleaning');
  await page.getByLabel('Name (Finnish)').fill('Ikkunanpesu');
  await page.getByRole('button', { name: 'Create service', exact: true }).click();
  await expect.poll(() => creates.length).toBe(1);
  const fields = creates[0].fields;
  expect(fields.slug).toBe('window-cleaning');
  expect(fields.name).toEqual({ fi: 'Ikkunanpesu' });
  expect(fields.vatRatePercent).toBe(25.5);
  expect(fields.durationMinutes).toBe(60);
  expect(fields.priceNet).toBe(0);
  expect(fields.icon).toBe('Sparkles');
  expect(fields.currency).toBe('EUR');
  expect(fields.sortOrder).toBe(0);
  expect(fields.isFeatured).toBe(false);
  expect(fields.additionalInfo).toBeNull();
});
