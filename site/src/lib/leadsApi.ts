import { API_URL } from './api';
import { authorizedFetch, invalidateAdminCache } from './adminApi';
import { token, refreshToken } from './auth';
export interface LeadBranchFields { id: string; name: string; street: string; postalCode: string; city: string; country: string; contactPhone: string; contactEmail: string }
export function leadBranches(fields: LeadFields, leadId: string): LeadBranchFields[] {
  if (fields.branches) return fields.branches;
  if (![fields.branchName, fields.street, fields.postalCode, fields.city, fields.branchPhone, fields.branchEmail].some(value => value?.trim())) return [];
  return [{ id: leadId, name: fields.branchName || '', street: fields.street || '', postalCode: fields.postalCode || '', city: fields.city || '', country: fields.country || '', contactPhone: fields.branchPhone || '', contactEmail: fields.branchEmail || '' }];
}
export interface LeadFields {
  branches?: LeadBranchFields[];
  name: string; businessId: string; contactName: string; contactPhone: string; notes: string; description: string;
  emails: string[]; primaryEmail: string; status: string; branchName: string; street: string; postalCode: string; city: string;
  country: string; branchPhone: string; branchEmail: string;
}
export const emptyLead: LeadFields = { branches: [], name: '', businessId: '', contactName: '', contactPhone: '', notes: '', description: '', emails: [], primaryEmail: '', status: 'New', branchName: '', street: '', postalCode: '', city: '', country: '', branchPhone: '', branchEmail: '' };
export interface LeadRow { id: string; version: string; fields: LeadFields; isActive: boolean; status: string; imageCount: number; companyId: string | null; branchId: string | null; convertedUtc: string | null; updatedUtc: string; convertedBranches?: { sourceBranchId: string; companyBranchId: string }[] }
export interface LeadDetail { lead: LeadRow; images: { id: string; extension: string }[]; customerAvailable: boolean }
const root = '/api/v1/admin/leads';
async function read<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const messages: string[] = Object.values(problem.errors ?? {}).flat() as string[];
    throw new Error(messages.join(' ') || problem.detail || problem.title || `Request failed (${response.status}). Please retry.`);
  }
  if (response.status === 204) return undefined as T;
  const data = await response.json();
  if (data?.lead?.fields) data.lead.fields = { ...data.lead.fields, branches: leadBranches(data.lead.fields, data.lead.id) };
  return data;
}
async function mutate<T>(path: string, method: string, body: unknown): Promise<T> {
  const result = await read<T>(await authorizedFetch(root + path, { method, body: JSON.stringify(body) })); invalidateAdminCache(); return result;
}
export const leadsApi = {
  list: async (search: string, status: string, skip: number) => read<{ items: LeadRow[]; total: number }>(await authorizedFetch(`${root}?search=${encodeURIComponent(search)}&status=${encodeURIComponent(status)}&skip=${skip}&take=25`)),
  get: async (id: string) => read<LeadDetail>(await authorizedFetch(`${root}/${id}`)),
  create: (fields: LeadFields) => mutate<LeadDetail>('', 'POST', fields),
  update: (lead: LeadRow, fields: LeadFields) => mutate<LeadDetail>(`/${lead.id}`, 'PUT', { version: lead.version, fields }),
  activity: (lead: LeadRow) => mutate<LeadDetail>(`/${lead.id}/${lead.isActive ? 'deactivate' : 'activate'}`, 'POST', { version: lead.version }),
  remove: (lead: LeadRow) => mutate<void>(`/${lead.id}`, 'DELETE', { version: lead.version }),
  convert: (lead: LeadRow) => mutate<LeadDetail>(`/${lead.id}/convert`, 'POST', { version: lead.version }),
  removeImage: (lead: LeadRow, imageId: string) => mutate<LeadDetail>(`/${lead.id}/images/${imageId}`, 'DELETE', { version: lead.version }),
  image: async (leadId: string, imageId: string) => { const response = await authorizedFetch(`${root}/${leadId}/images/${imageId}`); if (!response.ok) await read(response); return response.blob(); },
  upload: async (lead: LeadRow, file: File) => {
    const form = new FormData(); form.append('file', file); form.append('version', lead.version);
    // FormData must use the browser-generated multipart boundary, not the JSON API header.
    const send = (bearer: string | undefined) => fetch(API_URL + root + `/${lead.id}/images`, { method: 'POST', headers: { Authorization: `Bearer ${bearer ?? ''}` }, body: form });
    let response = await send(token() || await refreshToken());
    if (response.status === 401) response = await send(await refreshToken());
    const result = await read<LeadDetail>(response); invalidateAdminCache(); return result;
  },
};
