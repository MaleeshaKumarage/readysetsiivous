import type { Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

// A Work queue shaped like the design canvas. Times are Europe/Helsinki, UTC+2 in January 2030.
export const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET,PUT,POST,DELETE,OPTIONS', 'Access-Control-Allow-Headers': 'Authorization,Content-Type' };

const employee = (id: string, firstName: string, lastName: string, extra: Record<string, unknown> = {}) =>
  ({ id, firstName, lastName, email: `${id}@example.fi`, phone: '', role: 'employee', isActive: true, ...extra });
export const EMPLOYEES = [
  employee('anna', 'Anna', 'Korhonen', { autoAcceptWork: true }), employee('ville', 'Ville', 'Rantanen'),
  employee('minna', 'Minna', 'Salo'), employee('jari', 'Jari', 'Laine'),
  employee('partner-user', 'Pertti', 'Partner', { role: 'partner' }), employee('gone', 'Old', 'Leaver', { isActive: false }),
];
const COMPANIES = [
  { id: 'nordea', name: 'Nordea branch, Mannerheimintie', isActive: true, isPartnerEnabled: true },
  { id: 'kesko', name: 'Kesko store, Kamppi', isActive: true, isPartnerEnabled: true },
  { id: 'kamppi', name: 'Kamppi Apartments Oy', isActive: true, isPartnerEnabled: true },
  { id: 'aurora', name: 'Aurora clinic', isActive: true, isPartnerEnabled: true },
];
const BRANCHES = [
  { id: 'b-nordea', companyId: 'nordea', name: 'Nordea office', city: 'Helsinki', isActive: true, pricingReady: true, priceNet: 180, vatRatePercent: 25.5, estimatedDurationMinutes: 180 },
  { id: 'b-kesko', companyId: 'kesko', name: 'Kesko store', city: 'Helsinki', isActive: true, pricingReady: true },
  { id: 'b-studio', companyId: 'kamppi', name: 'Kamppi studio 12', city: 'Helsinki', isActive: true, pricingReady: false, quoteRequestedUtc: '2030-01-10T09:00:00Z', roomCount: 2, bedCount: 2 },
  { id: 'b-aurora', companyId: 'aurora', name: 'Aurora clinic', city: 'Espoo', isActive: true, pricingReady: true },
];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const shiftVisit = (fields: Record<string, unknown>): Record<string, any> => ({ originalStartUtc: null, exceptionId: null, extraExceptionId: null, status: null, visitEmployeeId: null,
  rosterVersion: 'roster-1', assignmentVersion: null, isBranchMain: false, cleaningType: null, notes: null, offeredEmployeeId: null, offerRevision: 0, ...fields });
const SHIFTS = [
  { source: 'shift', id: 'occ-lindqvist', reference: 'Lindqvist weekly', customerName: 'Lindqvist household', scheduledLocal: '2030-01-14 08:00', status: 'needs-action',
    stateLabel: 'Overdue · not started', assigneeName: null, href: '/admin/shifts/scheduler/',
    visit: shiftVisit({ shiftId: 'shift-lindqvist', startUtc: '2030-01-14T06:00:00Z', endUtc: '2030-01-14T09:00:00Z', branchName: 'Kallio flat', notes: 'Keys under the mat.' }) },
  { source: 'shift', id: 'occ-virtanen', reference: 'Showroom visit 6', customerName: 'Virtanen Oy', scheduledLocal: '2030-01-20 09:00', status: 'needs-action',
    stateLabel: 'Unassigned', assigneeName: null, href: '/admin/shifts/scheduler/',
    visit: shiftVisit({ shiftId: 'shift-virtanen', startUtc: '2030-01-20T07:00:00Z', endUtc: '2030-01-20T10:00:00Z', branchName: 'Helsinki showroom', cleaningType: 'Deep', status: 4, extraExceptionId: 'extra-6', exceptionId: 'extra-6', isBranchMain: true }) },
];
const BOOKINGS = [
  { source: 'booking', id: 'bk-1042', reference: 'B-1042', customerName: 'Eeva Mäkinen', scheduledLocal: '2030-01-16 08:00', status: 'New', stateLabel: 'New booking',
    assigneeName: null, href: '/admin/work-queue/?booking=bk-1042', service: 'Kotisiivous', durationMinutes: 180 },
];
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const request = (fields: Record<string, unknown>): Record<string, any> => ({ version: 3, notes: null, cleaningType: 'Normal', priceNet: 180, vatRatePercent: 25.5, estimatedDurationMinutes: 180,
  exactStartLocalTime: null, firstAcceptedAtUtc: null, linkedShiftId: null, plannedStartUtc: null, plannedEndUtc: null, acceptedCleanerName: null, actualStartUtc: null, actualEndUtc: null,
  offeredEmployeeId: null, ...fields });
const REQUESTS = [
  request({ id: 'p311', companyId: 'nordea', branchId: 'b-nordea', branchName: 'Nordea office', fromDate: '2030-01-17', toDate: '2030-01-18', status: 'Requested', notes: 'Keys at reception.' }),
  request({ id: 'p314', companyId: 'kesko', branchId: 'b-kesko', branchName: 'Kesko store', fromDate: '2030-01-21', toDate: '2030-01-21', exactStartLocalTime: '07:00', status: 'Offered',
    offeredEmployeeId: 'ville', cleaningType: 'Deep', estimatedDurationMinutes: 120, priceNet: 140 }),
  request({ id: 'p309', companyId: 'nordea', branchId: 'b-nordea', branchName: 'Nordea office', fromDate: '2030-01-18', toDate: '2030-01-18', exactStartLocalTime: '09:00', status: 'Accepted', acceptedCleanerName: 'Ville Rantanen', version: 5 }),
  request({ id: 'p305', companyId: 'aurora', branchId: 'b-aurora', branchName: 'Aurora clinic', fromDate: '2030-01-24', toDate: '2030-01-24', exactStartLocalTime: '08:00', status: 'Accepted', acceptedCleanerName: 'Minna Salo', version: 2, notes: 'Use the back door.' }),
];
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const change = (fields: Record<string, unknown>): Record<string, any> => ({ branchId: null, requestId: null, kind: 'Request', resolvedUtc: null, resolution: null, proposedFromDate: null, proposedToDate: null,
  proposedExactStartLocalTime: null, proposedNotes: null, ...fields });
const CHANGES = [
  change({ id: 'ch-move', companyId: 'nordea', requestId: 'p309', createdUtc: '2030-01-12T10:00:00Z', message: 'The office is closed on Friday. Could you come next week instead?',
    action: 'Reschedule', proposedFromDate: '2030-01-22', proposedToDate: '2030-01-23' }),
  change({ id: 'ch-cancel', companyId: 'aurora', requestId: 'p305', createdUtc: '2030-01-13T10:00:00Z', message: 'We are closed for renovation that week. Please cancel the cleaning.', action: 'Cancel' }),
  change({ id: 'ch-notes', companyId: 'nordea', requestId: 'p311', createdUtc: '2030-01-13T11:00:00Z', message: 'Please use the side entrance.', action: 'Instructions', proposedNotes: 'Side entrance, code 1234.' }),
  change({ id: 'ch-msg', companyId: 'kesko', branchId: 'b-kesko', kind: 'Branch', createdUtc: '2030-01-11T10:00:00Z', message: 'Can you bring extra towels next time?', action: 'Message' }),
];
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const key = (fields: Record<string, unknown>): Record<string, any> => ({ revision: 1, notes: null, feeNet: 25, vatRatePercent: 25.5, status: 'Requested', cancellationReason: null, assignedEmployeeId: null, assignedEmployeeName: null, photos: [], ...fields });
const KEYS = [
  key({ id: 'k-kesko', companyId: 'kesko', branchId: 'b-kesko', branchName: 'Kesko store', kind: 'Pickup', date: '2030-01-19', notes: 'Pick up from the security desk.' }),
  key({ id: 'k-aurora', companyId: 'aurora', branchId: 'b-aurora', branchName: 'Aurora clinic', kind: 'Handover', date: '2030-01-25', status: 'Assigned', assignedEmployeeId: 'minna', assignedEmployeeName: 'Minna Salo' }),
  key({ id: 'k-done', companyId: 'aurora', branchId: 'b-aurora', branchName: 'Aurora clinic', kind: 'Pickup', date: '2030-01-05', status: 'Completed' }),
];
// Booked work shown on the availability bar: Anna 08:00–10:30 and Minna 09:00–12:00 and 14:00–16:00 on 14 January.
const SCHEDULE = [
  { employeeId: 'anna', localDay: '2030-01-14', visits: [{ shiftId: 's-a', shiftName: 'Töölö weekly', startUtc: '2030-01-14T06:00:00Z', endUtc: '2030-01-14T08:30:00Z', branchName: 'Töölö flat' }] },
  { employeeId: 'minna', localDay: '2030-01-14', visits: [
    { shiftId: 's-m1', shiftName: 'Aurora', startUtc: '2030-01-14T07:00:00Z', endUtc: '2030-01-14T10:00:00Z', branchName: 'Aurora clinic' },
    { shiftId: 's-m2', shiftName: 'Kallio', startUtc: '2030-01-14T12:00:00Z', endUtc: '2030-01-14T14:00:00Z', branchName: 'Kallio flat' }] },
  { employeeId: 'ville', localDay: '2030-01-22', visits: [{ shiftId: 's-v', shiftName: 'Nordea', startUtc: '2030-01-22T08:00:00Z', endUtc: '2030-01-22T11:00:00Z', branchName: 'Nordea branch' }] },
];
const BOOKED = [
  { id: 'bk-old', bookingNumber: 'B-0999', status: 'Confirmed', employeeId: 'jari', startUtc: '2030-01-16T06:00:00Z', endUtc: '2030-01-16T08:00:00Z', customer: { name: 'X' }, service: { nameFi: 'Kotisiivous', durationMinutes: 120 } },
];

export type Saved = { method: string; path: string; body: any };
export type WorkQueueState = { saved: Saved[]; fail: Set<string>; queueCalls: number; partnerDown: boolean; queueDown: boolean };

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const clone = (value: unknown): any[] => JSON.parse(JSON.stringify(value));

/** Mocks every endpoint the Work queue reads and writes. Writes are recorded and remove the item, as the server would. */
export async function setupWorkQueue(page: Page, options: { empty?: boolean; many?: number } = {}) {
  await setupAdminMocks(page);
  const state: WorkQueueState = { saved: [], fail: new Set(), queueCalls: 0, partnerDown: false, queueDown: false };
  let shifts = options.empty ? [] : clone(SHIFTS), bookings = options.empty ? [] : clone(BOOKINGS);
  let requests = options.empty ? [] : clone(REQUESTS), changes = options.empty ? [] : clone(CHANGES), keys = options.empty ? [] : clone(KEYS);
  let branches = options.empty ? [] : clone(BRANCHES);
  let fee = 25;
  if (options.many) for (let i = 0; i < options.many; i++) shifts.push({ ...clone(SHIFTS[1]), id: `occ-many-${i}`, reference: `Extra visit ${String(i).padStart(2, '0')}`, customerName: `Customer ${String(i).padStart(2, '0')}`, scheduledLocal: `2030-02-${String(1 + (i % 27)).padStart(2, '0')} 10:00` });

  await page.route(/\/api\/v1\/admin\//, async route => {
    const req = route.request(); const url = new URL(req.url()); const path = url.pathname.replace('/api/v1/admin', '');
    const method = req.method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const json = (body: unknown, status = 200) => route.fulfill({ status, headers, json: body });
    if (method !== 'GET') {
      const body = req.postDataBuffer() && !req.headers()['content-type']?.includes('multipart') ? (() => { try { return req.postDataJSON(); } catch { return null; } })() : null;
      state.saved.push({ method, path, body });
      for (const fail of state.fail) if (path.includes(fail)) return json({ detail: `Refused by the server: ${fail}.` }, 409);
    }
    if (method === 'GET') {
      if (path === '/work-queue') {
        state.queueCalls++;
        if (state.queueDown) return json({ detail: 'down' }, 500);
        const source = url.searchParams.get('source'); const skip = Number(url.searchParams.get('skip')); const take = Number(url.searchParams.get('take'));
        const items = source === 'shift' ? shifts : source === 'booking' ? bookings : [];
        return json({ view: 'needs-action', source, items: items.slice(skip, skip + take), total: items.length, counts: { 'needs-action': items.length, scheduled: 0, 'in-progress': 0, completed: 0, cancelled: 0 } });
      }
      if (path === '/employees') return json(EMPLOYEES);
      if (path === '/companies') return json({ items: COMPANIES, total: COMPANIES.length });
      if (path === '/tenant') return json({ companyName: 'ReadySetSiivous', timeZoneId: 'Europe/Helsinki' });
      if (path === '/shifts/employee-schedule') return json(SCHEDULE);
      if (path === '/bookings') return json(BOOKED);
      if (path.startsWith('/bookings/')) {
        const id = path.split('/')[2]; const item = bookings.find(b => b.id === id);
        const booked = BOOKED.find(b => b.id === id);
        if (!item && booked) return json({ ...booked, startLocalDate: '2030-01-16', startLocalTime: '08:00', employeeName: 'Jari Laine', customer: { name: 'Assigned customer', phone: '040 000 0000', email: 'x@example.fi' }, service: { nameFi: 'Kotisiivous', durationMinutes: 120, priceNet: 90 }, history: [] });
        return item ? json({ id, bookingNumber: item.reference, customer: { name: item.customerName, phone: '040 123 4567', email: 'eeva@example.fi' }, service: { nameFi: 'Kotisiivous', durationMinutes: 180, priceNet: 120 },
          startLocalDate: '2030-01-16', startLocalTime: '08:00', status: item.status, employeeName: null }) : json({}, 404);
      }
      if (path.startsWith('/partner/')) {
        if (state.partnerDown) return json({ detail: 'down' }, 500);
        if (path === '/partner/requests') return json(requests);
        if (path === '/partner/quotes') return json(branches.filter(b => b.quoteRequestedUtc));
        if (path === '/partner/change-requests') return json(changes);
        if (path === '/partner/branches') return json(branches);
        if (path === '/partner/key-requests') return json({ feeNet: fee, vatRatePercent: 25.5, items: keys });
      }
      if (path === '/inventory/acknowledgments') return json([]);
      return json([]);
    }
    const body = state.saved[state.saved.length - 1].body ?? {};
    const drop = <T extends { id: string }>(list: T[], id: string) => list.filter(item => item.id !== id);
    let m: RegExpMatchArray | null;
    if ((m = path.match(/^\/shifts\/([^/]+)\/visit-assignees$/))) { shifts = shifts.filter(s => s.visit?.shiftId !== m![1]); return json({ employeeIds: body.employeeIds, message: 'Saved', preview: false }); }
    if ((m = path.match(/^\/shifts\/([^/]+)\/occurrence-exceptions$/))) { shifts = shifts.filter(s => s.visit?.shiftId !== m![1]); return json({ startUtc: body.startUtc, endUtc: body.endUtc }); }
    if ((m = path.match(/^\/shifts\/([^/]+)\/visits\/([^/]+)\/cancel$/))) { shifts = shifts.filter(s => s.visit?.shiftId !== m![1]); return json({ startUtc: '', endUtc: '' }); }
    if ((m = path.match(/^\/branch-visits\/shifts\/([^/]+)\/visits\/([^/]+)\/employee$/))) { shifts = shifts.filter(s => s.visit?.shiftId !== m![1]); return json({ id: m[2], shiftId: m[1], status: 'Offered', offerRevision: 1 }); }
    if ((m = path.match(/^\/bookings\/([^/]+)\/(assign|cancel)$/))) { bookings = drop(bookings, m[1]); return route.fulfill({ status: 204, headers }); }
    if ((m = path.match(/^\/bookings\/([^/]+)\/confirm$/))) { bookings = bookings.map(b => b.id === m![1] ? { ...b, status: 'Confirmed', stateLabel: 'Unassigned' } : b); return route.fulfill({ status: 204, headers }); }
    if ((m = path.match(/^\/partner\/requests\/([^/]+)\/offer$/))) {
      const found = requests.find(r => r.id === m![1])!;
      const accepted = body.employeeId === 'anna';
      requests = requests.map(r => r.id === found.id ? { ...r, status: accepted ? 'Accepted' : 'Offered', offeredEmployeeId: accepted ? null : body.employeeId, version: r.version + 1 } : r);
      return json({ ...found, status: accepted ? 'Accepted' : 'Offered' });
    }
    if ((m = path.match(/^\/partner\/requests\/([^/]+)\/cancel$/))) { requests = requests.map(r => r.id === m![1] ? { ...r, status: 'Cancelled' } : r); return json({ id: m[1], status: 'Cancelled' }); }
    if ((m = path.match(/^\/partner\/requests\/([^/]+)$/)) && method === 'PUT') { requests = requests.map(r => r.id === m![1] ? { ...r, ...body, version: r.version + 1, status: 'Requested', offeredEmployeeId: null } : r); return json(requests.find(r => r.id === m![1])); }
    if ((m = path.match(/^\/partner\/branches\/([^/]+)\/(pricing|quote-rejection)$/))) { branches = branches.map(b => b.id === m![1] ? { ...b, quoteRequestedUtc: undefined, pricingReady: m![2] === 'pricing' } : b); return json(branches.find(b => b.id === m![1])); }
    if ((m = path.match(/^\/partner\/change-requests\/([^/]+)\/(resolve|apply)$/))) {
      const found = changes.find(c => c.id === m![1])!;
      if (m[2] === 'apply' && found.action === 'Reschedule') requests = requests.map(r => r.id === found.requestId ? { ...r, fromDate: found.proposedFromDate!, toDate: found.proposedToDate!, status: 'Requested', version: r.version + 1 } : r);
      changes = drop(changes, m[1]); return json({ ...found, resolvedUtc: '2030-01-14T10:00:00Z', resolution: body?.resolution });
    }
    if ((m = path.match(/^\/partner\/key-requests\/([^/]+)\/assignment$/))) { keys = keys.map(k => k.id === m![1] ? { ...k, status: 'Assigned', assignedEmployeeId: body.employeeId, assignedEmployeeName: EMPLOYEES.find(e => e.id === body.employeeId)?.firstName } : k); return json(keys.find(k => k.id === m![1])); }
    if ((m = path.match(/^\/partner\/key-requests\/([^/]+)\/complete$/))) { keys = keys.map(k => k.id === m![1] ? { ...k, status: 'Completed' } : k); return json(keys.find(k => k.id === m![1])); }
    if ((m = path.match(/^\/partner\/key-requests\/([^/]+)\/photos$/))) return json({ id: 'photo-1', sizeBytes: 10, uploadedUtc: '2030-01-14T10:00:00Z' });
    if (path === '/partner/key-request-fee') { fee = body.feeNet; return json({ feeNet: fee, vatRatePercent: 25.5, items: keys }); }
    return json({});
  });
  return state;
}
