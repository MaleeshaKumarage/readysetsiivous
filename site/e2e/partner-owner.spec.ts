import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import type { Branch } from '../src/lib/adminApi';
import type { CleanerOffer, CleaningRequest, PartnerChange, PartnerIdentity, PartnerShift } from '../src/lib/partnerApi';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };
const companyId = '11111111-1111-4111-8111-111111111111';
const priced: Branch = { id: '22222222-2222-4222-8222-222222222222', companyId, name: 'Lake apartment', street: 'Lake road 1', postalCode: '33100', city: 'Tampere', country: 'FI', isActive: true, roomCount: 2, bedCount: 3, priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, pricingReady: true };
const pending: CleaningRequest = { id: '33333333-3333-4333-8333-333333333333', version: 5, companyId, branchId: priced.id, branchName: priced.name, fromDate: '2026-12-01', toDate: '2026-12-02', exactStartLocalTime: null, notes: 'Original cleaning instructions', priceNet: 70, vatRatePercent: 25.5, estimatedDurationMinutes: 120, status: 'Requested', firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null };
interface Write { path: string; method: string; body: Record<string, unknown> | null }
async function owner(page: Page, options: { enabled?: boolean; pending?: boolean; branches?: Branch[]; shifts?: PartnerShift[] } = {}) {
  await setupAdminMocks(page); await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['partner']; });
  const identity: PartnerIdentity = { realm: 'readysetsiivous', person: { id: 'owner-profile', firstName: 'Test', lastName: 'Owner', role: 'partner', isActive: true }, company: { id: companyId, name: 'Owner company', isActive: true, isPartnerEnabled: options.enabled ?? true }, timeZoneId: 'Europe/Helsinki' };
  let branches: Branch[] = options.branches ?? [priced], requests: CleaningRequest[] = options.pending ? [pending] : [], changes: PartnerChange[] = [];
  const writes: Write[] = [], reads: string[] = [];
  await page.route(/\/api\/v1\/partner\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/partner')[1];
    if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') { reads.push(path); return route.fulfill({ headers, json: path === '/me' ? identity : path === '/branches' ? branches : path === '/requests' ? requests : path === '/change-requests' ? changes : path === '/shifts' ? options.shifts ?? [] : [] }); }
    const body = req.postData() ? req.postDataJSON() as Record<string, unknown> : null; writes.push({ path, method: req.method(), body });
    if (path === '/branches' && body) { const added: Branch = { id: 'new-property', companyId, isActive: true, name: String(body.name), street: String(body.street), postalCode: String(body.postalCode), city: String(body.city), country: String(body.country), contactEmail: String(body.contactEmail), contactPhone: String(body.contactPhone), roomCount: body.roomCount === null ? null : Number(body.roomCount), bedCount: body.bedCount === null ? null : Number(body.bedCount), priceNet: null, pricingReady: false }; branches = [...branches, added]; return route.fulfill({ headers, json: added }); }
    if (path.endsWith('/quote-request')) { const id = path.split('/')[2]; branches = branches.map(branch => branch.id === id ? { ...branch, quoteRequestedUtc: '2026-11-01T10:00:00Z' } : branch); return route.fulfill({ headers, json: branches.find(branch => branch.id === id) }); }
    if (path.endsWith('/change-requests') && body) { const change: PartnerChange = { id: 'property-change', companyId, branchId: path.split('/')[2], requestId: null, kind: 'Property', message: String(body.message), createdUtc: '2026-11-01T10:00:00Z', resolvedUtc: null, resolution: null }; changes = [...changes, change]; return route.fulfill({ headers, json: change }); }
    if (path === '/requests' && body) { const added: CleaningRequest = { ...pending, version: 1, fromDate: String(body.fromDate), toDate: String(body.toDate), exactStartLocalTime: body.exactStartLocalTime === null ? null : String(body.exactStartLocalTime), notes: String(body.notes) }; requests = [added]; return route.fulfill({ headers, json: added }); }
    if (path === `/requests/${pending.id}` && body) { requests = [{ ...requests[0], version: requests[0].version + 1, fromDate: String(body.fromDate), toDate: String(body.toDate), exactStartLocalTime: body.exactStartLocalTime === null ? null : String(body.exactStartLocalTime), notes: String(body.notes) }]; return route.fulfill({ headers, json: requests[0] }); }
    if (path === `/requests/${pending.id}/cancel`) { requests = [{ ...requests[0], version: requests[0].version + 1, status: 'Cancelled' }]; return route.fulfill({ headers, json: requests[0] }); }
    return route.fulfill({ status: 404, headers });
  });
  return { writes, reads };
}

