import { type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

export const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,POST,OPTIONS', 'Access-Control-Allow-Headers': 'Content-Type,Authorization' };
export const photo = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64');
export const company = { id: 'company-one', name: 'Partner One', isActive: true, isPartnerEnabled: true };
export const usage = ['Keys', 'Bedsheets'].map((name, index) => ({ id: `usage-${index}`, employeeId: 'cleaner', employeeName: 'Private Cleaner', locationId: 'store', locationName: 'Main store', itemId: `item-${index}`, itemName: name, quantity: index + 1, unit: 'pcs', note: 'Collected at reception', takenUtc: '2026-11-09T08:00:00Z', photoIds: [`photo-${index}`], photosPurged: false, version: 'v1' }));
export function acknowledgment(row = usage[0]) { return { ...row, batchId: 'batch-one', companyId: company.id, companyName: company.name, message: 'Please confirm collection', status: 'Pending', responseNote: '', respondedByName: null, respondedUtc: null, createdUtc: '2026-11-09T09:00:00Z' }; }

export async function setup(page: Page, options: { pending?: boolean; rows?: any[]; failSend?: boolean; failResponse?: boolean; failLookup?: boolean; expiredPhoto?: boolean } = {}) {
  await setupAdminMocks(page);
  const state = { requests: [] as any[], responses: [] as any[], rows: options.rows ?? (options.pending ? [acknowledgment()] as any[] : []), searches: [] as string[] };
  await page.route(/\/api\/v1\/admin\/employees(?:\?.*)?$/, route => route.fulfill({ headers, json: [] }));
  await page.route(/\/api\/v1\/(?:admin\/inventory|partner\/)/, route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ headers });
    if (path.includes('/photos/')) return options.expiredPhoto ? route.fulfill({ headers, status: 404 }) : route.fulfill({ headers, contentType: 'image/png', body: photo });
    if (path.endsWith('/acknowledgments/partners')) { state.searches.push(url.searchParams.get('search') ?? ''); return route.fulfill({ headers, json: [company] }); }
    if (path.endsWith('/inventory/acknowledgments')) {
      if (request.method() === 'GET') return route.fulfill({ headers, status: options.failLookup ? 500 : 200, json: options.failLookup ? { detail: 'Unavailable' } : state.rows });
      const body = request.postDataJSON(); state.requests.push(body);
      if (options.failSend) return route.fulfill({ headers, status: 409, json: { detail: 'An acknowledgment has already been requested for a selected entry. Refresh the log.' } });
      state.rows = usage.filter(row => body.usageIds.includes(row.id)).map(row => ({ ...acknowledgment(row), ...body }));
      return route.fulfill({ headers, json: state.rows });
    }
    if (path.endsWith('/inventory/usage')) return route.fulfill({ headers, json: usage });
    if (path.endsWith('/inventory-acknowledgments')) {
      const history = url.searchParams.get('history') === 'true';
      return route.fulfill({ headers, json: { items: state.rows.filter(row => history ? row.status !== 'Pending' : row.status === 'Pending'), pendingCount: state.rows.filter(row => row.status === 'Pending').length, hasMore: false } });
    }
    if (path.endsWith('/response')) {
      const body = request.postDataJSON(); state.responses.push(body);
      if (options.failResponse) return route.fulfill({ headers, status: 409, json: { detail: 'Already changed.' } });
      const id = path.split('/').at(-2);
      state.rows = state.rows.map(row => row.id === id ? { ...row, status: body.status, responseNote: body.note, respondedByName: 'Test Owner', respondedUtc: '2026-11-09T10:00:00Z' } : row);
      return route.fulfill({ headers, json: state.rows.find(row => row.id === id) });
    }
    if (path.endsWith('/partner/me')) return route.fulfill({ headers, json: { realm: 'readysetsiivous', person: { id: 'owner', role: 'partner', isActive: true }, company, timeZoneId: 'Europe/Helsinki' } });
    return route.fulfill({ headers, json: path.endsWith('/settings') ? { photoLabels: [], account: {} } : [] });
  });
  return state;
}

export async function enterPartner(page: Page) {
  await page.addInitScript(() => { (window as any).__MOCK_ROLES__ = ['partner']; });
  await page.goto('/partner/');
}

