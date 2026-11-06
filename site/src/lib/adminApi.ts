// Admin API client: attaches the in-memory Keycloak bearer token.

import { API_URL } from './api';
import { token, refreshToken, login } from './auth';
import { cacheScopeFor, requestSignal } from './requestScope';

export interface Booking {
  id: string;
  bookingNumber: string;
  customer: { name: string; phone: string; email: string };
  service: { nameFi: string; durationMinutes: number; priceNet: number };
  startLocalDate: string;
  startLocalTime: string;
  status: string;
  employeeName?: string | null;
  history?: { status: string; atUtc: string; by: string; note?: string | null }[];
}

export interface Invoice {
  id: string;
  invoiceNumber: string;
  bookingNumber: string;
  customer: { name: string };
  total: { net: number; vat: number; gross: number };
  status: string;
  issueDate: string;
  dueDate: string;
}

export interface Employee { autoAcceptWork?: boolean;
  shiftCalendarEnabled?: boolean;
  id: string;
  email: string;
  keycloakUserId?: string;
  inviteLastSentUtc?: string | null;
  inviteError?: string | null;
  identitySyncPending?: boolean;
  firstName: string;
  lastName: string;
  phone: string;
  role: string;
  partnerCompanyId?: string | null;
  isActive: boolean;
  colorHex?: string | null;
  skills: string[];
  serviceAreas: string[];
  payRate?: number | null;
  certifications: { name: string; expiresAtUtc?: string | null }[];
  notes?: string | null;
  defaultHours: Record<string, { start?: string | null; end?: string | null }>;
}

export async function authorizedFetch(path: string, init: RequestInit = {}): Promise<Response> {
  let t = token();
  if (!t) t = await refreshToken();
  if (!t) await login();

  // Reads get a timeout so a hung page can settle. Writes do not: the server may still commit a create or invite after the client gives up, and a retry would duplicate it.
  const isRead = !init.method || init.method === 'GET';
  const send = (bearer: string | undefined) => fetch(`${API_URL}${path}`, {
    ...init,
    signal: isRead ? requestSignal(init.signal) : init.signal,
    headers: {
      ...(init.headers ?? {}),
      ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}),
      ...(init.body ? { "Content-Type": "application/json" } : {}),
    },
  });

  let response = await send(t);

  if (response.status === 401) {
    const refreshed = await refreshToken();
    // Without a fresh token the original 401 is the answer; never retry with an empty bearer.
    if (!refreshed) return response;
    response = await send(refreshed);
  }

  return response;
}

// In-memory GET cache so navigating between admin pages does not re-fetch
// unchanged lists. Mutations clear it.
const _cache = new Map<string, { data: unknown; ts: number }>();
const _CACHE_TTL = 30_000;

export function invalidateAdminCache() {
  _cache.clear();
}

let _cacheScope = 'anonymous';

// A null result means the read failed (HTTP error, timeout or network). Callers already treat null as "could not load".
export async function adminGet<T>(path: string, useCache = true): Promise<T | null> {
  // Responses are only reused for the same signed-in user; a different user or no user clears them.
  const scope = cacheScopeFor(token());
  if (scope !== _cacheScope) { _cache.clear(); _cacheScope = scope; }
  const hit = useCache ? _cache.get(path) : undefined;
  if (hit && Date.now() - hit.ts < _CACHE_TTL) return hit.data as T;
  try {
    const response = await authorizedFetch(path, useCache ? {} : { cache: 'no-store' });
    if (!response.ok) return null;
    const data = (await response.json()) as T;
    // Cache only if the same user is still signed in; a response that landed after a user switch must not be stored for the next user.
    if (useCache && cacheScopeFor(token()) === scope) _cache.set(path, { data, ts: Date.now() });
    return data;
  } catch {
    return null;
  }
}