test('company shifts show localized empty state, dates and statuses while preserving entered names', async ({ page }) => {
  await owner(page);
  await page.goto('/partner/');
  await page.getByRole('tab', { name: 'Company shifts', exact: true }).click();
  await expect(page.getByText('No company shifts.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Suomi', exact: true }).click();
  await expect(page.getByText('Yrityksellä ei ole vuoroja.', { exact: true })).toBeVisible();
  await owner(page, { shifts: [
    { id: 'active-shift', branchId: priced.id, branchName: priced.name, name: 'Guest checkout clean', isActive: true, schedule: { type: 0 }, validFrom: '2026-12-01', validUntil: '2026-12-15' },
    { id: 'inactive-shift', branchId: priced.id, branchName: priced.name, name: 'Archived apartment clean', isActive: false, schedule: { type: 0 }, validFrom: null, validUntil: null },
  ] });
  await page.reload();
  await page.getByRole('tab', { name: 'Yrityksen vuorot', exact: true }).click();
  const active = page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Guest checkout clean', exact: true }) });
  const inactive = page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Archived apartment clean', exact: true }) });
  await expect(active).toContainText(priced.name);
  await expect(active).toContainText('Aktiivinen');
  await expect(active).toContainText('1.12.2026 – 15.12.2026');
  await expect(inactive).toContainText('Ei aktiivinen');
  await expect(inactive).toContainText('Ei aloituspäivää – Toistaiseksi');
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(active).toContainText('Active');
  await expect(active).toContainText('2026-12-01 – 2026-12-15');
  await expect(inactive).toContainText('Inactive');
  await expect(inactive).toContainText('No start date – Ongoing');
});

test('owner creates a property, requests its quote and sends property changes without setting company or prices', async ({ page }) => {
  const state = await owner(page); await page.goto('/partner/');
  await page.getByRole('button', { name: 'Add property', exact: true }).click();
  const form = page.getByRole('region', { name: 'Add property', exact: true });
  await form.getByRole('button', { name: 'Save property', exact: true }).click(); expect(state.writes).toHaveLength(0);
  await form.getByLabel('Property name', { exact: true }).fill('Abandoned draft'); await form.getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(form).toHaveCount(0); expect(state.writes).toHaveLength(0);
  await page.getByRole('button', { name: 'Add property', exact: true }).click();
  for (const [label, value] of [['Property name', 'Forest apartment'], ['Street', 'Forest road 9'], ['Postal code', '90100'], ['City', 'Oulu'], ['Country', 'FI'], ['Contact phone', '+358401234567'], ['Contact email', 'property@example.invalid'], ['Rooms (optional)', '3'], ['Beds (optional)', '4']]) await form.getByLabel(label, { exact: true }).fill(value);
  await form.getByRole('button', { name: 'Save property', exact: true }).click();
  const property = page.getByRole('article', { name: 'Forest apartment', exact: true }); await expect(property).toBeVisible();
  expect(state.writes[0]).toEqual({ path: '/branches', method: 'POST', body: { name: 'Forest apartment', street: 'Forest road 9', postalCode: '90100', city: 'Oulu', country: 'FI', contactPhone: '+358401234567', contactEmail: 'property@example.invalid', roomCount: 3, bedCount: 4 } });
  await expect(property.getByRole('button', { name: 'Request cleaning', exact: true })).toBeDisabled();
  await property.getByRole('button', { name: 'Request quote', exact: true }).click(); await expect(property.getByText('Quote requested — awaiting staff pricing')).toBeVisible(); await expect(property.getByRole('button', { name: 'Request quote', exact: true })).toBeDisabled();
  expect(state.writes[1]).toEqual({ path: '/branches/new-property/quote-request', method: 'POST', body: null });
  await expect(property.getByText('Quote requested', { exact: true })).toHaveClass(/text-red-800/);
  await property.getByRole('button', { name: 'Request property changes', exact: true }).click(); await page.getByLabel('Describe the requested change').fill('Please add one sofa bed.'); await page.getByRole('button', { name: 'Send change request', exact: true }).click();
  await expect(page.getByText('Change request sent to staff.', { exact: true })).toBeVisible(); expect(state.writes[2]).toEqual({ path: '/branches/new-property/change-requests', method: 'POST', body: { message: 'Please add one sofa bed.' } });
  await page.getByRole('tab', { name: 'Change requests', exact: true }).click(); await expect(page.getByText('Please add one sofa bed.', { exact: true })).toBeVisible(); await expect(page.getByText('Awaiting staff response', { exact: true })).toBeVisible();
});

