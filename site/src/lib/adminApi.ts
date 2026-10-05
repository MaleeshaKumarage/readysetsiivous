// Admin API client: attaches the in-memory Keycloak bearer token.

import { API_URL } from './api';
import { token, refreshToken, login } from './auth';

export interface Booking {
  id: string;
  bookingNumber: string;
  customer: { name: string; phone: string; email: string };
  service: { nameFi: string; durationMinutes: number; priceNet: number };
  startLocalDate: string;
  startLocalTime: string;
  status: string;
  employeeName?: string | null;
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

export interface Employee {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  phone: string;
  role: string;
  isActive: boolean;
  colorHex?: string | null;
  skills: string[];
  serviceAreas: string[];
  payRate?: number | null;
  certifications: { name: string; expiresAtUtc?: string | null }[];
  notes?: string | null;
  defaultHours: Record<string, { start?: string | null; end?: string | null }>;
}

async function authorizedFetch(path: string, init: RequestInit = {}): Promise<Response> {
  let t = token();
  if (!t) t = await refreshToken();
  if (!t) await login();

  let response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      ...(init.headers ?? {}),
      Authorization: `Bearer ${t}`,
      ...(init.body ? { "Content-Type": "application/json" } : {}),
    },
  });

  if (response.status === 401) {
    t = await refreshToken();
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: {
        ...(init.headers ?? {}),
        Authorization: `Bearer ${t}`,
        ...(init.body ? { "Content-Type": "application/json" } : {}),
      },
    });
  }

  return response;
}

// In-memory GET cache so navigating between admin pages does not re-fetch
// unchanged lists. Mutations clear it.
const _cache = new Map<string, { data: unknown; ts: number }>();
const _CACHE_TTL = 30_000;

function _invalidate() {
  _cache.clear();
}

export async function adminGet<T>(path: string): Promise<T | null> {
  const hit = _cache.get(path);
  if (hit && Date.now() - hit.ts < _CACHE_TTL) return hit.data as T;
  const response = await authorizedFetch(path);
  if (!response.ok) return null;
  const data = (await response.json()) as T;
  _cache.set(path, { data, ts: Date.now() });
  return data;
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
  if (ok) _invalidate();
  return ok;
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
  _invalidate();
  return data;
}

export const adminBookings = {
  list: (query = '') => adminGet<Booking[]>(`/api/v1/admin/bookings${query}`),
  confirm: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/confirm`, 'POST', {}),
  cancel: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/cancel`, 'POST', {}),
  complete: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/complete`, 'POST', {}),
  assign: (id: string, employeeId: string) =>
    adminSend(`/api/v1/admin/bookings/${id}/assign`, 'POST', { employeeId }),
  unassign: (id: string) => adminSend(`/api/v1/admin/bookings/${id}/unassign`, 'POST'),
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
  create: (fields: ServiceFields) => adminSend('/api/v1/admin/services', 'POST', { fields }),
  update: (id: string, fields: ServiceFields, isActive: boolean) =>
    adminSend(`/api/v1/admin/services/${id}`, 'PUT', { fields, isActive }),
  remove: (id: string) => adminSend(`/api/v1/admin/services/${id}`, 'DELETE'),
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
  colorHex?: string | null;
  defaultHours: Record<string, { start?: string | null; end?: string | null }>;
  skills?: string[];
  serviceAreas?: string[];
  payRate?: number | null;
  certifications?: { name: string; expiresAtUtc?: string | null }[];
  notes?: string | null;
}

export const adminEmployees = {
  list: () => adminGet<Employee[]>('/api/v1/admin/employees'),
  create: (fields: EmployeeFields) => adminSend('/api/v1/admin/employees', 'POST', { fields }),
  update: (id: string, fields: EmployeeFields, isActive: boolean) =>
    adminSend(`/api/v1/admin/employees/${id}`, 'PUT', { fields, isActive }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/employees/${id}/deactivate`, 'POST'),
  invite: (id: string) =>
    adminSendJson<{ email: string; temporaryPassword: string }>(
      `/api/v1/admin/employees/${id}/invite`,
      'POST'
    ),
};

export interface AgreementSignerDto {
  id: string; name: string; email: string; status: string; token: string;
}
export interface AgreementListItem {
  id: string; title: string; code: string; status: string; isActive: boolean; signerCount: number; signedCount: number; createdUtc: string;
}
export interface AgreementDetail extends AgreementListItem {
  signers: AgreementSignerDto[];
  completedUtc: string | null;
}

export const adminTenant = {
  get: () => adminGet<{ companyName: string }>('/api/v1/admin/tenant'),
};

