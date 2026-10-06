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

export function getKeycloak(): Keycloak {
  if (!keycloak) {
    keycloak = new Keycloak({
      url: KEYCLOAK_URL,
      realm: KEYCLOAK_REALM,
      clientId: KEYCLOAK_CLIENT_ID,
    });
  }
  return keycloak;
}

function isMockAllowed(): boolean {
  return process.env.NEXT_PUBLIC_E2E_MOCK_AUTH === 'true';
}

export function initAuth(): Promise<boolean> {
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
    sessionStorage.setItem('__MOCK_AUTHED__', 'true');
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    (window as any).__MOCK_AUTHED__ = true;
    window.location.reload();
    return;
  }
  await getKeycloak().login({ redirectUri: window.location.href });
}

export async function logout(): Promise<void> {
  if (isMockAllowed() && typeof window !== 'undefined') {
    sessionStorage.removeItem('__MOCK_AUTHED__');
    sessionStorage.setItem('__MOCK_LOGGED_OUT__', 'true');
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    (window as any).__MOCK_AUTHED__ = false;
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
    return Boolean((window as any).__MOCK_AUTHED__);
  }
  return getKeycloak().hasRealmRole('admin');
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
