import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { partnerCopy } from '../src/lib/partnerLocale';
import type { Branch } from '../src/lib/adminApi';
import type { PartnerIdentity } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const branch: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', isActive: true, street: 'Lake road 1', city: 'Tampere', roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' };

// The API reports every missing field at once, as ASP.NET problem details.
const incomplete = { status: 400, title: 'Validation failed', errors: {
  ClientRequestId: ['A request identifier is required.'],
  BranchId: ['Choose the property for this cleaning.'],
  FromDate: ['Choose the first day of the cleaning.'],
  ToDate: ['Choose the last day of the cleaning.'],
} };

async function fixture(page: Page, language: 'en' | 'fi') {
  await setupAdminMocks(page);
  await page.addInitScript(value => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; localStorage.setItem('readyset:partner-language', value); }, language);
  const posts: string[] = [];
  await page.route(/\/api\/v1\/(?:admin|partner)\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'POST' && path === '/api/v1/partner/requests') { posts.push(path); return route.fulfill({ status: 400, headers, json: incomplete }); }
    if (req.method() !== 'GET') return route.fulfill({ status: 409, headers, json: { detail: 'Unexpected mutation.' } });
    const json = path === '/api/v1/partner/me' ? identity
      : path.endsWith('/companies') ? { items: [{ id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: true }], total: 1 }
      : path.endsWith('/branches') || path.endsWith('/quotes') ? [branch]
      : path.endsWith('/tenant') ? { timeZoneId: 'Europe/Helsinki' } : [];
    return route.fulfill({ headers, json });
  });
  return posts;
}

for (const language of ['en', 'fi'] as const) {
  const copy = partnerCopy[language];
  test(`an incomplete cleaning request names every missing field in ${language}`, async ({ page }) => {
    const posts = await fixture(page, language);
    await page.goto('/partner/');
    await page.getByRole('article', { name: branch.name, exact: true }).getByRole('button', { name: copy.requestCleaning, exact: true }).click();
    const dialog = page.getByRole('dialog').filter({ has: page.getByRole('button', { name: copy.saveRequest, exact: true }) });
    await dialog.getByLabel(copy.fromDate, { exact: true }).fill('2026-12-10');
    await dialog.getByLabel(copy.toDate, { exact: true }).fill('2026-12-11');
    await dialog.getByRole('button', { name: copy.saveRequest, exact: true }).click();
    const alert = dialog.getByRole('alert');
    await expect(alert).toHaveText(language === 'en'
      ? 'A request identifier is required. Choose the property for this cleaning. Choose the first day of the cleaning. Choose the last day of the cleaning.'
      : 'Pyynnön tunniste puuttuu. Avaa lomake uudelleen. Valitse siivouksen kohde. Valitse siivouksen ensimmäinen päivä. Valitse siivouksen viimeinen päivä.');
    expect(posts).toHaveLength(1);
  });
}