export async function adminSend(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: unknown
): Promise<boolean> {
  const response = await authorizedFetch(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const ok = response.ok || response.status === 204;
  if (ok) invalidateAdminCache();
  return ok;
}

export type AdminSendResult = { ok: true } | { ok: false; message: string };

// Turns an RFC 7807 problem into one readable sentence. Field-level messages are used when
// there is no detail, so validation failures still say what to change.
export function problemText(problem: unknown, fallback: string): string {
  const body = (problem ?? {}) as { detail?: unknown; title?: unknown; errors?: Record<string, unknown> };
  if (typeof body.detail === 'string' && body.detail.trim()) return body.detail.trim();
  if (body.errors && typeof body.errors === 'object') {
    const messages = Object.values(body.errors).flat().filter((m): m is string => typeof m === 'string' && m.trim() !== '');
    if (messages.length) return [...new Set(messages)].slice(0, 3).join(' ');
  }
  if (typeof body.title === 'string' && body.title.trim()) return body.title.trim();
  return fallback;
}

// Like adminSend, but keeps the server's reason (problem detail) so the user can act on it.
export async function adminSendResult(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: unknown
): Promise<AdminSendResult> {
  let response: Response;
  try {
    response = await authorizedFetch(path, { method, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    return { ok: false, message: 'Could not reach the server. Check your connection, then save again.' };
  }
  if (response.ok || response.status === 204) { invalidateAdminCache(); return { ok: true }; }
  const problem = await response.json().catch(() => null);
  return { ok: false, message: problemText(problem, `The change was not saved (error ${response.status}). Check the fields, then save again.`) };
}

export type AdminJsonResult<T> = { ok: true; value: T } | { ok: false; message: string };

// Like adminSendJson, but keeps the server's reason so the form can say what to change.
export async function adminSendJsonResult<T>(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<AdminJsonResult<T>> {
  let response: Response;
  try {
    response = await authorizedFetch(path, { method, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    return { ok: false, message: 'Could not reach the server. Check your connection, then save again.' };
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    return { ok: false, message: problemText(problem, `The change was not saved (error ${response.status}). Check the fields, then save again.`) };
  }
  invalidateAdminCache();
  return { ok: true, value: (await response.json()) as T };
}

export async function adminSendJson<T>(
  path: string,
  method: 'POST' | 'PUT',
  body?: unknown
): Promise<T | null> {
  const response = await authorizedFetch(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) return null;
  const data = (await response.json()) as T;
  invalidateAdminCache();
  return data;
}

export async function adminDelete(path: string): Promise<void> {
  const response = await authorizedFetch(path, { method: 'DELETE' });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
    throw new Error(problem.detail || problem.title || `Delete failed (${response.status}). Please retry.`);
  }
  invalidateAdminCache();
}

export type WorkQueueView = 'needs-action' | 'scheduled' | 'in-progress' | 'completed' | 'cancelled';
export type WorkQueueSource = 'all' | 'booking' | 'partner' | 'shift';

export interface WorkQueueItem {
  source: 'booking' | 'partner' | 'shift';
  id: string;
  reference: string;
  customerName: string;
  scheduledLocal: string;
  status: string;
  stateLabel: string;
  assigneeName: string | null;
  href: string;
}

export interface WorkQueuePage {
  view: WorkQueueView;
  source: WorkQueueSource;
  items: WorkQueueItem[];
  total: number;
  counts: Record<WorkQueueView, number>;
}

/** One bounded page of the admin work queue. Null on any failure so callers can offer retry. */
export const adminWorkQueue = {
  page: async (params: { view: WorkQueueView; source: WorkQueueSource; q?: string; from?: string; to?: string; assignee?: string; skip: number; take: number }): Promise<WorkQueuePage | null> => {
    const qs = new URLSearchParams({ view: params.view, source: params.source, skip: String(params.skip), take: String(params.take) });
    if (params.q) qs.set('q', params.q);
    if (params.from) qs.set('from', params.from);
    if (params.to) qs.set('to', params.to);
    if (params.assignee) qs.set('assignee', params.assignee);
    const page = await adminGet<WorkQueuePage>(`/api/v1/admin/work-queue?${qs.toString()}`, false);
    if (!page || !Array.isArray(page.items) || !Number.isInteger(page.total) || page.total < 0 || !page.counts) return null;
    return page;
  },
};

export const adminBookings = {
  list: (query = '') => adminGet<Booking[]>(`/api/v1/admin/bookings${query}`),
  detail: (id: string) => adminGet<Booking>(`/api/v1/admin/bookings/${id}`, false),
  confirm: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/confirm`, 'POST', {}),
  cancel: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/cancel`, 'POST', {}),
  complete: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/complete`, 'POST', {}),
  assign: (id: string, employeeId: string) =>
    adminSend(`/api/v1/admin/bookings/${id}/assign`, 'POST', { employeeId }),
  unassign: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/unassign`, 'POST'),
  schedule: (date: string, employeeId: string) =>
    adminGet<Booking[]>(`/api/v1/admin/schedule?date=${encodeURIComponent(date)}&employeeId=${encodeURIComponent(employeeId)}`, false),
};

export interface ServiceFields {
  slug: string;
  category: string;
  name: Record<string, string>;
  description: Record<string, string>;
  additionalInfo?: Record<string, string> | null;
  durationMinutes: number;
  priceNet: number;
  vatRatePercent: number;
  currency: string;
  isFeatured: boolean;
  sortOrder: number;
  icon?: string | null;
  imageUrl?: string | null;
}

export const adminServices = {
  list: (includeInactive = false) =>
    adminGet<{ id: string; slug: string; category: string; name: { values: Record<string, string> }; description: { values: Record<string, string> }; additionalInfo: { values: Record<string, string> } | null; icon: string; imageUrl: string; durationMinutes: number; priceNet: number; vatRatePercent: number; isActive: boolean; isFeatured: boolean; sortOrder: number }[]>(
      `/api/v1/admin/services?includeInactive=${includeInactive}`
    ),
  create: (fields: ServiceFields): Promise<AdminSendResult> => adminSendResult('/api/v1/admin/services', 'POST', { fields }),
  update: (id: string, fields: ServiceFields, isActive: boolean): Promise<AdminSendResult> =>
    adminSendResult(`/api/v1/admin/services/${id}`, 'PUT', { fields, isActive }),
  remove: (id: string) => adminSend(`/api/v1/admin/services/${id}`, 'DELETE'),
  deleteInactive: (id: string) => adminDelete(`/api/v1/admin/services/${id}`),
  uploadImage: async (id: string, file: File): Promise<string | null> => {
    const t = token();
    const body = new FormData();
    body.append('file', file);
    const response = await fetch(`${API_URL}/api/v1/admin/services/${id}/image`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${t}` },
      body,
    });
    if (!response.ok) return null;
    const data = (await response.json()) as { imageUrl: string };
    return `${API_URL}${data.imageUrl}`;
  },
};

export const adminInvoices = {
  list: () => adminGet<Invoice[]>('/api/v1/admin/invoices'),
  listForCustomer: (companyId: string) => adminGet<Invoice[]>(`/api/v1/admin/invoices?companyId=${encodeURIComponent(companyId)}`, false),
  create: (bookingId: string) => adminSend('/api/v1/admin/invoices', 'POST', { bookingId }),
  markPaid: (id: string) =>
    adminSend(`/api/v1/admin/invoices/${id}/mark-paid`, 'POST', { paidAtUtc: new Date().toISOString() }),
  void: (id: string) => adminSend(`/api/v1/admin/invoices/${id}/void`, 'POST'),
  pdfUrl: (id: string) => `${API_URL}/api/v1/admin/invoices/${id}/pdf`,
};

export interface EmployeeFields {
  email: string;
  firstName: string;
  lastName: string;
  phone: string;
  role: string;
  partnerCompanyId?: string | null;
  colorHex?: string | null;
  defaultHours: Record<string, { start?: string | null; end?: string | null }>;
  skills?: string[];
  serviceAreas?: string[];
  payRate?: number | null;
  certifications?: { name: string; expiresAtUtc?: string | null }[];
  notes?: string | null;
}

async function employeeMutation(path: string, body?: unknown): Promise<boolean> {
  const response = await authorizedFetch(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
    throw new Error(problemText(problem, `The change was not saved (error ${response.status}). Check the fields, then save again.`));
  }
  invalidateAdminCache(); return true;
}

export const adminEmployees = {
  list: (includeInactive = false) => adminGet<Employee[]>(`/api/v1/admin/employees${includeInactive ? '?includeInactive=true' : ''}`, false),
  create: (fields: EmployeeFields) => employeeMutation('/api/v1/admin/employees', { fields }),
  reactivate: (id: string) => employeeMutation(`/api/v1/admin/employees/${id}/reactivate`),
  update: (id: string, fields: EmployeeFields, isActive: boolean): Promise<AdminSendResult> =>
    adminSendResult(`/api/v1/admin/employees/${id}`, 'PUT', { fields, isActive }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/employees/${id}/deactivate`, 'POST'),
  deleteInactive: (id: string) => adminDelete(`/api/v1/admin/employees/${id}`),
  invite: async (id: string): Promise<{ email: string; keycloakUserId: string; status: 'Sent' | 'AlreadySent'; sentUtc: string }> => {
    const response = await authorizedFetch(`/api/v1/admin/employees/${id}/invite`, { method: 'POST' });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
      throw new Error(problem.detail || problem.title || `Invitation failed (${response.status}).`);
    }
    invalidateAdminCache(); return response.json();
  },
};

export interface AgreementSignerDto {
  id: string; name: string; email: string; status: string; token: string;
}
export interface AgreementListItem {
  hasSignedEvidence?: boolean;
  companyId?: string;
  companyName?: string;
  id: string; title: string; code: string; status: string; isActive: boolean; signerCount: number; signedCount: number; createdUtc: string;
}
export interface AgreementDetail extends AgreementListItem {
  signers: AgreementSignerDto[];
  completedUtc: string | null;
}

export const adminTenant = {
  get: () => adminGet<{ companyName: string; timeZoneId?: string }>('/api/v1/admin/tenant'),
};

// Parse RFC 7807 problem details into a single human-readable message, so the
// create form can show *why* creation failed instead of a generic "Failed".
async function agreementCreateError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> };
    if (body.errors) {
      const first = Object.values(body.errors).flat()[0];
      if (first) return first;
    }
    // The detail explains what to change; the title is only a category.
    if (body.detail) return body.detail;
    if (body.title) return body.title;
  } catch {
    // fall through
  }
  return `The agreement was not created (error ${response.status}). Check the fields and the PDF, then create it again.`;
}