test('owner creates a fixed appointment with one date and immutable pricing supplied by the server', async ({ page }) => {
  const state = await owner(page); await page.goto('/partner/'); const property = page.getByRole('article', { name: priced.name, exact: true });
  await property.getByRole('button', { name: 'Request cleaning', exact: true }).click(); const form = page.getByRole('region', { name: 'Cleaning request form' });
  await form.getByLabel('From date', { exact: true }).fill('2026-12-15'); await form.getByLabel('Fixed appointment on this date').check(); await expect(form.getByLabel('To date', { exact: true })).toHaveCount(0);
  await form.getByLabel('Exact start time').fill('10:30'); await form.getByLabel('Cleaning instructions').fill('After guest checkout.'); await form.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(page.getByText('2026-12-15 – 2026-12-15 at 10:30', { exact: true })).toBeVisible();
  expect(state.writes[0]).toMatchObject({ path: '/requests', method: 'POST', body: { branchId: priced.id, fromDate: '2026-12-15', toDate: '2026-12-15', exactStartLocalTime: '10:30', notes: 'After guest checkout.' } });
  expect(state.writes[0].body?.clientRequestId).toMatch(/^[a-f0-9-]{36}$/); expect(state.writes[0].body).not.toHaveProperty('companyId'); expect(state.writes[0].body).not.toHaveProperty('priceNet');
});

test('Request cleaning visibly opens and focuses the form even below a long property list', async ({ page }) => {
  await owner(page, { branches: [priced, ...Array.from({ length: 20 }, (_, index) => ({ ...priced, id: `other-${index}`, name: `Other apartment ${index}` }))] });
  await page.goto('/partner/');
  await page.getByRole('article', { name: priced.name, exact: true }).getByRole('button', { name: 'Request cleaning', exact: true }).click();
  const form = page.getByRole('region', { name: 'Cleaning request form', exact: true });
  await expect(form).toBeInViewport();
  await expect(form).toBeFocused();
});

