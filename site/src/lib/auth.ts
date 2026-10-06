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
  return (
    process.env.NODE_ENV !== 'production' ||
    (typeof window !== 'undefined' &&
      ((window as any).__MOCK_AUTHED__ !== undefined ||
        localStorage.getItem('rss_mock_authed') === 'true' ||
        sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true'))
  );
}

export function initAuth(): Promise<boolean> {
  if (isMockAllowed() && typeof window !== 'undefined' && sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
    return Promise.resolve(false);
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    return Promise.resolve(Boolean((window as any).__MOCK_AUTHED__));
  }
  if (isMockAllowed() && typeof window !== 'undefined' && localStorage.getItem('rss_mock_authed') === 'true') {
    return Promise.resolve(true);
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
    localStorage.setItem('rss_mock_authed', 'true');
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
    localStorage.removeItem('rss_mock_authed');
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
  if (isMockAllowed() && typeof window !== 'undefined' && localStorage.getItem('rss_mock_authed') === 'true') {
    return true;
  }
  const kc = getKeycloak();
  return Boolean(kc.authenticated && kc.token);
}

export function token(): string | undefined {
  if (isMockAllowed() && typeof window !== 'undefined' && sessionStorage.getItem('__MOCK_LOGGED_OUT__') === 'true') {
    return undefined;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    return (window as any).__MOCK_AUTHED__ ? 'mock-jwt-token' : undefined;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && localStorage.getItem('rss_mock_authed') === 'true') {
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
  if (isMockAllowed() && typeof window !== 'undefined' && localStorage.getItem('rss_mock_authed') === 'true') {
    return true;
  }
  return getKeycloak().hasRealmRole('admin');
}

export async function refreshToken(): Promise<string | undefined> {
  if (isMockAllowed() && typeof window !== 'undefined' && (window as any).__MOCK_AUTHED__ !== undefined) {
    return (window as any).__MOCK_AUTHED__ ? 'mock-jwt-token' : undefined;
  }
  if (isMockAllowed() && typeof window !== 'undefined' && localStorage.getItem('rss_mock_authed') === 'true') {
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
