import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const branch = { id: 'branch-one', companyId: 'company-one', name: 'Lake apartment', street: 'Lake road 1', city: 'Tampere', isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
// The server names each missing field separately; the page shows them together.
const missing = { BranchId: ['Choose the property for this cleaning.'], FromDate: ['Choose the first day of the cleaning.'], ToDate: ['Choose the last day of the cleaning.'] };

async function setupPartner(page: Page, language: 'en' | 'fi') {
  await setupAdminMocks(page);
  await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = [role]; }, 'partner');
  await page.addInitScript(lang => localStorage.setItem('readyset:partner-language', lang), language);
  await page.route(/\/api\/v1\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    if (path === '/me') return route.fulfill({ headers, json: { realm: 'readysetsiivous', person: { id: 'owner', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: 'company-one', name: 'Owner company', isActive: true, isPartnerEnabled: true }, timeZoneId: 'Europe/Helsinki' } });
    if (req.method() === 'POST' && path === '/requests') return route.fulfill({ status: 400, headers, json: { title: 'Validation failed', status: 400, errors: missing } });
    return route.fulfill({ headers, json: path === '/branches' ? [branch] : [] });
  });
}

async function submitEmptyDates(page: Page, labels: { request: string; from: string; to: string; save: string }) {
  await page.goto('/partner/');
  await page.getByRole('article', { name: 'Lake apartment', exact: true }).getByRole('button', { name: labels.request, exact: true }).click();
  await page.getByLabel(labels.from, { exact: true }).fill('2026-12-01'); await page.getByLabel(labels.to, { exact: true }).fill('2026-12-02');
  await page.getByRole('button', { name: labels.save, exact: true }).click();
}

test('a request the server rejects names every missing field in English', async ({ page }) => {
  await setupPartner(page, 'en');
  await submitEmptyDates(page, { request: 'Request cleaning', from: 'From date', to: 'To date', save: 'Save cleaning request' });
  await expect(page.locator('p.partner-alert-error')).toContainText('Choose the property for this cleaning. Choose the first day of the cleaning. Choose the last day of the cleaning.');
});

test('a request the server rejects names every missing field in Finnish', async ({ page }) => {
  await setupPartner(page, 'fi');
  await submitEmptyDates(page, { request: 'Pyydä siivous', from: 'Alkupäivä', to: 'Loppupäivä', save: 'Tallenna siivouspyyntö' });
  await expect(page.locator('p.partner-alert-error')).toContainText('Valitse siivouksen kohde. Valitse siivouksen ensimmäinen päivä. Valitse siivouksen viimeinen päivä.');
});
