import type { Branch, Employee, WorkQueueItem } from '@/lib/adminApi';
import type { CleaningRequest, KeyRequest, PartnerChange } from '@/lib/partnerApi';

/** What an item is: every cleaning visit is "Cleaning", whatever its source. */
export type Kind = 'cleaning' | 'quote' | 'change' | 'key';
/** Where an item comes from. Each has its own colour. */
export type Source = 'admin' | 'booking' | 'partner';
export type ChangeKind = 'reschedule' | 'cancel' | 'instructions' | 'message';

export type Payload =
  | { t: 'shift'; item: WorkQueueItem }
  | { t: 'booking'; item: WorkQueueItem }
  | { t: 'request'; request: CleaningRequest }
  | { t: 'quote'; branch: Branch }
  | { t: 'change'; change: PartnerChange; changeKind: ChangeKind; request: CleaningRequest | null }
  | { t: 'key'; key: KeyRequest };

export interface Row {
  id: string;
  kind: Kind;
  source: Source;
  customer: string;
  reference: string;
  when: string;
  /** Sorts by date: yyyy-MM-dd HH:mm in the tenant's time. */
  sortKey: string;
  state: string;
  alert: boolean;
  cleanType: string | null;
  action: string;
  payload: Payload;
}

export const TYPE_LABEL: Record<Kind, string> = { cleaning: 'Cleaning', quote: 'Quote', change: 'Change request', key: 'Key handling' };
export const SOURCE_LABEL: Record<Source, string> = { admin: 'Admin', booking: 'Public booking', partner: 'Partner portal' };
export const SOURCE_CHIP: Record<Source, string> = { admin: 'chip src-admin', booking: 'chip src-booking', partner: 'chip src-partner' };
export const SOURCES: { value: 'all' | Source; label: string }[] = [
  { value: 'all', label: 'All sources' }, { value: 'admin', label: 'Admin' }, { value: 'booking', label: 'Public booking' }, { value: 'partner', label: 'Partner portal' },
];
export const TYPES: { value: 'all' | Kind; label: string }[] = [
  { value: 'all', label: 'All types' }, { value: 'cleaning', label: 'Cleaning' }, { value: 'quote', label: 'Quote' }, { value: 'change', label: 'Change request' }, { value: 'key', label: 'Key handling' },
];
export const CHANGE_ACTION: Record<ChangeKind, string> = { reschedule: 'Reschedule', cancel: 'Cancel visit', instructions: 'Update notes', message: 'Reply' };
export const CHANGE_NAME: Record<ChangeKind, string> = { reschedule: 'Reschedule', cancel: 'Cancel', instructions: 'Instructions', message: 'Message' };

// ---- Dates and times. Hours are decimals: 8.25 is 08:15. ----

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

