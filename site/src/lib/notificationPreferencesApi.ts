import { authorizedFetch } from './adminApi';

export interface NotificationEvent { key: string; group: 'Cleaner' | 'Staff' | string; label: string; description: string; app: boolean; emailAvailable: boolean }
export interface NotificationPreferences { events: NotificationEvent[] }

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await authorizedFetch(`/api/v1/me/notification-preferences${path}`, {
    method, cache: 'no-store', headers: body === undefined ? undefined : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
    throw new Error(problem.detail || problem.title || `Could not save notification settings (${response.status}). Try again.`);
  }
  return response.json();
}

export const notificationPreferencesApi = {
  get: () => request<NotificationPreferences>(''),
  setApp: (key: string, app: boolean) => request<NotificationPreferences>(`/${encodeURIComponent(key)}`, 'PUT', { app }),
};
