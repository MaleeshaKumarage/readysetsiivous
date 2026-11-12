import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loginReturnPath, PORTAL_HOME } from './loginReturn.ts';

test('sign-in lands on the portal screen when nothing else is asked for', () => {
  assert.equal(loginReturnPath(), PORTAL_HOME);
  assert.equal(loginReturnPath(null), '/account/');
  assert.equal(loginReturnPath(''), '/account/');
});

test('a same-site path is honoured', () => {
  assert.equal(loginReturnPath('/admin/shifts/'), '/admin/shifts/');
  assert.equal(loginReturnPath('/partner/'), '/partner/');
});

test('anything that could leave the site falls back to the portal screen', () => {
  assert.equal(loginReturnPath('https://evil.example/'), '/account/');
  assert.equal(loginReturnPath('//evil.example/'), '/account/');
  assert.equal(loginReturnPath('/\\evil.example'), '/account/');
  assert.equal(loginReturnPath('javascript:alert(1)'), '/account/');
  // A tab or newline is stripped by the browser URL parser, which would make '/\t/' protocol-relative.
  assert.equal(loginReturnPath('/\t/evil.example'), '/account/');
  assert.equal(loginReturnPath('/\n/evil.example'), '/account/');
});