export const hh = (h: number) => `${String(Math.floor(h)).padStart(2, '0')}:${String(Math.round((h % 1) * 60)).padStart(2, '0')}`;
/** Short time for inside a block: 8–11, 8:15–11:15. */
export const hs = (h: number) => `${Math.floor(h)}${h % 1 ? `:${String(Math.round((h % 1) * 60)).padStart(2, '0')}` : ''}`;
export const snap = (h: number) => Math.round(h * 4) / 4;
export const lenText = (l: number) => (l % 1 ? `${Math.floor(l)} h ${Math.round((l % 1) * 60)} min` : `${l} h`);
export const addDays = (day: string, n: number) => {
  const d = new Date(`${day}T12:00:00Z`); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10);
};
export const dayName = (day: string) => { const d = new Date(`${day}T12:00:00Z`); return `${WEEKDAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
export const dateText = (day: string) => { const [y, m, d] = day.split('-').map(Number); return y && m && d ? `${d} ${MONTHS[m - 1]} ${y}` : day; };
/** "2026-11-24 08:00" as "24 Nov 2026 08:00". */
export const localText = (local: string) => { const [day, time] = local.split(' '); return `${dateText(day)}${time ? ` ${time.slice(0, 5)}` : ''}`; };
export const windowText = (from: string, to: string) => {
  if (from === to) return dateText(from);
  const [fy, fm, fd] = from.split('-').map(Number); const [ty, tm, td] = to.split('-').map(Number);
  return fy === ty && fm === tm ? `${fd}–${td} ${MONTHS[tm - 1]} ${ty}` : `${dateText(from)} – ${dateText(to)}`;
};
export const daysBetween = (from: string, to: string, max = 14) => {
  const out: string[] = []; for (let day = from; day <= to && out.length < max; day = addDays(day, 1)) out.push(day); return out;
};
const timeOf = (value: string | null | undefined) => {
  const match = value ? /^(\d{1,2}):(\d{2})/.exec(value) : null; return match ? Number(match[1]) + Number(match[2]) / 60 : null;
};
export { timeOf };

/** A UTC instant as the tenant's local day and decimal hour. */
export function localOf(utc: string, zone: string): { day: string; hour: number } {
  const parts = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: zone }).formatToParts(new Date(utc));
  const get = (type: string) => parts.find(part => part.type === type)?.value ?? '00';
  return { day: `${get('year')}-${get('month')}-${get('day')}`, hour: Number(get('hour')) + Number(get('minute')) / 60 };
}
/** A tenant-local day and decimal hour as a UTC instant, correct across clock changes. */
export function zonedToUtc(day: string, hour: number, zone: string): string {
  const [y, m, d] = day.split('-').map(Number);
  const minutes = Math.round(hour * 60);
  const wanted = Date.UTC(y, m - 1, d, Math.floor(minutes / 60), minutes % 60);
  let instant = wanted;
  for (let attempt = 0; attempt < 3; attempt++) {
    const seen = localOf(new Date(instant).toISOString(), zone);
    const [sy, sm, sd] = seen.day.split('-').map(Number);
    const seenMinutes = Math.round(seen.hour * 60);
    const shown = Date.UTC(sy, sm - 1, sd, Math.floor(seenMinutes / 60), seenMinutes % 60);
    if (shown === wanted) break;
    instant += wanted - shown;
  }
  return new Date(instant).toISOString();
}

export const personName = (employee: Pick<Employee, 'firstName' | 'lastName'>) => `${employee.firstName} ${employee.lastName}`.trim();
export const cleaningTypeText = (value: string | null | undefined) => !value ? null : value === 'Deep' ? 'Deep clean' : value;
const changeKindOf = (change: PartnerChange): ChangeKind =>
  change.action === 'Reschedule' ? 'reschedule' : change.action === 'Cancel' ? 'cancel' : change.action === 'Instructions' ? 'instructions' : 'message';
const waitingFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Europe/Helsinki' });
const sinceText = (utc: string | null | undefined) => !utc || Number.isNaN(Date.parse(utc)) ? '' : waitingFormat.format(new Date(utc));
const localDayOf = (utc: string | null | undefined, zone: string) => !utc || Number.isNaN(Date.parse(utc)) ? '9999-12-31' : localOf(utc, zone).day;

export interface Sources {
  shifts: WorkQueueItem[];
  bookings: WorkQueueItem[];
  /** Partner cleaning requests waiting for an employee. */
  requests: CleaningRequest[];
  /** Every partner request, so a change request can show the visit it is about. */
  allRequests: CleaningRequest[];
  quotes: Branch[];
  changes: PartnerChange[];
  keys: KeyRequest[];
  companies: Map<string, string>;
  branches: Map<string, Branch>;
  employees: Employee[];
  zone: string;
}

/** Everything waiting for the office, as one list of typed items. */
export function buildRows(data: Sources): Row[] {
  const company = (id: string) => data.companies.get(id) ?? 'Partner company';
  const branchName = (id: string | null | undefined) => (id && data.branches.get(id)?.name) || 'Property';
  const nameOf = (id: string | null | undefined) => { const found = data.employees.find(e => e.id === id); return found ? personName(found) : 'an employee'; };
  const rows: Row[] = [];
  for (const item of data.shifts) {
    const overdue = item.stateLabel.startsWith('Overdue');
    rows.push({ id: `shift-${item.id}`, kind: 'cleaning', source: 'admin', customer: item.customerName,
      reference: [item.visit?.branchName, item.reference].filter(Boolean).join(', '), when: localText(item.scheduledLocal), sortKey: item.scheduledLocal,
      state: item.stateLabel, alert: overdue, cleanType: cleaningTypeText(item.visit?.cleaningType) ?? 'Normal', action: 'Assign', payload: { t: 'shift', item } });
  }
  for (const item of data.bookings) {
    rows.push({ id: `booking-${item.id}`, kind: 'cleaning', source: 'booking', customer: item.customerName, reference: item.reference,
      when: localText(item.scheduledLocal), sortKey: item.scheduledLocal, state: item.stateLabel, alert: false,
      cleanType: item.service ?? null, action: 'Assign', payload: { t: 'booking', item } });
  }
  for (const request of data.requests) {
    const offered = request.status === 'Offered';
    rows.push({ id: `request-${request.id}`, kind: 'cleaning', source: 'partner', customer: company(request.companyId), reference: request.branchName,
      when: `${windowText(request.fromDate, request.toDate)}${request.exactStartLocalTime ? ` ${request.exactStartLocalTime.slice(0, 5)}` : ''}`,
      sortKey: `${request.fromDate} ${request.exactStartLocalTime?.slice(0, 5) ?? '00:00'}`,
      state: offered ? `Awaiting acceptance · offered to ${nameOf(request.offeredEmployeeId)}` : 'Needs an employee', alert: false,
      cleanType: cleaningTypeText(request.cleaningType) ?? 'Normal', action: 'Offer', payload: { t: 'request', request } });
  }
  for (const branch of data.quotes) {
    rows.push({ id: `quote-${branch.id}`, kind: 'quote', source: 'partner', customer: company(branch.companyId),
      reference: [branch.name, branch.city].filter(Boolean).join(', '), when: `Waiting since ${sinceText(branch.quoteRequestedUtc)}`,
      sortKey: `${localDayOf(branch.quoteRequestedUtc, data.zone)} 00:00`, state: 'Quote requested', alert: false, cleanType: null, action: 'Set price', payload: { t: 'quote', branch } });
  }
  for (const change of data.changes) {
    const changeKind = changeKindOf(change);
    const request = change.requestId ? data.allRequests.find(r => r.id === change.requestId) ?? null : null;
    const place = request?.branchName ?? branchName(change.branchId);
    rows.push({ id: `change-${change.id}`, kind: 'change', source: 'partner', customer: company(change.companyId),
      reference: `${place} · ${CHANGE_NAME[changeKind]}`, when: `Sent ${sinceText(change.createdUtc)}`, sortKey: `${localDayOf(change.createdUtc, data.zone)} 00:00`,
      state: 'Awaiting response', alert: false, cleanType: null, action: CHANGE_ACTION[changeKind], payload: { t: 'change', change, changeKind, request } });
  }
  for (const key of data.keys) {
    const assigned = !!key.assignedEmployeeId;
    const kind = key.kind === 'Handover' ? 'Key handover' : 'Key pickup';
    rows.push({ id: `key-${key.id}`, kind: 'key', source: 'partner', customer: company(key.companyId), reference: `${key.branchName} · ${kind}`,
      when: dateText(key.date), sortKey: `${key.date} 00:00`,
      state: assigned ? `Assigned to ${key.assignedEmployeeName ?? nameOf(key.assignedEmployeeId)} · not completed` : 'Needs a staff member', alert: false,
      cleanType: null, action: assigned ? 'Complete' : 'Assign', payload: { t: 'key', key } });
  }
  return rows;
}
