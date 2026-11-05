import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cacheScopeFor, requestSignal } from './requestScope.ts';

function jwtWith(claims: object): string {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
  return `${encode({ alg: 'none' })}.${encode(claims)}.signature`;
}

test('cache scope is the token subject, so another user never reuses cached data', () => {
  assert.equal(cacheScopeFor(jwtWith({ sub: 'user-a' })), 'user:user-a');
  assert.notEqual(cacheScopeFor(jwtWith({ sub: 'user-a' })), cacheScopeFor(jwtWith({ sub: 'user-b' })));
});

test('missing, malformed or subject-less tokens share the anonymous scope', () => {
  assert.equal(cacheScopeFor(undefined), 'anonymous');
  assert.equal(cacheScopeFor('not-a-jwt'), 'anonymous');
  assert.equal(cacheScopeFor(jwtWith({ realm: 'tenant-a' })), 'anonymous');
});

test('request signal aborts after the timeout', async () => {
  const signal = requestSignal(undefined, 20);
  assert.equal(signal.aborted, false);
  // The timeout timer is unreferenced, so keep the event loop alive while waiting for it.
  await new Promise<void>(resolve => setTimeout(resolve, 100));
  assert.equal(signal.aborted, true);
});

test('request signal aborts immediately when the caller aborts', () => {
  const caller = new AbortController();
  const signal = requestSignal(caller.signal, 60_000);
  assert.equal(signal.aborted, false);
  caller.abort();
  assert.equal(signal.aborted, true);
});
