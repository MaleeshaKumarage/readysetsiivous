import { API_URL } from './api';
import { authorizedFetch } from './adminApi';
import { refreshToken, token } from './auth';
import type { LeadDetail, LeadRow } from './leadsApi';
export interface VoiceNote { id: string; version: string; authorName: string; createdUtc: string; durationSeconds: number; contentType: string }
export interface FollowUpInput { reason: string; firstDate: string; endDate: string | null; recurrence: string; interval: number; recipientIds: string[] }
export interface FollowUp extends FollowUpInput { id: string; version: string; state: string; nextDate: string | null; authorName: string; lastError?: string | null; lastCheckedUtc?: string | null }
export interface Delivery { id: string; followUpId: string; recipientName: string; recipientEmail: string; localDate: string; timeZoneId: string; dueUtc: string; state: string; attempts: number; sentUtc: string | null; error: string | null }
export interface LeadAdmin { id: string; name: string; email: string }
export interface Page<T> { items: T[]; total: number }
export interface CollaborationDetail extends LeadDetail { capabilities: { canEditMetadata: boolean; canRecord: boolean; canSchedule: boolean; canDeleteVoice: boolean } }
const root = '/api/v1/manager/leads';
async function read<T>(response: Response): Promise<T> {
  if (!response.ok) { const problem = await response.json().catch(() => ({})); throw new Error((Object.values(problem.errors ?? {}).flat() as string[]).join(' ') || problem.detail || problem.title || `Request failed (${response.status}). Please retry.`); }
  return response.status === 204 ? undefined as T : response.json();
}
async function get<T>(path: string) { return read<T>(await authorizedFetch(root + path, { cache: 'no-store' })); }
async function send<T>(path: string, method: string, body: unknown) { return read<T>(await authorizedFetch(root + path, { method, body: JSON.stringify(body) })); }
export const leadCollaborationApi = {
  list: (search: string, status: string, skip: number) => get<Page<LeadRow>>(`?search=${encodeURIComponent(search)}&status=${encodeURIComponent(status)}&skip=${skip}&take=25`),
  detail: (id: string) => get<CollaborationDetail>(`/${id}`),
  settings: () => get<{ timeZoneId: string }>('/settings'),
  recipients: () => get<LeadAdmin[]>('/recipients'),
  voices: (id: string, skip: number) => get<Page<VoiceNote>>(`/${id}/voice-notes?skip=${skip}&take=25`),
  followUps: (id: string, skip: number) => get<Page<FollowUp>>(`/${id}/follow-ups?skip=${skip}&take=25`),
  history: (id: string, skip: number) => get<Page<Delivery>>(`/${id}/deliveries?skip=${skip}&take=25`),
  saveFollowUp: (id: string, input: FollowUpInput, existing?: FollowUp) => existing ? send<FollowUp>(`/${id}/follow-ups/${existing.id}`, 'PUT', { version: existing.version, input }) : send<FollowUp>(`/${id}/follow-ups`, 'POST', input),
  cancel: (id: string, follow: FollowUp) => send<void>(`/${id}/follow-ups/${follow.id}/cancel`, 'POST', { version: follow.version }),
  removeVoice: (id: string, note: VoiceNote) => send<void>(`/${id}/voice-notes/${note.id}`, 'DELETE', { version: note.version }),
  image: async (id: string, imageId: string) => { const response = await authorizedFetch(`${root}/${id}/images/${imageId}`); if (!response.ok) await read(response); return response.blob(); },
  audio: async (id: string, note: VoiceNote) => { const response = await authorizedFetch(`${root}/${id}/voice-notes/${note.id}/audio`); if (!response.ok) await read(response); return response.blob(); },
  saveRecording: async (id: string, recordingId: string, blob: Blob) => {
    const form = new FormData(); form.append('file', blob, 'recording');
    const upload = (bearer: string | undefined) => fetch(`${API_URL}${root}/${id}/voice-notes/${recordingId}`, { method: 'POST', headers: { Authorization: `Bearer ${bearer ?? ''}` }, body: form });
    let response = await upload(token() || await refreshToken()); if (response.status === 401) response = await upload(await refreshToken());
    return read<VoiceNote>(response);
  },
};
