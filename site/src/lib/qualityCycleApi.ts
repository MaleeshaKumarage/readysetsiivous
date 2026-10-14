import { API_URL } from './api';
import { adminGet, adminSend, adminSendJson } from './adminApi';
import { refreshToken } from './auth';
import type { ShiftPhoto } from './shiftPhotos';

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
  photos?: ShiftPhoto[];
  cleanerNotes?: string | null;
  taskStatus?: "Pending" | "Recorded";
  isSubmitted: boolean;
  submittedUtc?: string | null;
  startedAtUtc?: string | null;
  endedAtUtc?: string | null;
  createdUtc: string;
}

export interface ReportShare { id: string; token: string; tenantId: string; expiresUtc: string; isRevoked: boolean; year: number; month: number; }
export function reportShareUrl(share: ReportShare): string {
  return `${API_URL}/api/v1/public/${encodeURIComponent(share.tenantId)}/quality-cycle/reports/${encodeURIComponent(share.token)}/pdf`;
}
export const adminQualityCycle = {
  createReportShare: (year: number, month: number, shiftId?: string, branchId?: string) =>
    adminSendJson<ReportShare>('/api/v1/admin/quality-cycle/report-shares', 'POST', { year, month, shiftId, branchId }),
  listReportShares: () => adminGet<ReportShare[]>('/api/v1/admin/quality-cycle/report-shares'),
  revokeReportShare: (id: string) => adminSend(`/api/v1/admin/quality-cycle/report-shares/${id}`, 'DELETE'),
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

  monthlyReport: (year: number, month: number, companyId?: string, branchId?: string, shiftId?: string) => {
    const query = new URLSearchParams({ year: String(year), month: String(month) });
    if (companyId) query.set('companyId', companyId);
    if (branchId) query.set('branchId', branchId);
    if (shiftId) query.set('shiftId', shiftId);
    return adminGet<MonthlyReport>(`/api/v1/admin/quality-cycle/monthly-summary?${query}`);
  },
  downloadSummaryPdf: (shiftId: string, year: number, month: number) => downloadReport('summary-pdf', 'shiftId', shiftId, year, month),
  downloadBranchPdf: (branchId: string, year: number, month: number) => downloadReport('branch-summary-pdf', 'branchId', branchId, year, month),
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
    photos?: ShiftPhoto[],
    cleanerNotes?: string
  ): Promise<QualityCycleForm | null> => {
    const response = await fetch(`${API_URL}/api/v1/public/${tenantSlug}/quality-cycle/${formToken}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: formToken, items, photos, cleanerNotes }),
    });
    if (!response.ok) return null;
    return (await response.json()) as QualityCycleForm;
  },
};

export interface ShiftReport {
  shiftId: string; shiftName: string; companyId: string; companyName: string; branchId: string; branchName: string;
  scheduledOccurrences: number; scheduledHours: number; completedOccurrences: number; missingOccurrences: number;
  pendingOccurrences: number; totalTasks: number; submittedTasks: number; missingTasks: number; pendingTasks: number; items: { itemText: string; daysPerformed: number; recordedOccurrences: number; recordedDates: string[] }[];
}
export interface MonthlyReport { year: number; month: number; timeZoneId: string; generatedUtc: string; shifts: ShiftReport[]; }
async function downloadReport(route: string, key: string, id: string, year: number, month: number): Promise<void> {
  const jwt = await refreshToken();
  if (!jwt) throw new Error('Please sign in again.');
  const query = new URLSearchParams({ [key]: id, year: String(year), month: String(month) });
  const response = await fetch(`${API_URL}/api/v1/admin/quality-cycle/${route}?${query}`, { headers: { Authorization: `Bearer ${jwt}` } });
  if (!response.ok) throw new Error(`Report download failed (${response.status}).`);
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement('a'); a.href = url; a.download = `QualityCycle_${key === 'shiftId' ? 'Summary' : 'Branch'}_${id}_${year}_${String(month).padStart(2, '0')}.pdf`; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
