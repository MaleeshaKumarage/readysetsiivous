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

export async function adminGet<T>(path: string): Promise<T | null> {
  const response = await authorizedFetch(path);
  if (!response.ok) return null;
  return (await response.json()) as T;
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
  return response.ok || response.status === 204;
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
  return (await response.json()) as T;
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

export const adminServices = {
  list: (includeInactive = false) =>
    adminGet<{ id: string; slug: string; category: string; name: { values: Record<string, string> }; description: { values: Record<string, string> }; additionalInfo: { values: Record<string, string> } | null; icon: string; imageUrl: string; durationMinutes: number; priceNet: number; vatRatePercent: number; isActive: boolean; isFeatured: boolean; sortOrder: number }[]>(
      `/api/v1/admin/services?includeInactive=${includeInactive}`
    ),
  create: (fields: unknown) => adminSend('/api/v1/admin/services', 'POST', { fields }),
  update: (id: string, fields: unknown, isActive: boolean) =>
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

export const adminEmployees = {
  list: () => adminGet<Employee[]>('/api/v1/admin/employees'),
  create: (fields: unknown) => adminSend('/api/v1/admin/employees', 'POST', { fields }),
  invite: (id: string) =>
    adminSendJson<{ email: string; temporaryPassword: string }>(
      `/api/v1/admin/employees/${id}/invite`,
      'POST'
    ),
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