export const adminAgreements = {
  list: () => adminGet<AgreementListItem[]>('/api/v1/admin/agreements'),
  get: (id: string) => adminGet<AgreementDetail>(`/api/v1/admin/agreements/${id}`),
  create: async (title: string, companyId: string, pdf: File, signers: { name: string; email: string }[]) => {
    const form = new FormData();
    form.append('title', title);
    form.append('companyId', companyId);
    form.append('pdf', pdf);
    form.append('signersJson', JSON.stringify(signers));
    const t = token();
    const response = await fetch(`${API_URL}/api/v1/admin/agreements`, {
      method: 'POST', headers: { Authorization: `Bearer ${t}` }, body: form,
    });
    return response.ok ? ((await response.json()) as AgreementDetail) : null;
  },
  addSigner: (id: string, name: string, email: string) =>
    adminSend(`/api/v1/admin/agreements/${id}/signers`, 'POST', { name, email }),
  removeSigner: (id: string, signerId: string) =>
    adminSend(`/api/v1/admin/agreements/${id}/signers/${signerId}`, 'DELETE'),
  cancel: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/cancel`, 'POST'),
  activate: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/activate`, 'POST'),
  deactivate: (id: string) => adminSend(`/api/v1/admin/agreements/${id}/deactivate`, 'POST'),
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
  id: string;
  companyId: string;
  name: string;
  street?: string | null;
  postalCode?: string | null;
  city?: string | null;
  country?: string | null;
  contactPhone?: string | null;
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
// so the wire values are 0..4 matching the server enum's declaration order.
export const ShiftScheduleType = {
  DailySameTime: 0,
  DailyDifferentTime: 1,
  Weekly: 2,
  BiWeekly: 3,
  OnCallFlexible: 4,
  Monthly: 5,
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
}

export interface ShiftAssignment {
  id: string;
  shiftId: string;
  employeeId: string;
  assignedAtUtc: string;
  isActive: boolean;
  note?: string | null;
}

export interface ShiftOccurrence {
  startUtc: string;
  endUtc: string;
}

export const adminShifts = {
  list: (companyId?: string, branchId?: string) => {
    const query = new URLSearchParams();
    if (companyId) query.set('companyId', companyId);
    if (branchId) query.set('branchId', branchId);
    const suffix = query.toString();
    return adminGet<Shift[]>(`/api/v1/admin/shifts${suffix ? `?${suffix}` : ''}`);
  },
  get: (id: string) => adminGet<Shift>(`/api/v1/admin/shifts/${id}`),
  create: (fields: unknown) => adminSendJson<Shift>('/api/v1/admin/shifts', 'POST', fields),
  update: (id: string, fields: unknown) =>
    adminSendJson<Shift>(`/api/v1/admin/shifts/${id}`, 'PUT', fields),
  deactivate: (id: string) => adminSend(`/api/v1/admin/shifts/${id}/deactivate`, 'POST'),
  assignments: (id: string) =>
    adminGet<ShiftAssignment[]>(`/api/v1/admin/shifts/${id}/assignments`),
  assign: (id: string, employeeId: string) =>
    adminSend(`/api/v1/admin/shifts/${id}/assignments`, 'POST', { employeeId }),
  unassign: (id: string, employeeId: string) =>
    adminSend(`/api/v1/admin/shifts/${id}/assignments/${employeeId}`, 'DELETE'),
  occurrences: (id: string, fromUtc: string, toUtc: string) =>
    adminGet<ShiftOccurrence[]>(
      `/api/v1/admin/shifts/${id}/occurrences?fromUtc=${encodeURIComponent(fromUtc)}&toUtc=${encodeURIComponent(toUtc)}`
    ),
};

export const adminCompanies = {
  list: (search = '', skip = 0, take = 100) =>
    adminGet<{ items: Company[]; total: number }>(
      `/api/v1/admin/companies?search=${encodeURIComponent(search)}&skip=${skip}&take=${take}`
    ),
  get: (id: string) => adminGet<CompanyDetail>(`/api/v1/admin/companies/${id}`),
  create: (body: {
    businessId: string;
    name: string;
    contactName?: string;
    contactEmail?: string;
    contactPhone?: string;
    notes?: string;
  }) => adminSendJson<Company>('/api/v1/admin/companies', 'POST', body),
  update: (
    id: string,
    body: {
      businessId: string;
      name: string;
      contactName?: string;
      contactEmail?: string;
      contactPhone?: string;
      notes?: string;
      isActive: boolean;
    }
  ) => adminSendJson<Company>(`/api/v1/admin/companies/${id}`, 'PUT', { id, ...body }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/companies/${id}/deactivate`, 'POST'),
};

export const adminBranches = {
  list: (companyId: string) => adminGet<Branch[]>(`/api/v1/admin/companies/${companyId}/branches`),
  create: (
    companyId: string,
    body: {
      name: string;
      street?: string;
      postalCode?: string;
      city?: string;
      country?: string;
      contactPhone?: string;
    }
  ) => adminSendJson<Branch>(`/api/v1/admin/companies/${companyId}/branches`, 'POST', { companyId, ...body }),
  update: (
    id: string,
    body: {
      name: string;
      street?: string;
      postalCode?: string;
      city?: string;
      country?: string;
      contactPhone?: string;
      isActive: boolean;
    }
  ) => adminSendJson<Branch>(`/api/v1/admin/companies/branches/${id}`, 'PUT', { id, ...body }),
  deactivate: (id: string) => adminSend(`/api/v1/admin/companies/branches/${id}/deactivate`, 'POST'),
};