test('FI/EN switching preserves a draft, creates and cancels a Finnish request and stays local to the portal', async ({ page }) => {
  const state = await owner(page); await page.goto('/partner/');
  await page.getByRole('button', { name: 'Suomi', exact: true }).click();
  await expect(page.getByRole('main')).toHaveAttribute('lang', 'fi');
  await expect(page.getByRole('heading', { name: 'Kumppaniportaali', exact: true })).toBeVisible();
  await expect(page.getByText(/70,00\s*€/).first()).toBeVisible();
  await page.getByRole('button', { name: 'Pyydä siivous', exact: true }).click();
  let form = page.getByRole('region', { name: 'Siivouspyynnön lomake', exact: true });
  await form.getByLabel('Alkupäivä', { exact: true }).fill('2026-12-10');
  await form.getByLabel('Loppupäivä', { exact: true }).fill('2026-12-11');
  await form.getByLabel('Siivouksen ohjeet').fill('Keep this entered text unchanged.');
  await page.getByRole('button', { name: 'English', exact: true }).click();
  const english = page.getByRole('region', { name: 'Cleaning request form', exact: true });
  await expect(english.getByLabel('From date', { exact: true })).toHaveValue('2026-12-10');
  await expect(english.getByLabel('Cleaning instructions')).toHaveValue('Keep this entered text unchanged.');
  await page.getByRole('button', { name: 'Suomi', exact: true }).click();
  form = page.getByRole('region', { name: 'Siivouspyynnön lomake', exact: true });
  await form.getByRole('button', { name: 'Tallenna siivouspyyntö', exact: true }).click();
  await expect(page.getByText('Siivouspyyntö tallennettu. Henkilökunta järjestää siivoojan.', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Lake apartment · Pyydetty', exact: true })).toBeVisible();
  await expect(page.getByText('Keep this entered text unchanged.', { exact: true })).toBeVisible();
  let confirmation = '';
  page.once('dialog', async dialog => { confirmation = dialog.message(); await dialog.accept(); });
  await page.getByRole('button', { name: 'Peruuta pyyntö', exact: true }).click();
  expect(confirmation).toBe('Haluatko peruuttaa tämän siivouspyynnön?');
  await expect(page.getByRole('heading', { name: 'Lake apartment · Peruttu', exact: true })).toBeVisible();
  expect(state.writes.filter(write => write.path === '/requests')).toHaveLength(1);
  await page.reload();
  await expect(page.getByRole('main')).toHaveAttribute('lang', 'fi');
  await expect(page.getByRole('button', { name: 'Suomi', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('link', { name: 'Tili', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Account ready', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Open partner portal', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Kumppaniportaali', exact: true })).toBeVisible();
});

test('Finnish property, quote, change and validation messages preserve entered content', async ({ page }) => {
  const state = await owner(page);
  await page.route(/\/api\/v1\/partner\/requests$/, route => route.request().method() === 'POST'
    ? route.fulfill({ status: 409, headers, json: { detail: 'Request a quote before booking this property.' } }) : route.fallback());
  await page.goto('/partner/'); await page.getByRole('button', { name: 'Suomi', exact: true }).click();
  await page.getByRole('button', { name: 'Lisää kohde', exact: true }).click();
  const propertyForm = page.getByRole('region', { name: 'Lisää kohde', exact: true });
  await propertyForm.getByLabel('Kohteen nimi', { exact: true }).fill('Mökki Oulussa');
  await propertyForm.getByLabel('Kaupunki', { exact: true }).fill('Oulu');
  await propertyForm.getByLabel('Huoneita (valinnainen)', { exact: true }).fill('3');
  await propertyForm.getByRole('button', { name: 'Tallenna kohde', exact: true }).click();
  const property = page.getByRole('article', { name: 'Mökki Oulussa', exact: true });
  await property.getByRole('button', { name: 'Pyydä tarjous', exact: true }).click();
  await expect(property.getByText('Tarjous pyydetty', { exact: true })).toHaveClass(/text-red-800/);
  await property.getByRole('button', { name: 'Pyydä kohteen muutosta', exact: true }).click();
  await page.getByLabel('Kuvaile toivottu muutos').fill('Add one sofa bed, please.');
  await page.getByRole('button', { name: 'Lähetä muutospyyntö', exact: true }).click();
  await expect(page.getByText('Muutospyyntö lähetetty henkilökunnalle.', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Muutospyynnöt', exact: true }).click();
  await expect(page.getByText('Add one sofa bed, please.', { exact: true })).toBeVisible();
  await expect(page.getByText('Odottaa henkilökunnan vastausta', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Kohteet', exact: true }).click();
  await page.getByRole('article', { name: priced.name, exact: true }).getByRole('button', { name: 'Pyydä siivous', exact: true }).click();
  const cleaning = page.getByRole('region', { name: 'Siivouspyynnön lomake', exact: true });
  await cleaning.getByRole('button', { name: 'Tallenna siivouspyyntö', exact: true }).click();
  await expect(cleaning.getByRole('alert')).toHaveText('Täytä pakolliset kentät ennen tallentamista.');
  await cleaning.getByLabel('Alkupäivä', { exact: true }).fill('2026-12-10');
  await cleaning.getByLabel('Loppupäivä', { exact: true }).fill('2026-12-09');
  await cleaning.getByRole('button', { name: 'Tallenna siivouspyyntö', exact: true }).click();
  await expect(cleaning.getByRole('alert')).toHaveText('Loppupäivän on oltava sama tai myöhempi kuin alkupäivä.');
  await cleaning.getByLabel('Loppupäivä', { exact: true }).fill('2026-12-11');
  await cleaning.getByRole('button', { name: 'Tallenna siivouspyyntö', exact: true }).click();
  await expect(cleaning.getByRole('alert')).toHaveText('Pyydä tarjous ennen siivouksen tilaamista tähän kohteeseen.');
  await expect(cleaning.getByLabel('Alkupäivä', { exact: true })).toHaveValue('2026-12-10');
  expect(state.writes.find(write => write.path === '/branches')?.body).toMatchObject({ name: 'Mökki Oulussa', city: 'Oulu', roomCount: 3 });
});

test.describe('Finnish browser defaults', () => {
  test.use({ locale: 'fi-FI' });
  test('portal defaults to Finnish, honours a saved choice and remains light and keyboard-friendly at 320px', async ({ page }, testInfo) => {
    await owner(page); await page.goto('/partner/');
    await expect(page.getByRole('main')).toHaveAttribute('lang', 'fi');
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await page.reload(); await expect(page.getByRole('main')).toHaveAttribute('lang', 'en');
    await page.getByRole('button', { name: 'Suomi', exact: true }).click();
    await page.evaluate(() => document.documentElement.classList.add('dark'));
    const property = page.getByRole('article', { name: priced.name, exact: true }).locator('.partner-surface');
    await expect(property).toHaveCSS('background-color', 'rgb(255, 255, 255)');
    await expect(page.getByRole('img', { name: 'ReadySetSiivous', exact: true })).toBeVisible();
    const properties = page.getByRole('tab', { name: 'Kohteet', exact: true });
    await properties.focus(); await properties.press('ArrowRight');
    await expect(page.getByRole('tab', { name: 'Siivouspyynnöt', exact: true })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('tab', { name: 'Siivouspyynnöt', exact: true }).press('Home');
    await expect(properties).toHaveAttribute('aria-selected', 'true');
    await properties.press('End');
    const changesTab = page.getByRole('tab', { name: 'Muutospyynnöt', exact: true });
    await expect(changesTab).toHaveAttribute('aria-selected', 'true');
    await expect(changesTab).toBeFocused();
    await changesTab.press('ArrowLeft');
    const shiftsTab = page.getByRole('tab', { name: 'Yrityksen vuorot', exact: true });
    await expect(shiftsTab).toHaveAttribute('aria-selected', 'true');
    await expect(shiftsTab).toBeFocused();
    await shiftsTab.press('Home');
    await page.screenshot({ path: testInfo.outputPath('partner-portal-fi.png'), fullPage: true });
    await page.setViewportSize({ width: 320, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('partner-portal-fi-320.png'), fullPage: true });
    await page.getByRole('button', { name: 'Pyydä siivous', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Siivouspyynnön lomake', exact: true })).toBeInViewport();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('partner-request-fi-320.png'), fullPage: true });
    expect(await page.evaluate(() => document.documentElement.classList.contains('dark'))).toBe(true);
  });
});

test('portal shows its real logo and a failed cleaning save preserves fields and retry identity', async ({ page }, testInfo) => {
  const state = await owner(page);
  const attempts: Record<string, unknown>[] = [];
  await page.route(/\/api\/v1\/partner\/requests$/, route => {
    if (route.request().method() !== 'POST') return route.fallback();
    attempts.push(route.request().postDataJSON());
    if (attempts.length === 1) return route.fulfill({ status: 503, headers, json: { detail: 'Cleaning could not be saved. Please retry.' } });
    return route.fallback();
  });
  await page.goto('/partner/');
  const logo = page.getByRole('img', { name: 'ReadySetSiivous', exact: true });
  await expect(logo).toBeVisible();
  await expect.poll(() => logo.evaluate(image => (image as HTMLImageElement).complete && (image as HTMLImageElement).naturalWidth > 0)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('partner-portal-logo.png'), fullPage: true });
  await page.getByRole('button', { name: 'Request cleaning', exact: true }).click();
  const form = page.getByRole('region', { name: 'Cleaning request form' });
  await form.getByLabel('From date', { exact: true }).fill('2026-12-10');
  await form.getByLabel('To date', { exact: true }).fill('2026-12-11');
  await form.getByLabel('Cleaning instructions').fill('Please keep these instructions.');
  await form.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(form.getByRole('alert')).toHaveText('Cleaning could not be saved. Please retry.');
  await expect(form.getByLabel('From date', { exact: true })).toHaveValue('2026-12-10');
  await expect(form.getByLabel('To date', { exact: true })).toHaveValue('2026-12-11');
  await expect(form.getByLabel('Cleaning instructions')).toHaveValue('Please keep these instructions.');
  await form.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(form).toHaveCount(0);
  await expect(page.getByRole('tab', { name: 'Cleaning requests', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('2026-12-10 – 2026-12-11', { exact: true })).toBeVisible();
  expect(attempts).toHaveLength(2);
  expect(attempts[0].clientRequestId).toBe(attempts[1].clientRequestId);
  expect(state.writes.filter(write => write.path === '/requests')).toHaveLength(1);
});

test('unpriced or inactive properties cannot request cleaning and invalid dates never submit', async ({ page }) => {
  const state = await owner(page, { branches: [priced, { ...priced, id: 'unpriced', name: 'Unpriced apartment', pricingReady: false }, { ...priced, id: 'inactive', name: 'Inactive apartment', isActive: false }] });
  await page.goto('/partner/');
  for (const name of ['Unpriced apartment', 'Inactive apartment'])
    await expect(page.getByRole('article', { name, exact: true }).getByRole('button', { name: 'Request cleaning', exact: true })).toBeDisabled();
  await page.getByRole('article', { name: priced.name, exact: true }).getByRole('button', { name: 'Request cleaning', exact: true }).click();
  const form = page.getByRole('region', { name: 'Cleaning request form' });
  await form.getByRole('button', { name: 'Save cleaning request' }).click();
  expect(state.writes).toEqual([]);
  await form.getByLabel('From date', { exact: true }).fill('2026-12-10');
  await form.getByLabel('To date', { exact: true }).fill('2026-12-09');
  await form.getByRole('button', { name: 'Save cleaning request' }).click();
  expect(await form.getByLabel('To date', { exact: true }).evaluate(input => (input as HTMLInputElement).validity.rangeUnderflow)).toBe(true);
  expect(state.writes).toEqual([]);
  await form.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(form).toHaveCount(0);
});

test('owner edits a pending request with its revision and cancels the refreshed revision only after confirmation', async ({ page }) => {
  const state = await owner(page, { pending: true }); await page.goto('/partner/'); await page.getByRole('tab', { name: 'Cleaning requests', exact: true }).click();
  await page.getByRole('button', { name: 'Edit request', exact: true }).click(); const form = page.getByRole('region', { name: 'Cleaning request form' });
  await expect(form.getByLabel('From date', { exact: true })).toHaveValue('2026-12-01'); await expect(form.getByLabel('Cleaning instructions')).toHaveValue(pending.notes!);
  await form.getByLabel('From date', { exact: true }).fill('2026-12-05'); await form.getByLabel('To date', { exact: true }).fill('2026-12-06'); await form.getByLabel('Cleaning instructions').fill('Updated checkout instructions.'); await form.getByRole('button', { name: 'Save cleaning request', exact: true }).click();
  await expect(page.getByText('2026-12-05 – 2026-12-06', { exact: true })).toBeVisible(); expect(state.writes[0]).toEqual({ path: `/requests/${pending.id}`, method: 'PUT', body: { version: 5, fromDate: '2026-12-05', toDate: '2026-12-06', exactStartLocalTime: null, notes: 'Updated checkout instructions.' } });
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('button', { name: 'Cancel request', exact: true }).click(); expect(state.writes).toHaveLength(1);
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Cancel request', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Lake apartment · Cancelled', exact: true })).toBeVisible(); await expect(page.getByRole('button', { name: 'Edit request', exact: true })).toHaveCount(0);
  expect(state.writes[1]).toEqual({ path: `/requests/${pending.id}/cancel`, method: 'POST', body: { version: 6 } });
});

test('enabled partner account links to the verified company portal', async ({ page }) => {
  const state = await owner(page); await page.goto('/account/?tenant=readysetsiivous'); await expect(page.getByRole('heading', { name: 'Account ready', exact: true })).toBeVisible();
  const portal = page.getByRole('link', { name: 'Open partner portal', exact: true }); await expect(portal).toHaveAttribute('href', '/partner/'); await expect(page.getByRole('link', { name: 'My shifts', exact: true })).toHaveCount(0);
  expect(state.reads).toEqual(['/me']); await portal.click(); await expect(page).toHaveURL(/\/partner\/$/); await expect(page.getByRole('article', { name: priced.name, exact: true })).toBeVisible(); expect(state.reads).toContain('/branches');
});

test('disabled company account cannot show a portal link or fetch company data', async ({ page }) => {
  const state = await owner(page, { enabled: false }); await page.goto('/account/?tenant=readysetsiivous'); await expect(page.getByText('Partner access is unavailable. Contact your tenant administrator.', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Open partner portal', exact: true })).toHaveCount(0); await expect(page.getByRole('heading', { name: 'Account ready', exact: true })).toHaveCount(0); expect(state.reads).toEqual(['/me']);
  await page.goto('/partner/'); await expect(page.getByText('Access denied.', { exact: false })).toBeVisible(); expect(state.reads).toEqual(['/me', '/me']);
});

test('manager navigation loads partner operations through manager routes only and can price a quote', async ({ page }) => {
  await setupAdminMocks(page); await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['manager']; });
  await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: ['manager'] })], employee: { id: 'manager', firstName: 'Tenant', lastName: 'Manager', role: 'manager', isActive: true } } }));
  const adminCalls: string[] = [], managerReads: string[] = [], writes: Write[] = []; let requestLoads = 0;
  let quotedBranch: Branch = { ...priced, pricingReady: false, quoteRequestedUtc: '2026-11-01T10:00:00Z' };
  await page.route(/\/api\/v1\/admin\//, route => { adminCalls.push(route.request().url()); return route.fulfill({ status: 403, headers }); });
  await page.route(/\/api\/v1\/manager\//, route => {
    const req = route.request(), path = new URL(req.url()).pathname.split('/manager')[1]; if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') {
      managerReads.push(path);
      if (path === '/partner/requests' && ++requestLoads === 1) return route.fulfill({ status: 503, headers, json: { detail: 'Partner requests temporarily unavailable.' } });
      const json = path === '/companies' ? { items: [{ id: companyId, name: 'Owner company', isActive: true }], total: 1 } : path === '/employees' ? [{ id: 'cleaner', firstName: 'Test', lastName: 'Cleaner', isActive: true }] : path === '/time-zone' ? { timeZoneId: 'Europe/Helsinki' } : path === '/shifts' ? [] : path === '/partner/requests' ? [pending] : path === '/partner/branches' ? [quotedBranch] : path === '/partner/quotes' ? (quotedBranch.pricingReady ? [] : [quotedBranch]) : [];
      return route.fulfill({ headers, json });
    }
    const body = req.postDataJSON() as Record<string, unknown>; writes.push({ path, method: req.method(), body }); quotedBranch = { ...quotedBranch, ...body, pricingReady: true }; return route.fulfill({ headers, json: quotedBranch });
  });
  await page.goto('/manager/'); const link = page.getByRole('link', { name: 'Partner requests', exact: true }); await expect(link).toHaveAttribute('href', '/manager/partner-requests/'); await link.click();
  await expect(page.getByText('Partner requests temporarily unavailable.', { exact: true })).toBeVisible(); await page.getByRole('button', { name: 'Refresh partner operations', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Partner operations', exact: true })).toBeVisible(); await expect(page.getByRole('heading', { name: 'Lake apartment · Requested', exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page.getByText('Quote requested', { exact: true })).toHaveClass(/text-red-800/);
  await page.getByRole('tab', { name: 'Quote requests', exact: true }).click(); await page.getByRole('button', { name: 'Set cleaning price', exact: true }).click(); await page.getByLabel('Fixed cleaning price excl. VAT (€)').fill('95'); await page.getByRole('button', { name: 'Save pricing', exact: true }).click(); await expect(page.getByText('Pricing saved.', { exact: true })).toBeVisible();
  expect(writes).toEqual([{ path: `/partner/branches/${priced.id}/pricing`, method: 'PUT', body: { priceNet: 95, vatRatePercent: 25.5, estimatedDurationMinutes: 120 } }]);
  await page.getByRole('tab', { name: 'Properties', exact: true }).click();
  await expect(page.getByText('Quote requested', { exact: true })).toHaveCount(0);
  for (const path of ['/employees', '/companies', '/partner/requests', '/partner/quotes', '/partner/branches', '/partner/change-requests']) expect(managerReads).toContain(path);
  expect(adminCalls).toEqual([]);
});

test('owner load failure is distinct from an empty company and explicit refresh recovers', async ({ page }) => {
  const state = await owner(page); let loads = 0;
  await page.route(/\/api\/v1\/partner\/branches$/, route => {
    if (route.request().method() === 'OPTIONS') return route.fulfill({ headers });
    loads++;
    return route.fulfill(loads === 1 ? { status: 503, headers, json: { detail: 'Properties are temporarily unavailable.' } } : { headers, json: [priced] });
  });
  await page.goto('/partner/'); await expect(page.getByText('Properties are temporarily unavailable.', { exact: true })).toBeVisible(); await expect(page.getByText('No properties yet. Add a property and request a quote.', { exact: true })).toHaveCount(0);
  expect(loads).toBe(1); expect(state.writes).toHaveLength(0);
  await page.getByRole('button', { name: 'Refresh partner data', exact: true }).click();
  await expect(page.getByRole('article', { name: priced.name, exact: true })).toBeVisible(); await expect(page.getByText('Properties are temporarily unavailable.', { exact: true })).toHaveCount(0); expect(loads).toBe(2); expect(state.writes).toHaveLength(0);
});

test('cleaner refreshes a failed offer read and a stale preplanned acceptance without automatic write retries', async ({ page }) => {
  await setupAdminMocks(page); await page.addInitScript(() => { (window as unknown as { __MOCK_ROLES__: string[] }).__MOCK_ROLES__ = ['employee']; });
  await page.route(/\/api\/v1\/me\/work-preferences$/, route => route.fulfill({ headers, json: { autoAcceptWork: false } }));
  let loads = 0, accepted = false;
  const decisions: { path: string; method: string; body: Record<string, unknown> }[] = [];
  const offer: CleanerOffer = { id: 'planned-offer', requestId: pending.id, version: 5, branchName: priced.name, street: priced.street ?? null, postalCode: priced.postalCode ?? null, city: priced.city ?? null, fromDate: '2026-12-01', toDate: '2026-12-01', exactStartLocalTime: '10:30', plannedStartUtc: '2026-12-01T08:30:00Z', estimatedDurationMinutes: 120, notes: null, timeZoneId: 'Europe/Helsinki' };
  await page.route(/\/api\/v1\/me\/shifts\/calendar(?:\?.*)?$/, route => route.fulfill({ headers, json: { occurrences: [], timeZoneId: 'Europe/Helsinki', fromLocalDate: '2026-12-01', toLocalDate: '2026-12-01' } }));
  await page.route(/\/api\/v1\/me\/partner-offers(?:\/.*)?$/, route => {
    const req = route.request(); if (req.method() === 'OPTIONS') return route.fulfill({ headers });
    if (req.method() === 'GET') { loads++; return route.fulfill(loads === 1 ? { status: 503, headers, json: { detail: 'Offers are temporarily unavailable.' } } : { headers, json: accepted ? [] : [{ ...offer, version: decisions.length ? 6 : 5 }] }); }
    decisions.push({ path: new URL(req.url()).pathname, method: req.method(), body: req.postDataJSON() as Record<string, unknown> });
    if (decisions.length === 1) return route.fulfill({ status: 409, headers, json: { detail: 'Offer changed. Refresh before accepting.' } });
    accepted = true; return route.fulfill({ headers, json: { id: pending.id, version: 7, status: 'Accepted', linkedShiftId: 'linked-shift', plannedStartUtc: offer.plannedStartUtc, plannedEndUtc: '2026-12-01T10:30:00Z' } });
  });
  await page.goto('/me/shifts/'); await expect(page.getByText('Offers are temporarily unavailable.', { exact: true })).toBeVisible(); await expect(page.getByText('No pending cleaning offers.', { exact: true })).toHaveCount(0); expect(decisions).toHaveLength(0);
  await page.getByRole('button', { name: 'Refresh offers', exact: true }).click(); await expect(page.getByText(/^Planned start:/)).toContainText('10:30'); await expect(page.getByLabel('Planned start for Lake apartment', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Accept cleaning', exact: true }).click(); await expect(page.getByText('Offer changed. Refresh before accepting.', { exact: true })).toBeVisible(); expect(decisions).toHaveLength(1);
  await page.getByRole('button', { name: 'Refresh offers', exact: true }).click(); await expect(page.getByText('Offer changed. Refresh before accepting.', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Refresh offers', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Accept cleaning', exact: true }).click(); await expect(page.getByText('No pending cleaning offers.', { exact: true })).toBeVisible();
  expect(decisions).toEqual([{ path: '/api/v1/me/partner-offers/planned-offer/accept', method: 'POST', body: { version: 5, plannedStartLocal: null } }, { path: '/api/v1/me/partner-offers/planned-offer/accept', method: 'POST', body: { version: 6, plannedStartLocal: null } }]);
});
