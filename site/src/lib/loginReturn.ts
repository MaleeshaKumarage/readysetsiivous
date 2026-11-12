/** Where a person lands after signing in: the portal screen, which lists every portal their account can open. */
export const PORTAL_HOME = '/account/';

/** A same-site path only. Anything else falls back to the portal screen, so a return value can never leave the site. */
export function loginReturnPath(requested?: string | null): string {
  if (requested && requested.startsWith('/') && !requested.startsWith('//') && !requested.includes('\\') && !/[\u0000-\u001f\u007f]/.test(requested)) return requested;
  return PORTAL_HOME;
}
