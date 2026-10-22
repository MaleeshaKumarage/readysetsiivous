import { authorizedFetch, adminGet, adminDelete, invalidateAdminCache } from './adminApi';
import { API_URL } from './api';
import { token } from './auth';

export interface LeadContactPerson {
  name: string;
  email?: string | null;
  phone?: string | null;
  role?: string | null;
}

export interface LeadAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface Lead {
  id: string;
  name: string;
  businessId?: string | null;
  mainContactName?: string | null;
  mainContactEmail?: string | null;
  mainContactPhone?: string | null;
  /** Extra emails beyond the main contact email */
  emails: string[];
  /** Additional contact persons (alongside main contact) */
  contactPersons: LeadContactPerson[];
  notes?: string | null;
  isActive: boolean;
  attachments?: LeadAttachment[];
}

export type LeadFields = {
  name: string;
  businessId?: string;
  mainContactName?: string;
  mainContactEmail?: string;
  mainContactPhone?: string;
  emails?: string[];
  contactPersons?: LeadContactPerson[];
  notes?: string;
};

async function problemMessage(response: Response, fallback: string): Promise<string> {
  const problem = await response.json().catch(() => ({})) as {
    detail?: string;
    title?: string;
    errors?: Record<string, string[]>;
  };
  const first = problem.errors ? Object.values(problem.errors).flat().find(Boolean) : undefined;
  return first || problem.detail || problem.title || fallback;
}

async function leadRequest<T>(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<T> {
  const response = await authorizedFetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(await problemMessage(response, `Lead request failed (${response.status}). Please retry.`));
  }
  invalidateAdminCache();
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const adminLeads = {
  list: (search = '', skip = 0, take = 100) =>
    adminGet<{ items: Lead[]; total: number }>(
      `/api/v1/admin/leads?search=${encodeURIComponent(search)}&skip=${skip}&take=${take}`
    ),
  listAll: async (): Promise<Lead[] | null> => {
    const records: Lead[] = [];
    const ids = new Set<string>();
    let expectedTotal: number | undefined;
    for (let skip = 0; ; ) {
      let page: { items: Lead[]; total: number };
      try {
        const response = await authorizedFetch(`/api/v1/admin/leads?search=&skip=${skip}&take=100`, { cache: 'no-store' });
        if (!response.ok) return null;
        page = await response.json();
      } catch {
        return null;
      }
      if (!page || !Array.isArray(page.items) || !Number.isInteger(page.total) || page.total < 0) return null;
      expectedTotal ??= page.total;
      if (page.total !== expectedTotal) return null;
      for (const item of page.items) {
        if (!item || typeof item.id !== 'string' || typeof item.isActive !== 'boolean' || ids.has(item.id)) return null;
        ids.add(item.id);
        records.push(item);
      }
      skip += page.items.length;
      if (skip >= expectedTotal) return skip === expectedTotal ? records : null;
      if (page.items.length === 0) return null;
    }
  },
  get: (id: string) => adminGet<Lead>(`/api/v1/admin/leads/${id}`),
  create: (body: LeadFields) => leadRequest<Lead>('/api/v1/admin/leads', 'POST', body),
  update: (id: string, body: LeadFields & { isActive: boolean }) =>
    leadRequest<Lead>(`/api/v1/admin/leads/${id}`, 'PUT', { id, ...body }),
  deactivate: (id: string) => leadRequest<void>(`/api/v1/admin/leads/${id}/deactivate`, 'POST'),
  reactivate: (id: string) => leadRequest<void>(`/api/v1/admin/leads/${id}/reactivate`, 'POST'),
  deleteInactive: (id: string) => adminDelete(`/api/v1/admin/leads/${id}`),
  uploadAttachment: async (id: string, file: File): Promise<LeadAttachment> => {
    const body = new FormData();
    body.append('file', file);
    const response = await fetch(`${API_URL}/api/v1/admin/leads/${id}/attachments`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token()}` },
      body,
    });
    if (!response.ok) {
      throw new Error(await problemMessage(response, 'Could not upload file. Please retry.'));
    }
    invalidateAdminCache();
    return (await response.json()) as LeadAttachment;
  },
  fetchAttachment: async (leadId: string, attachmentId: string): Promise<Blob> => {
    const response = await fetch(`${API_URL}/api/v1/admin/leads/${leadId}/attachments/${attachmentId}`, {
      headers: { Authorization: `Bearer ${token()}` },
    });
    if (!response.ok) throw new Error('Could not load file.');
    return response.blob();
  },
  removeAttachment: (leadId: string, attachmentId: string) =>
    adminDelete(`/api/v1/admin/leads/${leadId}/attachments/${attachmentId}`),
};
