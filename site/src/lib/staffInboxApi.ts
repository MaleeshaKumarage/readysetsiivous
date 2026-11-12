import { authorizedFetch } from './adminApi';

export interface StaffInboxEntry { id: string; kind: string; title: string; body: string; link: string; createdUtc: string; read: boolean }
export interface StaffInboxPage { unread: number; items: StaffInboxEntry[] }

async function inboxRequest<T>(path: string, method = 'GET'): Promise<T> {
  const response = await authorizedFetch(`/api/v1/me/inbox${path}`, { method, cache: 'no-store' });
  if (!response.ok) throw new Error(`Could not load notifications (${response.status}). Refresh to retry.`);
  return response.json();
}

export const staffInboxApi = {
  list: () => inboxRequest<StaffInboxPage>(''),
  markRead: (id: string) => inboxRequest<{ read: boolean }>(`/${encodeURIComponent(id)}/read`, 'POST'),
};
