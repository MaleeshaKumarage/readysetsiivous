import { authorizedFetch } from './adminApi';

async function clockRequest(path: string, body: unknown): Promise<QualityCycleForm> {
  const response = await authorizedFetch(path, { method: 'POST', body: JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || (response.status === 403 ? 'Your access or shift assignment changed. Refresh your shifts or contact your tenant administrator.' : problem.title) || 'Please retry.');
  }
  return await response.json() as QualityCycleForm;
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

export interface MyShiftOccurrence {
  shiftId: string;
  shiftName: string;
  startUtc: string;
  endUtc: string;
  form: QualityCycleForm | null;
}

export const meApi = {
  myShifts: async (from?: string, to?: string): Promise<MyShiftOccurrence[]> => {
    const q = new URLSearchParams();
    if (from) q.set('from', from);
    if (to) q.set('to', to);
    const suffix = q.toString();
    const response = await authorizedFetch(`/api/v1/me/shifts${suffix ? `?${suffix}` : ''}`, { cache: 'no-store' });
    if (!response.ok) throw new Error('Could not load your assigned shifts.');
    return await response.json() as MyShiftOccurrence[];
  },

  startShift: (shiftId: string, occurrenceStartUtc: string, occurrenceEndUtc: string) =>
    clockRequest(`/api/v1/me/shifts/${shiftId}/start`, {
      occurrenceStartUtc,
      occurrenceEndUtc,
    }),

  endForm: (formId: string, items: QualityCycleFormItem[], photoUrls?: string[], cleanerNotes?: string) =>
    clockRequest(`/api/v1/me/quality-cycle/${formId}/end`, {
      items,
      photoUrls,
      cleanerNotes,
    }),
};
