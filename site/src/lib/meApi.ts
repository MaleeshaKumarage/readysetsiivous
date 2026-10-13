import { adminGet, adminSendJson } from './adminApi';

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
  myShifts: (from?: string, to?: string): Promise<MyShiftOccurrence[] | null> => {
    const q = new URLSearchParams();
    if (from) q.set('from', from);
    if (to) q.set('to', to);
    const suffix = q.toString();
    return adminGet<MyShiftOccurrence[]>(`/api/v1/me/shifts${suffix ? `?${suffix}` : ''}`);
  },

  startShift: (shiftId: string, occurrenceStartUtc: string, occurrenceEndUtc: string) =>
    adminSendJson<QualityCycleForm>(`/api/v1/me/shifts/${shiftId}/start`, 'POST', {
      occurrenceStartUtc,
      occurrenceEndUtc,
    }),

  endForm: (formId: string, items: QualityCycleFormItem[], photoUrls?: string[], cleanerNotes?: string) =>
    adminSendJson<QualityCycleForm>(`/api/v1/me/quality-cycle/${formId}/end`, 'POST', {
      items,
      photoUrls,
      cleanerNotes,
    }),
};
