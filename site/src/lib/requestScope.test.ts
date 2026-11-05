import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cacheScopeFor, requestSignal } from './requestScope.ts';

function jwtWith(claims: object): string {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
  return `${encode({ alg: 'none' })}.${encode(claims)}.signature`;
}

test('cache scope is the token subject, so another user never reuses cached data', () => {
  assert.notEqual(cacheScopeFor(jwtWith({ sub: 'user-a' })), cacheScopeFor(jwtWith({ sub: 'user-b' })));
});

test('cache scope changes when the same user loses a role', () => {
  const admin = cacheScopeFor(jwtWith({ sub: 'user-a', realm_access: { roles: ['admin'] } }));
  const manager = cacheScopeFor(jwtWith({ sub: 'user-a', realm_access: { roles: ['manager'] } }));
  assert.notEqual(admin, manager);
  assert.equal(admin, cacheScopeFor(jwtWith({ sub: 'user-a', realm_access: { roles: ['admin'] } })));
});

test('missing, malformed or subject-less tokens share the anonymous scope', () => {
  assert.equal(cacheScopeFor(undefined), 'anonymous');
  assert.equal(cacheScopeFor('not-a-jwt'), 'anonymous');
  assert.equal(cacheScopeFor(jwtWith({ realm: 'tenant-a' })), 'anonymous');
});

test('request signal aborts after the timeout', async () => {
  const signal = requestSignal(undefined, 20);
  assert.equal(signal.aborted, false);
  // The timeout timer is unreferenced, so hold the event loop open until the abort fires (with a generous backstop).
  await new Promise<void>((resolve, reject) => {
    const backstop = setTimeout(() => reject(new Error('timeout signal never fired')), 5_000);
    signal.addEventListener('abort', () => { clearTimeout(backstop); resolve(); }, { once: true });
  });
  assert.equal(signal.aborted, true);
});

test('request signal aborts immediately when the caller aborts', () => {
  const caller = new AbortController();
  const signal = requestSignal(caller.signal, 60_000);
  assert.equal(signal.aborted, false);
  caller.abort();
  assert.equal(signal.aborted, true);
});
