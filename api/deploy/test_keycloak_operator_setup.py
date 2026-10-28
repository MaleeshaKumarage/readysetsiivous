#!/usr/bin/env python3
"""No live identities or mail: master-operator setup boundary regressions."""
import copy
import importlib.util
import io
import json
import os
from contextlib import redirect_stdout, redirect_stderr
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit

spec = importlib.util.spec_from_file_location('operator_setup', Path(__file__).with_name('keycloak-operator-setup.py'))
setup = importlib.util.module_from_spec(spec); spec.loader.exec_module(setup)
EMAIL = 'operator@example.invalid'
USER = '33333333-3333-3333-3333-333333333333'
ROLE = {'id': '44444444-4444-4444-4444-444444444444', 'name': 'admin', 'clientRole': False, 'composite': True}
ARGS = ['setup', '--server', 'https://private-auth.invalid', '--public-url', 'https://public-auth.invalid']
ENV = {'KEYCLOAK_OPERATOR_EMAIL': EMAIL, 'KEYCLOAK_MIGRATION_ADMIN_TOKEN': 'synthetic-bootstrap-bearer-never-print'}


class Response:
    def __init__(self, value=None, status=200, location=None):
        self.value, self.status = value, status
        self.headers = {'Location': location} if location else {}
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def read(self, size=None): return b'' if self.value is None else json.dumps(self.value).encode()


class Transport:
    def __init__(self, existing=None, smtp=None):
        self.requests = []
        self.created = None
        self.deleted = []
        self.existing = copy.deepcopy(existing)
        self.groups = []
        self.roles = [copy.deepcopy(ROLE)]
        self.credentials = [{'type': 'otp'}, {'type': 'password'}]
        self.ambiguous = False
        self.username_collision = False
        self.send_failure = False
        self.wrong_issuer = False
        self.private_issuer = 'https://public-auth.invalid/realms/master'
        self.drop_mfa = False
        self.smtp = smtp if smtp is not None else {'host': 'smtp.invalid', 'from': 'sender@example.invalid',
                                                  'auth': 'true', 'user': 'synthetic-smtp-user', 'password': '**********',
                                                  'port': '465', 'ssl': 'true'}
        self.realm = {'realm': 'master', 'enabled': True, 'smtpServer': self.smtp}

    def __call__(self, request, timeout):
        self.requests.append(request)
        parsed = urlsplit(request.full_url)
        path, query = parsed.path, parse_qs(parsed.query)
        method = request.method
        if path.endswith('/.well-known/openid-configuration'):
            assert request.get_header('Authorization') is None
            issuer = 'https://foreign.invalid/realms/master' if self.wrong_issuer else parsed.scheme + '://' + parsed.netloc + '/realms/master'
            if parsed.hostname == 'private-auth.invalid' and not self.wrong_issuer:
                issuer = self.private_issuer
            return Response({'issuer': issuer})
        assert request.get_header('Authorization') == 'Bearer ' + ENV['KEYCLOAK_MIGRATION_ADMIN_TOKEN']
        assert path.startswith('/admin/realms/master'), 'Operator tooling must never target a tenant realm'
        relative = path[len('/admin/realms/master'):]
        if method == 'GET':
            if not relative: return Response(self.realm)
            if relative == '/roles/admin': return Response(ROLE)
            if relative == '/users':
                result = [copy.deepcopy(self.existing)] if self.existing else []
                if 'email' in query and self.ambiguous:
                    result.append(dict(result[0], id='55555555-5555-5555-5555-555555555555'))
                if 'username' in query and self.username_collision:
                    result = [{'id': '55555555-5555-5555-5555-555555555555', 'username': EMAIL, 'email': 'another@example.invalid'}]
                return Response(result)
            if relative == '/clients':
                assert query == {'clientId': ['security-admin-console']}
                return Response([{'id': 'console', 'clientId': 'security-admin-console', 'enabled': True}])
            if relative == '/authentication/required-actions':
                return Response([{'alias': name, 'enabled': True} for name in setup.ACTIONS])
            if relative == '/users/' + USER: return Response(self.created or self.existing)
            if relative.endswith('/groups'): return Response(self.groups)
            if relative.endswith('/role-mappings/realm'): return Response(self.roles)
            if relative.endswith('/credentials'): return Response(self.credentials)
        body = json.loads(request.data) if request.data else None
        if method == 'PUT' and not relative:
            self.realm.update(body); return Response(status=204)
        if method == 'POST' and relative == '/users':
            self.created = dict(copy.deepcopy(body), id=USER)
            if self.drop_mfa: self.created['requiredActions'] = ['UPDATE_PASSWORD']
            return Response(status=201, location=parsed.scheme + '://' + parsed.netloc + '/admin/realms/master/users/' + USER)
        if method == 'POST' and relative.endswith('/role-mappings/realm'):
            assert body == [ROLE]
            return Response(status=204)
        if method == 'PUT' and relative.endswith('/execute-actions-email'):
            assert query == {'client_id': ['security-admin-console'], 'redirect_uri': ['https://public-auth.invalid/admin/master/console/'], 'lifespan': ['1800']}
            assert body == setup.ACTIONS
            if self.send_failure:
                raise HTTPError(request.full_url, 500, 'synthetic-sensitive-upstream-body', {}, io.BytesIO(b'synthetic-sensitive-upstream-body'))
            return Response(status=204)
        if method == 'DELETE' and relative == '/users/' + USER:
            self.deleted.append(USER); return Response(status=204)
        raise AssertionError('Unexpected synthetic route ' + method + ' ' + relative)