export const adminAgreements = {
  list: () => adminGet<AgreementListItem[]>('/api/v1/admin/agreements'),
  listForCustomer: (companyId: string) => adminGet<AgreementListItem[]>(`/api/v1/admin/agreements?companyId=${encodeURIComponent(companyId)}`, false),
  get: (id: string) => adminGet<AgreementDetail>(`/api/v1/admin/agreements/${id}`),
  create: async (title: string, companyId: string, pdf: File, signers: { name: string; email: string }[]):
    Promise<{ ok: true; data: AgreementDetail } | { ok: false; error: string }> => {
    const form = new FormData();
    form.append('title', title);
    form.append('companyId', companyId);
    form.append('pdf', pdf);
    form.append('signersJson', JSON.stringify(signers));
    const t = token();
    const response = await fetch(`${API_URL}/api/v1/admin/agreements`, {
      method: 'POST', headers: { Authorization: `Bearer ${t}` }, body: form,
    });
    if (response.ok) {
      invalidateAdminCache();
      return { ok: true, data: (await response.json()) as AgreementDetail };
    }
    return { ok: false, error: await agreementCreateError(response) };
  },
  addSigner: (id: string, name: string, email: string) =>
    adminSend(`/api/v1/admin/agreements/${id}/signers`, 'POST', { name, email }),
  removeSigner: (id: string, signerId: string) =>
    adminSend(`/api/v1/admin/agreements/${id}/signers/${signerId}`, 'DELETE'),
  cancel: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/cancel`, 'POST'),
  activate: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/activate`, 'POST'),
  deactivate: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/deactivate`, 'POST'),
  deleteInactive: (id: string, confirmSigned = false) => adminDelete(`/api/v1/admin/agreements/${id}${confirmSigned ? '?confirmSigned=true' : ''}`),
  legacyRepairPreview: () => adminGet<LegacyAgreementRepairPreview>('/api/v1/admin/agreements/legacy-repairs'),
  repairLegacyCompany: async (agreementId: string, companyId: string, expectedVersion: string): Promise<{ ok: boolean; error?: string }> => {
    const response = await authorizedFetch(`/api/v1/admin/agreements/${agreementId}/repair-company`, {
      method: 'POST', body: JSON.stringify({ companyId, expectedVersion }),
    });
    if (response.ok) { invalidateAdminCache(); return { ok: true }; }
    const body = await response.json().catch(() => ({})) as { detail?: string; title?: string };
    return { ok: false, error: body.detail || body.title || `Repair failed (${response.status}).` };
  },
  documentUrl: (id: string) => `${API_URL}/api/v1/admin/agreements/${id}/document`,
  downloadDocument: async (id: string): Promise<void> => {
    const t = token();
    const response = await fetch(`${API_URL}/api/v1/admin/agreements/${id}/document`, {
      headers: { Authorization: `Bearer ${t}` },
    });
    if (!response.ok) return;
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `agreement-${id}.pdf`;
    a.click();
    URL.revokeObjectURL(url);
  },
};

export interface LegacyAgreementRepairCandidate {
  agreementId: string;
  title: string;
  status: string;
  missingCompanyId: string;
  expectedVersion: string;
  createdUtc: string;
}

export interface LegacyAgreementRepairPreview {
  agreements: LegacyAgreementRepairCandidate[];
  companies: Company[];
}

export async function downloadInvoicePdf(id: string): Promise<void> {
  const t = token();
  const response = await fetch(adminInvoices.pdfUrl(id), {
    headers: { Authorization: `Bearer ${t}` },
  });
  if (!response.ok) return;
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `invoice-${id}.pdf`;
  a.click();
  URL.revokeObjectURL(url);
}

// ---------- Companies and Branches ----------
export interface Company {
  isPartnerEnabled?: boolean;
  id: string;
  businessId: string;
  name: string;
  contactName?: string | null;
  contactEmail?: string | null;
  contactPhone?: string | null;
  notes?: string | null;
  isActive: boolean;
}

export interface Branch {
  roomCount?: number | null;
  bedCount?: number | null;
  priceNet?: number | null;
  vatRatePercent?: number | null;
  estimatedDurationMinutes?: number | null;
  pricingVersion?: number;
  pricingReady?: boolean;
  quoteRequestedUtc?: string | null;
  quoteRejectedUtc?: string | null;
  quoteRejectionReason?: string | null;
  id: string;
  companyId: string;
  name: string;
  street?: string | null;
  postalCode?: string | null;
  city?: string | null;
  country?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  isActive: boolean;
}

export interface CompanyDetail {
  company: Company;
  branches: Branch[];
}

// ---------- Shifts ----------
export type TimeRange = {
  start: string; // HH:mm
  end: string;   // HH:mm
};

// System.Text.Json serializes enums as numbers by default (no JsonStringEnumConverter),
// so the wire values are 0..6 matching the server enum's declaration order.
export const ShiftScheduleType = {
  DailySameTime: 0,
  DailyDifferentTime: 1,
  Weekly: 2,
  BiWeekly: 3,
  OnCallFlexible: 4,
  Monthly: 5,
  OneTime: 6,
} as const;

export type ShiftScheduleType =
  (typeof ShiftScheduleType)[keyof typeof ShiftScheduleType];

export interface ShiftSchedule {
  type: ShiftScheduleType;
  dailyStart?: string | null;
  dailyEnd?: string | null;
  dailyTimes?: Record<string, TimeRange> | null;
  weeklyDay?: number | null;
  weeklyDays?: number[] | null;
  weeklyStart?: string | null;
  weeklyEnd?: string | null;
  biWeeklyWeekParity?: number | null;
  biWeeklyDay?: number | null;
  biWeeklyStart?: string | null;
  biWeeklyEnd?: string | null;
  monthlyDay?: number | null;
  monthlyStart?: string | null;
  monthlyEnd?: string | null;
  oneTimeDate?: string | null; // YYYY-MM-DD, tenant local calendar date
  oneTimeStart?: string | null; // HH:mm
  oneTimeEnd?: string | null; // HH:mm
}

export interface Shift {
  id: string;
  companyId: string;
  branchId: string;
  name: string;
  schedule: ShiftSchedule;
  notes?: string | null;
  isActive: boolean;
  validFrom?: string | null;
  validUntil?: string | null;
  qualityCycleTemplateId?: string | null;
  /** One-time visit whose checklist has been submitted. */
  isCompleted?: boolean;
}

export interface ShiftAssignment {
  id: string;
  shiftId: string;
  employeeId: string;
  assignedAtUtc: string;
  isActive: boolean;
  note?: string | null;
}

export interface VisitAssigneesInput {
  originalStartUtc: string | null; extraExceptionId: string | null;
  mode: number; employeeIds: string[]; reason: string; rosterVersion: string;
  assignmentVersion: string | null; fromThisDate: boolean; preview: boolean;
}
export interface ScheduleVisit { shiftId: string; shiftName: string; startUtc: string; endUtc: string }
export interface EmployeeDaySchedule { employeeId: string; localDay: string; visits: ScheduleVisit[] }
export interface OverlapFinding { employeeId: string; earlier: ScheduleVisit; later: ScheduleVisit }
export interface VisitAssignmentResult { employeeIds: string[]; message: string; preview: boolean }
export interface ShiftOccurrence {
  startUtc: string;
  endUtc: string;
  exceptionId?: string | null;
  originalStartUtc?: string | null;
  status?: number | null;
  reason?: string | null;
  employeeId?: string | null;
  assigneeIds?: string[]; assignmentSource?: string; assignmentMode?: number | null;
  assignmentVersion?: string | null; overrideEmployeeIds?: string[];
  assignmentReason?: string | null; rosterVersion?: string; canAssign?: boolean; assignmentLockReason?: string | null;
}

export interface ShiftOccurrenceExceptionInput {
  id?: string | null;
  originalStartUtc?: string | null;
  startUtc: string;
  endUtc: string;
  status: number;
  reason: string;
  employeeId?: string | null;
}

export interface ShiftConflictDetail { shiftId: string; shiftName: string; startUtc: string; endUtc: string }

// Overlap refusals carry the clashing shift so the assignment dialog can offer a fix.
export class VisitConflictError extends Error {
  constructor(message: string, public readonly conflicts: ShiftConflictDetail[]) { super(message); this.name = 'VisitConflictError'; }
}

async function occurrenceRequest<T>(path: string, method: 'PUT' | 'DELETE', body?: unknown): Promise<T> {
  const response = await authorizedFetch(path, { method, cache: 'no-store',
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const message = problemText(problem, 'Could not update this occurrence. Please retry.');
    const conflicts = Array.isArray(problem?.conflicts) ? problem.conflicts as ShiftConflictDetail[] : [];
    if (response.status === 409 && conflicts.length > 0) throw new VisitConflictError(message, conflicts);
    throw new Error(message);
  }
  invalidateAdminCache();
  return method === 'DELETE' ? undefined as T : await response.json() as T;
}

// Assignment failures must reach the UI; the generic admin boolean helpers are
// retained for other pages. Roster reads bypass their 30-second in-memory cache.
async function shiftAssignmentRequest<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const operation = method === 'GET' ? 'load assignments' : method === 'DELETE' ? 'remove employee' : 'assign employee';
  let response: Response;
  try {
    response = await authorizedFetch(path, { method, cache: 'no-store',
      ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  } catch {
    throw new Error(`Could not confirm the operation. Refresh assignments before retrying to ${operation}.`);
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(response.status < 500 ? problemText(problem, `Could not ${operation}. Please retry.`) : `Could not ${operation}. Please retry.`);
  }
  if (method !== 'GET') { invalidateAdminCache(); return undefined as T; }
  try {
    const data = await response.json();
    if (!Array.isArray(data) || data.some(a => !a || typeof a.employeeId !== 'string' || typeof a.isActive !== 'boolean')) throw new Error('Invalid roster');
    return data as T;
  }
  catch { throw new Error('Could not load assignments. Please retry.'); }
}

// One request for every shift's active assignment count, so the shift list never fans out per shift.
async function shiftAssignmentCounts(): Promise<Record<string, number>> {
  let response: Response;
  try { response = await authorizedFetch('/api/v1/admin/shifts/assignment-counts', { cache: 'no-store' }); }
  catch { throw new Error('Could not load employee counts. Refresh the page to retry.'); }
  if (!response.ok) throw new Error('Could not load employee counts. Refresh the page to retry.');
  const data = await response.json().catch(() => null);
  if (!data || typeof data !== 'object' || Array.isArray(data) || Object.values(data).some(value => typeof value !== 'number'))
    throw new Error('Could not load employee counts. Refresh the page to retry.');
  return data as Record<string, number>;
}

export type ShiftTab = 'active' | 'completed' | 'inactive';
export interface ShiftPageResult { items: Shift[]; total: number; counts: { active: number; completed: number; inactive: number } }
export const adminShifts = {
  // One page of one tab, with counts for every tab. Never cached, so a save is visible in the counts at once.
  page: (params: { tab: ShiftTab; skip: number; take: number }) =>
    adminGet<ShiftPageResult>(`/api/v1/admin/shifts/page?${new URLSearchParams({ tab: params.tab, skip: String(params.skip), take: String(params.take) }).toString()}`, false),
  list: (companyId?: string, branchId?: string, includeInactive = false) => {
    const query = new URLSearchParams();
    if (companyId) query.set('companyId', companyId);
    if (branchId) query.set('branchId', branchId);
    if (includeInactive) query.set('includeInactive', 'true');
    const suffix = query.toString();
    return adminGet<Shift[]>(`/api/v1/admin/shifts${suffix ? `?${suffix}` : ''}`);
  },
  listForCustomer: (companyId: string) => adminGet<Shift[]>(`/api/v1/admin/shifts?companyId=${encodeURIComponent(companyId)}&includeInactive=true`, false),
  get: (id: string) => adminGet<Shift>(`/api/v1/admin/shifts/${id}`),
  create: (fields: unknown) => adminSendJsonResult<Shift>('/api/v1/admin/shifts', 'POST', fields),
  update: (id: string, fields: unknown) =>
    adminSendJsonResult<Shift>(`/api/v1/admin/shifts/${id}`, 'PUT', fields),
  setQualityCycleTemplate: (id: string, templateId: string | null) => adminSendJson<Shift>(`/api/v1/admin/shifts/${id}/quality-cycle-template`, "PUT", { templateId }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/shifts/${id}/deactivate`, 'POST'),
  reactivate: (id: string) => adminSendJson<Shift>(`/api/v1/admin/shifts/${id}/reactivate`, 'POST'),
  deleteInactive: (id: string) => adminDelete(`/api/v1/admin/shifts/${id}`),
  assignmentCounts: shiftAssignmentCounts,
  employeeSchedule: (fromDate: string, toDate: string) =>
    adminGet<EmployeeDaySchedule[]>(`/api/v1/admin/shifts/employee-schedule?from=${fromDate}T00:00:00Z&to=${toDate}T00:00:00Z`, false),
  overlapAudit: (fromDate: string, toDate: string) =>
    adminGet<OverlapFinding[]>(`/api/v1/admin/shifts/overlap-audit?from=${fromDate}T00:00:00Z&to=${toDate}T00:00:00Z`, false),
  assignments: (id: string) =>
    shiftAssignmentRequest<ShiftAssignment[]>(`/api/v1/admin/shifts/${id}/assignments`),
  assign: (id: string, employeeId: string) =>
    shiftAssignmentRequest<void>(`/api/v1/admin/shifts/${id}/assignments`, 'POST', { employeeId }),
  unassign: (id: string, employeeId: string) =>
    shiftAssignmentRequest<void>(`/api/v1/admin/shifts/${id}/assignments/${employeeId}`, 'DELETE'),
  occurrences: (id: string, fromUtc: string, toUtc: string) =>
    adminGet<ShiftOccurrence[]>(
      `/api/v1/admin/shifts/${id}/occurrences?from=${encodeURIComponent(fromUtc)}&to=${encodeURIComponent(toUtc)}`
    ),
  setVisitAssignees: (id: string, fields: VisitAssigneesInput) =>
    occurrenceRequest<VisitAssignmentResult>(`/api/v1/admin/shifts/${id}/visit-assignees`, 'PUT', fields),
  saveOccurrenceException: (id: string, fields: ShiftOccurrenceExceptionInput) =>
    occurrenceRequest<ShiftOccurrence>(`/api/v1/admin/shifts/${id}/occurrence-exceptions`, 'PUT', fields),
  deleteOccurrenceException: (id: string, exceptionId: string) =>
    occurrenceRequest<void>(`/api/v1/admin/shifts/${id}/occurrence-exceptions/${exceptionId}`, 'DELETE'),
};

