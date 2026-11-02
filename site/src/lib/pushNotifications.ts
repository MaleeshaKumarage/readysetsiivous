import { API_URL } from './api';
import { authTenant, getKeycloak, refreshToken } from './auth';

export interface PushConfig { available: boolean; publicKey: string; devices: { id: string; enabled: boolean }[] }
interface Binding { identity: string; id: string }
const storageKey = 'readyset-push-device-v1';
export function pushIdentity(): string {
  const subject = process.env.NODE_ENV !== 'production' && (window as any).__MOCK_AUTHED__
    ? ((window as any).__MOCK_SUBJECT__ || 'mock-subject') : getKeycloak().tokenParsed?.sub;
  if (!subject) throw new Error('Please sign in again before enabling notifications.');
  return `${authTenant()}:${subject}`;
}
export function readPushBinding(): Binding | null {
  const value = localStorage.getItem(storageKey);
  if (!value) return null;
  try { const data = JSON.parse(value); return typeof data.identity === 'string' && typeof data.id === 'string' ? data : null; } catch { return null; }
}
export function savePushBinding(id: string): void { localStorage.setItem(storageKey, JSON.stringify({ identity: pushIdentity(), id })); }
export function clearPushBinding(): void { localStorage.removeItem(storageKey); }
export async function pushRequest<T>(path = '', method = 'GET', body?: unknown): Promise<T> {
  const jwt = await refreshToken();
  if (!jwt) throw new Error('Please sign in again.');
  const response = await fetch(`${API_URL}/api/v1/me/notifications${path}`, { method, cache: 'no-store', headers: { Authorization: `Bearer ${jwt}`, ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
  if (!response.ok) {
    const details = await response.json().catch(() => null) as { code?: string } | null;
    const code = typeof details?.code === 'string' ? details.code : undefined;
    const error = new Error(code === 'device_limit' ? 'The notification device limit has been reached. Disable notifications on another device before adding this one.' : response.status === 403 ? 'Notifications require an active employee profile linked to your login. Ask your administrator to link your profile.' : response.status === 429 ? 'Too many requests. Wait a minute and try again.' : response.status === 503 ? 'Notifications are unavailable. Please try again later.' : 'Could not update notifications. Please try again.') as Error & { status: number; code?: string };
    error.code = code;
    error.status = response.status; throw error;
  }
  return response.status === 204 ? undefined as T : response.json();
}
export async function removePushDevice(): Promise<void> {
  const binding = readPushBinding();
  let serverError: unknown;
  if (binding && binding.identity === pushIdentity()) {
    try { await pushRequest(`/subscription/${encodeURIComponent(binding.id)}`, 'DELETE'); }
    catch (error) { if ((error as { status?: number }).status !== 404) serverError = error; }
  }
  if ('serviceWorker' in navigator) {
    const registration = await navigator.serviceWorker.getRegistration('/');
    const subscription = await registration?.pushManager?.getSubscription();
    if (subscription) {
      if (!await subscription.unsubscribe()) throw new Error('Could not disable browser notifications. Please retry before signing out.');
      serverError = undefined; // Unsubscribing invalidates this browser endpoint even when offline.
    }
  }
  if (serverError) throw serverError;
  clearPushBinding();
}
export function vapidBytes(key: string): Uint8Array {
  const raw = atob(key.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(key.length / 4) * 4, '='));
  return Uint8Array.from(raw, char => char.charCodeAt(0));
}
