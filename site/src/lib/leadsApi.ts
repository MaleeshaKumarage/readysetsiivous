import { authorizedFetch, adminGet, adminDelete, invalidateAdminCache } from './adminApi';

export interface LeadContactPerson {
  name: string;
  email?: string | null;
  phone?: string | null;
  role?: string | null;
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

async function leadRequest<T>(path: string, method: 'POST' | 'PUT', body?: unknown): Promise<T> {
  const response = await authorizedFetch(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
    throw new Error(problem.detail || problem.title || `Lead request failed (${response.status}). Please retry.`);
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
};
