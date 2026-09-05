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

export function initAuth(): Promise<boolean> {
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
  await getKeycloak().login({ redirectUri: window.location.href });
}

export async function logout(): Promise<void> {
  await getKeycloak().logout();
}

export function isAuthenticated(): boolean {
  const kc = getKeycloak();
  return Boolean(kc.authenticated && kc.token);
}

export function token(): string | undefined {
  return getKeycloak().token ?? undefined;
}

export function isAdmin(): boolean {
  return getKeycloak().hasRealmRole('admin');
}

export async function refreshToken(): Promise<string | undefined> {
  const kc = getKeycloak();
  try {
    await kc.updateToken(30);
    return kc.token ?? undefined;
  } catch {
    await login();
    return undefined;
  }
}