def ready():
    return {'id': USER, 'username': EMAIL, 'email': EMAIL, 'enabled': True,
            'emailVerified': True, 'requiredActions': [], 'attributes': {}}


class OperatorTests(unittest.TestCase):
    def run_setup(self, transport, apply=False, env=None, args=None):
        output = io.StringIO()
        actual_env = dict(ENV)
        if env: actual_env.update(env)
        arguments = list(args or ARGS) + (['--apply', '--send-setup-email'] if apply else [])
        with patch.dict(os.environ, actual_env, clear=True), patch.object(sys, 'argv', arguments), \
                patch.object(setup, 'urlopen', transport), redirect_stdout(output):
            result = setup.main()
        for secret in (EMAIL, ENV['KEYCLOAK_MIGRATION_ADMIN_TOKEN'], 'synthetic-smtp-secret', 'synthetic-sensitive-upstream-body'):
            self.assertNotIn(secret, output.getvalue())
        return result, json.loads(output.getvalue())

    def test_check_is_read_only_and_reports_new_operator_without_pii(self):
        transport = Transport()
        result, status = self.run_setup(transport)
        self.assertEqual(2, result)
        self.assertEqual('needs_setup', status['status'])
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_public_http_and_redirected_issuer_are_rejected_before_writes(self):
        transport = Transport()
        args = list(ARGS); args[-1] = 'http://public-auth.invalid'
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            self.run_setup(transport, apply=True, args=args)
        self.assertEqual([], transport.requests)
        transport = Transport(); transport.wrong_issuer = True
        with self.assertRaisesRegex(RuntimeError, 'master issuer'):
            self.run_setup(transport, apply=True)
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_mutations_require_both_explicit_flags(self):
        for flag in ('--apply', '--send-setup-email'):
            transport = Transport()
            with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                self.run_setup(transport, args=ARGS + [flag])
            self.assertEqual([], transport.requests)

    def test_private_transport_must_use_the_same_public_https_frontend_issuer(self):
        for issuer in ('http://private-auth.invalid/realms/master', 'https://wrong-host.invalid/realms/master'):
            with self.subTest(issuer=issuer):
                transport = Transport(); transport.private_issuer = issuer
                with self.assertRaisesRegex(RuntimeError, 'public and private transport'):
                    self.run_setup(transport, apply=True)
                self.assertEqual(2, len(transport.requests))
                self.assertTrue(all(r.method == 'GET' and r.full_url.endswith('/.well-known/openid-configuration') for r in transport.requests))
                self.assertTrue(all(r.get_header('Authorization') is None for r in transport.requests))

    def test_private_http_is_safe_only_when_it_reports_the_fixed_public_https_issuer(self):
        transport = Transport()
        args = list(ARGS); args[args.index('https://private-auth.invalid')] = 'http://private-auth.invalid'
        result, _ = self.run_setup(transport, args=args)
        self.assertEqual(2, result)
        discovery = [r for r in transport.requests if r.full_url.endswith('/.well-known/openid-configuration')]
        self.assertEqual(2, len(discovery))
        self.assertTrue(all(r.get_header('Authorization') is None for r in discovery))
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_only_explicit_literal_loopback_development_allows_http(self):
        transport = Transport(smtp={'host': 'mailpit', 'from': 'sender@example.invalid', 'auth': False})
        result, _ = self.run_setup(transport, args=['setup', '--server', 'http://127.0.0.1:18580',
                                                   '--public-url', 'http://127.0.0.1:18580', '--development'])
        self.assertEqual(2, result)
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))
        transport = Transport()
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            self.run_setup(transport, args=['setup', '--server', 'http://127.0.0.1:18580',
                                            '--public-url', 'http://public-auth.invalid', '--development'])
        self.assertEqual([], transport.requests)

    def test_missing_or_incomplete_smtp_blocks_before_operator_creation(self):
        for smtp in ({}, {'host': 'smtp.invalid', 'from': 'sender@example.invalid', 'auth': 'true', 'ssl': 'true'},
                     {'host': 'smtp.invalid', 'from': 'sender@example.invalid', 'auth': 'invalid', 'ssl': 'true'}):
            with self.subTest(smtp=smtp):
                transport = Transport(smtp=smtp)
                with self.assertRaisesRegex(RuntimeError, 'SMTP configuration'):
                    self.run_setup(transport, apply=True)
                self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_ambiguous_or_unverified_existing_accounts_are_never_adopted(self):
        for ambiguous, verified in ((True, True), (False, False)):
            transport = Transport(existing=ready()); transport.ambiguous = ambiguous
            transport.existing['emailVerified'] = verified
            with self.assertRaises(RuntimeError): self.run_setup(transport, apply=True)
            self.assertTrue(all(r.method == 'GET' for r in transport.requests))
            self.assertEqual([], transport.deleted)

    def test_service_employee_grouped_and_username_collision_accounts_are_rejected(self):
        for change in ('service', 'employee', 'group', 'collision'):
            transport = Transport(existing=ready())
            if change == 'service': transport.existing['serviceAccountClientId'] = 'some-service'
            if change == 'employee': transport.existing['attributes'] = {'cleaning_employee_id': [USER]}
            if change == 'group': transport.groups = [{'id': 'group'}]
            if change == 'collision': transport.username_collision = True
            with self.subTest(change=change), self.assertRaises(RuntimeError): self.run_setup(transport, apply=True)
            self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_existing_ready_admin_is_noop_even_with_explicit_send_flags(self):
        transport = Transport(existing=ready(), smtp={})
        result, status = self.run_setup(transport, apply=True)
        self.assertEqual(0, result)
        self.assertEqual({'status': 'already_ready', 'changed': False, 'setup_email_sent': False}, status)
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_existing_admin_without_mfa_requires_reconciliation_without_rotation(self):
        transport = Transport(existing=ready()); transport.credentials = [{'type': 'password'}]
        with self.assertRaisesRegex(RuntimeError, 'MFA reconciliation'):
            self.run_setup(transport, apply=True)
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_new_operator_requires_password_email_and_mfa_without_credentials(self):
        transport = Transport()
        result, status = self.run_setup(transport, apply=True)
        self.assertEqual(0, result)
        self.assertEqual('smtp_accepted', status['status'])
        self.assertFalse(status['inbox_verified']); self.assertFalse(status['mfa_completed'])
        self.assertEqual(set(setup.ACTIONS), set(transport.created['requiredActions']))
        self.assertFalse(transport.created['emailVerified'])
        self.assertNotIn('credentials', transport.created)
        self.assertEqual([], transport.deleted)

    def test_send_failure_rolls_back_only_the_newly_created_user(self):
        transport = Transport(); transport.send_failure = True
        with self.assertRaisesRegex(RuntimeError, 'newly created account removed'):
            self.run_setup(transport, apply=True)
        self.assertEqual([USER], transport.deleted)
        self.assertEqual(1, sum(r.method == 'DELETE' for r in transport.requests))

    def test_ignored_new_mfa_requirements_roll_back_before_admin_grant(self):
        transport = Transport(); transport.drop_mfa = True
        with self.assertRaisesRegex(RuntimeError, 'newly created account removed'):
            self.run_setup(transport, apply=True)
        self.assertEqual([USER], transport.deleted)
        self.assertFalse(any(r.method == 'POST' and r.full_url.endswith('/role-mappings/realm') for r in transport.requests))

    def test_masked_existing_smtp_is_never_written_back(self):
        transport = Transport()
        self.run_setup(transport, apply=True)
        self.assertFalse(any(r.method == 'PUT' and r.full_url.endswith('/admin/realms/master') for r in transport.requests))
        self.assertTrue(all(b'**********' not in (r.data or b'') for r in transport.requests))

    def test_only_complete_protected_smtp_is_written_and_retained_after_send_failure(self):
        protected = {'host': 'smtp.invalid', 'from': 'sender@example.invalid', 'port': '465',
                     'auth': True, 'ssl': True, 'user': 'synthetic-smtp-user', 'password': 'synthetic-smtp-secret'}
        transport = Transport(smtp={}); transport.send_failure = True
        with self.assertRaisesRegex(RuntimeError, 'newly created account removed'):
            self.run_setup(transport, apply=True, env={'KEYCLOAK_OPERATOR_SMTP_JSON': json.dumps(protected)})
        writes = [r for r in transport.requests if r.method == 'PUT' and r.full_url.endswith('/admin/realms/master')]
        self.assertEqual(1, len(writes))
        self.assertEqual('synthetic-smtp-secret', json.loads(writes[0].data)['smtpServer']['password'])
        self.assertEqual([USER], transport.deleted)

    def test_protected_smtp_cannot_use_a_masked_password_placeholder(self):
        transport = Transport(smtp={})
        protected = {'host': 'smtp.invalid', 'from': 'sender@example.invalid', 'auth': True,
                     'ssl': True, 'user': 'synthetic-smtp-user', 'password': '**********'}
        with self.assertRaisesRegex(RuntimeError, 'incomplete or insecure'):
            self.run_setup(transport, apply=True, env={'KEYCLOAK_OPERATOR_SMTP_JSON': json.dumps(protected)})
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))


if __name__ == '__main__': unittest.main()
