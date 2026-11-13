import { loginReturnPath } from './loginReturn';
// Keycloak client-side auth (PKCE, public client). Token kept in memory only.

import Keycloak from 'keycloak-js';

export const KEYCLOAK_URL =
  process.env.NEXT_PUBLIC_KEYCLOAK_URL ?? "https://auth.readysetsiivous.fi";
export const KEYCLOAK_REALM =
  process.env.NEXT_PUBLIC_KEYCLOAK_REALM ?? "readysetsiivous";
export const KEYCLOAK_CLIENT_ID =
  process.env.NEXT_PUBLIC_KEYCLOAK_CLIENT_ID ?? "cleaning-suite-web";

let keycloak: Keycloak | null = null;
let initPromise: Promise<boolean> | null = null;

export function selectAuthTenant(realm: string): void {
  if (!/^[a-z0-9][a-z0-9-]{2,62}$/.test(realm)) throw new Error('Invalid tenant in account link.');
  if (typeof window === 'undefined') return;
  sessionStorage.setItem('__AUTH_TENANT__', realm);
  if (keycloak && keycloak.realm !== realm) { keycloak = null; initPromise = null; }
}
function selectedRealm(): string {
  if (typeof window !== 'undefined') {
    const stored = sessionStorage.getItem('__AUTH_TENANT__');
    if (stored && /^[a-z0-9][a-z0-9-]{2,62}$/.test(stored)) return stored;
  }
  return KEYCLOAK_REALM;
}
export function authTenant(): string {
  return keycloak?.realm ?? selectedRealm();
}
export function getKeycloak(): Keycloak {
  if (!keycloak) {
    keycloak = new Keycloak({
      url: KEYCLOAK_URL,
      realm: selectedRealm(),
      clientId: KEYCLOAK_CLIENT_ID,
    });
  }
  return keycloak;
}

function isMockAllowed(): boolean {
  return process.env.NODE_ENV !== 'production';
}

export function initAuth(): Promise<boolean> {
  if (typeof window !== 'undefined' && ['/me/shifts/', '/me/shifts', '/admin/partner-requests/', '/admin/partner-requests'].includes(window.location.pathname)) {
    const tenant = new URLSearchParams(window.location.search).get('tenant');
    if (tenant && /^[a-z0-9][a-z0-9-]{2,62}$/.test(tenant)) selectAuthTenant(tenant);
  }
  // Test-only switch: makes the session check fail, so callers' failure paths can be exercised.
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTH_ERROR__) {
    return Promise.reject(new Error('Session check failed'));
  }
  if (isMockAllowed() && typeof window !== 'undefined' && sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
    return Promise.resolve(false);
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    return Promise.resolve(Boolean((window as any).__MOCK_AUTHED__));
  }
  if (!initPromise) {
    initPromise = getKeycloak()
      .init({
        onLoad: 'check-sso',
        silentCheckSsoRedirectUri:
          typeof window !== 'undefined' ? `${window.location.origin}/silent-check-sso.html` : undefined,
        pkceMethod: 'S256',
        checkLoginIframe: false,
      })
      .then((authenticated) => authenticated)
      .catch(() => false);
  }
  return initPromise;
}

export async function login(): Promise<void> {
  if (isMockAllowed() && typeof window !== 'undefined') {
    sessionStorage.removeItem('__MOCK_LOGGED_OUT__');
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    (window as any).__MOCK_AUTHED__ = true;
    window.location.reload();
    return;
  }
  await getKeycloak().login({ redirectUri: new URL(loginReturnPath(), window.location.origin).toString() });
}


// Push-device cleanup must not hold sign-out indefinitely: a stalled service worker or request would otherwise leave the user signed in.
const PUSH_CLEANUP_LIMIT_MS = 5000;
class PushCleanupTimedOut extends Error {}

function withLimit<T>(work: Promise<T>, ms: number): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const limit = new Promise<never>((_, reject) => { timer = setTimeout(() => reject(new PushCleanupTimedOut('Push cleanup timed out')), ms); });
  return Promise.race([work, limit]).finally(() => clearTimeout(timer));
}

export async function logout(): Promise<void> {
  console.warn('[diag] logout start');
  try {
    await withLimit((async () => { const { removePushDevice } = await import('./pushNotifications'); await removePushDevice(); })(), PUSH_CLEANUP_LIMIT_MS);
  } catch (error) {
    // A stalled cleanup is not a refusal, so sign-out continues. The device stays registered until it is removed another way.
    if (error instanceof PushCleanupTimedOut) { /* continue signing out */ }
    else if (typeof window === 'undefined' || !window.confirm('Notification cleanup failed. This device may still receive generic alerts. Sign out anyway?')) throw error;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    (window as any).__MOCK_AUTHED__ = false;
    sessionStorage.setItem('__MOCK_LOGGED_OUT__', 'true');
    window.location.reload();
    return;
  }
  await getKeycloak().logout();
}

export function isAuthenticated(): boolean {
  if (isMockAllowed() && typeof window !== 'undefined' && sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
    return false;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    return Boolean((window as any).__MOCK_AUTHED__);
  }
  const kc = getKeycloak();
  return Boolean(kc.authenticated && kc.token);
}

export function token(): string | undefined {
  if (isMockAllowed() && typeof window !== 'undefined' && sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
    return undefined;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__) {
    return 'mock-jwt-token';
  }
  return getKeycloak().token ?? undefined;
}

export function isAdmin(): boolean {
  if (isMockAllowed() && typeof window !== 'undefined' && sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
    return false;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    return Boolean((window as any).__MOCK_AUTHED__) && (!(window as any).__MOCK_ROLES__ || (window as any).__MOCK_ROLES__.includes('admin'));
  }
  return getKeycloak().hasRealmRole('admin');
}

export function isManager(): boolean {
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined)
    return isAuthenticated() && Boolean((window as any).__MOCK_ROLES__?.includes('manager'));
  return getKeycloak().hasRealmRole('manager');
}

export function isPartner(): boolean {
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined)
    return isAuthenticated() && Boolean((window as any).__MOCK_ROLES__?.includes('partner'));
  return getKeycloak().hasRealmRole('partner');
}

export async function refreshToken(): Promise<string | undefined> {
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__) {
    return 'mock-jwt-token';
  }
  const kc = getKeycloak();
  try {
    await kc.updateToken(30);
    return kc.token ?? undefined;
  } catch {
    await login();
    return undefined;
  }
}