// Keep company/branch failures visible without changing legacy helper semantics.
async function companyRequest<T>(path: string, method: 'POST' | 'PUT', body: unknown): Promise<T> {
  const response = await authorizedFetch(path, { method, body: JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(response.status < 500 ? problemText(problem, 'Could not save. Please retry.') : 'Could not save. Please retry.');
  }
  const result = await response.json() as T;
  invalidateAdminCache();
  return result;
}

export const adminCompanies = {
  list: (search = '', skip = 0, take = 100, status: 'all' | 'active' | 'inactive' = 'all') =>
    adminGet<{ items: Company[]; total: number }>(
      `/api/v1/admin/companies?search=${encodeURIComponent(search)}&skip=${skip}&take=${take}&status=${status}`,
      false
    ),
  summary: () => adminGet<{ all: number; active: number; inactive: number }>('/api/v1/admin/companies/summary', false),
  listAll: async (): Promise<Company[] | null> => {
    const records: Company[] = [];
    const ids = new Set<string>();
    let expectedTotal: number | undefined;
    for (let skip = 0; ; ) {
      let page: { items: Company[]; total: number };
      try {
        const response = await authorizedFetch(`/api/v1/admin/companies?search=&skip=${skip}&take=100`, { cache: 'no-store' });
        if (!response.ok) return null;
        page = await response.json();
      } catch { return null; }
      if (!page || !Array.isArray(page.items) || !Number.isInteger(page.total) || page.total < 0) return null;
      expectedTotal ??= page.total;
      if (page.total !== expectedTotal) return null;
      for (const item of page.items) {
        if (!item || typeof item.id !== 'string' || typeof item.isActive !== 'boolean' || ids.has(item.id)) return null;
        ids.add(item.id); records.push(item);
      }
      skip += page.items.length;
      if (skip >= expectedTotal) return skip === expectedTotal ? records : null;
      if (page.items.length === 0) return null;
    }
  },
  get: (id: string) => adminGet<CompanyDetail>(`/api/v1/admin/companies/${id}`),
  create: (body: {
    isPartnerEnabled?: boolean;
    businessId: string;
    name: string;
    contactName?: string;
    contactEmail?: string;
    contactPhone?: string;
    notes?: string;
  }) => companyRequest<Company>('/api/v1/admin/companies', 'POST', body),
  update: (
    id: string,
    body: {
      isPartnerEnabled?: boolean;
      businessId: string;
      name: string;
      contactName?: string;
      contactEmail?: string;
      contactPhone?: string;
      notes?: string;
      isActive: boolean;
    }
  ) => companyRequest<Company>(`/api/v1/admin/companies/${id}`, 'PUT', { id, ...body }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/companies/${id}/deactivate`, 'POST'),
  deleteInactive: (id: string) => adminDelete(`/api/v1/admin/companies/${id}`),
};

export const adminBranches = {
  create: (
    companyId: string,
    body: {
      name: string;
      street?: string;
      postalCode?: string;
      city?: string;
      country?: string;
      contactPhone?: string;
      contactEmail?: string;
      roomCount?: number | null; bedCount?: number | null; priceNet?: number | null; vatRatePercent?: number | null; estimatedDurationMinutes?: number | null;
    }
  ) => companyRequest<Branch>(`/api/v1/admin/companies/${companyId}/branches`, 'POST', { companyId, ...body }),
  update: (
    id: string,
    body: {
      name: string;
      street?: string;
      postalCode?: string;
      city?: string;
      country?: string;
      contactPhone?: string;
      contactEmail?: string;
      roomCount?: number | null; bedCount?: number | null; priceNet?: number | null; vatRatePercent?: number | null; estimatedDurationMinutes?: number | null;
      isActive: boolean;
    }
  ) => adminSendJson<Branch>(`/api/v1/admin/companies/branches/${id}`, 'PUT', { id, ...body }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/companies/branches/${id}/deactivate`, 'POST'),
  deleteInactive: (id: string) => adminDelete(`/api/v1/admin/companies/branches/${id}`),
};
