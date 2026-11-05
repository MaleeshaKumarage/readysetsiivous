// Pure helpers for the admin request layer, kept free of browser globals so they can be unit tested.

export const ADMIN_REQUEST_TIMEOUT_MS = 20_000;

// Identifies whose data a cached response belongs to. A different signed-in user (or no user) gets a different scope.
export function cacheScopeFor(accessToken: string | undefined): string {
  if (!accessToken) return 'anonymous';
  const payload = accessToken.split('.')[1];
  if (!payload) return 'anonymous';
  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '=');
    const claims = JSON.parse(atob(base64)) as { sub?: unknown };
    return typeof claims.sub === 'string' && claims.sub ? `user:${claims.sub}` : 'anonymous';
  } catch {
    return 'anonymous';
  }
}

// A request fails after the timeout, or immediately when the caller aborts it, whichever happens first.
export function requestSignal(external: AbortSignal | null | undefined, timeoutMs: number = ADMIN_REQUEST_TIMEOUT_MS): AbortSignal {
  const timeout = AbortSignal.timeout(timeoutMs);
  return external ? AbortSignal.any([external, timeout]) : timeout;
}
