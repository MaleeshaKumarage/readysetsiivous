// Pure helpers for the admin request layer, kept free of browser globals so they can be unit tested.

export const ADMIN_REQUEST_TIMEOUT_MS = 20_000;

// Identifies whose data a cached response belongs to: the user and their realm roles. A different user, or the same user with different roles, gets a different scope.
export function cacheScopeFor(accessToken: string | undefined): string {
  if (!accessToken) return 'anonymous';
  const payload = accessToken.split('.')[1];
  if (!payload) return 'anonymous';
  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '=');
    const claims = JSON.parse(atob(base64)) as { sub?: unknown; realm_access?: { roles?: unknown } };
    if (typeof claims.sub !== 'string' || !claims.sub) return 'anonymous';
    const roles = Array.isArray(claims.realm_access?.roles) ? claims.realm_access.roles.filter((r): r is string => typeof r === 'string').sort() : [];
    return `user:${claims.sub}|roles:${roles.join(',')}`;
  } catch {
    return 'anonymous';
  }
}

// A request fails after the timeout, or immediately when the caller aborts it, whichever happens first.
// AbortSignal.timeout and AbortSignal.any are missing in older browsers, so fall back to a controller and a timer.
export function requestSignal(external: AbortSignal | null | undefined, timeoutMs: number = ADMIN_REQUEST_TIMEOUT_MS): AbortSignal {
  const hasTimeout = typeof AbortSignal !== 'undefined' && typeof AbortSignal.timeout === 'function';
  const hasAny = typeof AbortSignal !== 'undefined' && typeof AbortSignal.any === 'function';
  if (hasTimeout && hasAny) {
    const timeout = AbortSignal.timeout(timeoutMs);
    return external ? AbortSignal.any([external, timeout]) : timeout;
  }
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(new DOMException('Request timed out', 'TimeoutError')), timeoutMs);
  const cancel = () => { clearTimeout(timer); controller.abort(external?.reason); };
  if (external) {
    if (external.aborted) cancel();
    else external.addEventListener('abort', cancel, { once: true });
  }
  controller.signal.addEventListener('abort', () => clearTimeout(timer), { once: true });
  return controller.signal;
}
