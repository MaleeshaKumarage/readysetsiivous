import { API_URL } from './api';
import { adminGet, adminSend, adminSendJson } from './adminApi';
import { token } from './auth';

export interface QualityCycleTemplate {
  id: string;
  companyId?: string | null;
  branchId?: string | null;
  title: string;
  description?: string | null;
  items: string[];
  isActive: boolean;
  createdUtc: string;
  updatedUtc: string;
}

export interface QualityCycleFormItem {
  itemText: string;
  isChecked: boolean;
}

export interface QualityCycleForm {
  id: string;
  shiftId: string;
  shiftName: string;
  employeeId: string;
  employeeName: string;
  templateId: string;
  templateTitle: string;
  shiftOccurrenceUtc: string;
  shiftOccurrenceEndUtc: string;
  token: string;
  items: QualityCycleFormItem[];
  photoUrls: string[];
  cleanerNotes?: string | null;
  isSubmitted: boolean;
  submittedUtc?: string | null;
  startedAtUtc?: string | null;
  endedAtUtc?: string | null;
  createdUtc: string;
}

export const adminQualityCycle = {
  listTemplates: (companyId?: string, branchId?: string) => {
    const query = new URLSearchParams();
    if (companyId) query.set('companyId', companyId);
    if (branchId) query.set('branchId', branchId);
    const suffix = query.toString();
    return adminGet<QualityCycleTemplate[]>(`/api/v1/admin/quality-cycle/templates${suffix ? `?${suffix}` : ''}`);
  },
  getTemplate: (id: string) => adminGet<QualityCycleTemplate>(`/api/v1/admin/quality-cycle/templates/${id}`),
  createTemplate: (fields: { title: string; items: string[]; description?: string; companyId?: string; branchId?: string }) =>
    adminSendJson<QualityCycleTemplate>('/api/v1/admin/quality-cycle/templates', 'POST', fields),
  updateTemplate: (id: string, fields: { title: string; items: string[]; description?: string; isActive?: boolean; companyId?: string; branchId?: string }) =>
    adminSendJson<QualityCycleTemplate>(`/api/v1/admin/quality-cycle/templates/${id}`, 'PUT', fields),
  deleteTemplate: (id: string) => adminSend(`/api/v1/admin/quality-cycle/templates/${id}`, 'DELETE'),

  listForms: (shiftId?: string, from?: string, to?: string) => {
    const query = new URLSearchParams();
    if (shiftId) query.set('shiftId', shiftId);
    if (from) query.set('from', from);
    if (to) query.set('to', to);
    const suffix = query.toString();
    return adminGet<QualityCycleForm[]>(`/api/v1/admin/quality-cycle/forms${suffix ? `?${suffix}` : ''}`);
  },

  downloadSummaryPdf: async (shiftId: string, year: number, month: number): Promise<void> => {
    const t = token();
    const url = `${API_URL}/api/v1/admin/quality-cycle/summary-pdf?shiftId=${shiftId}&year=${year}&month=${month}`;
    const response = await fetch(url, {
      headers: { Authorization: `Bearer ${t}` },
    });
    if (!response.ok) return;
    const blob = await response.blob();
    const blobUrl = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = blobUrl;
    a.download = `QualityCycle_Summary_${shiftId}_${year}_${month.toString().padStart(2, '0')}.pdf`;
    a.click();
    URL.revokeObjectURL(blobUrl);
  },
};

export const publicQualityCycle = {
  getByToken: async (tenantSlug: string, formToken: string): Promise<QualityCycleForm | null> => {
    const response = await fetch(`${API_URL}/api/v1/public/${tenantSlug}/quality-cycle/${formToken}`);
    if (!response.ok) return null;
    return (await response.json()) as QualityCycleForm;
  },
  submit: async (
    tenantSlug: string,
    formToken: string,
    items: QualityCycleFormItem[],
    photoUrls?: string[],
    cleanerNotes?: string
  ): Promise<QualityCycleForm | null> => {
    const response = await fetch(`${API_URL}/api/v1/public/${tenantSlug}/quality-cycle/${formToken}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: formToken, items, photoUrls, cleanerNotes }),
    });
    if (!response.ok) return null;
    return (await response.json()) as QualityCycleForm;
  },
};
