#!/usr/bin/env python3
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
from contextlib import redirect_stdout, redirect_stderr
from urllib.error import HTTPError
from urllib.parse import parse_qs

spec = importlib.util.spec_from_file_location('identity_setup', Path(__file__).with_name('keycloak-employee-setup.py'))
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)
ARGS = ['setup','--server','https://auth.invalid','--tenant','tenant-a','--public-url','https://auth.invalid',
        '--redirect-uri','https://site.invalid/account/?tenant=tenant-a']
ALLOWED = []
MANAGEMENT_ID = '11111111-1111-1111-1111-111111111111'
RUNTIME_ID = '22222222-2222-2222-2222-222222222222'


class Response:
    def __init__(self, value): self.value = value
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def read(self, size=None): return json.dumps(self.value).encode()


class FakeTransport:
    def __init__(self, web=None, needs_setup=False, extra_effective=None, group=False, wrong_secret=False):
        self.requests = []
        self.web = web if web is not None else [{'id':'web','clientId':'cleaning-suite-web','enabled':True,'publicClient':True,'standardFlowEnabled':True,'redirectUris':['https://site.invalid/*']}]
        self.needs_setup = needs_setup
        self.extra_effective = extra_effective
        self.group = group
        self.wrong_secret = wrong_secret
    def __call__(self, request, timeout):
        self.requests.append(request)
        if request.full_url.endswith('/realms/tenant-a/protocol/openid-connect/token'):
            assert request.get_header('Authorization') is None
            assert parse_qs(request.data.decode()) == {'grant_type':['client_credentials'],'client_id':['employee-runtime'],'client_secret':['synthetic-runtime-secret']}
            if self.wrong_secret: raise HTTPError(request.full_url,401,'unauthorized',{},None)
            return Response({'access_token':'synthetic-runtime-token'})
        if request.full_url.endswith('/admin/serverinfo'): return Response({'systemInfo':{'version':'26.8.1'}})
        path = request.full_url.partition('/admin/realms/tenant-a')[2]
        if path.startswith('/groups?search='): return Response([])
        if path == '/clients?clientId=admin-permissions': return Response([])
        if path == '/clients?clientId=cleaning-suite-web': return Response(self.web)
        if path.startswith('/users?first='): return Response([])
        if path == '/users/profile': return Response({'attributes': [] if self.needs_setup else [{
            'name':name,'multivalued':False,'permissions':{'view':['admin'],'edit':['admin']}}
            for name in ('cleaning_employee_id', 'cleaning_invitation_ticket')]})
        if path == '/clients?clientId=employee-runtime': return Response([] if self.needs_setup else [{
            'id':RUNTIME_ID,'clientId':'employee-runtime','enabled':True,'serviceAccountsEnabled':True,'publicClient':False}])
        if path == '/clients?clientId=realm-management': return Response([{'id':MANAGEMENT_ID,'clientId':'realm-management'}])
        if path == '/clients/' + RUNTIME_ID + '/service-account-user': return Response({'id':'runtime-user'})
        if path == '/clients/' + RUNTIME_ID + '/protocol-mappers/models': return Response([])
        if path == '/users/runtime-user/groups?first=0&max=1': return Response([{'id':'unexpected-group'}] if self.group else [])
        if path == '/users/runtime-user/role-mappings/realm/composite': return Response([{'name':'default-roles-tenant-a'}])
        if path == '/clients?first=0&max=1000': return Response([{'id':MANAGEMENT_ID,'clientId':'realm-management'},{'id':'account','clientId':'account'}])
        if path.endswith('/role-mappings/clients/' + MANAGEMENT_ID + '/composite'):
            return Response([{'name':name} for name in ALLOWED + ([self.extra_effective] if self.extra_effective else [])])
        if path.endswith('/role-mappings/clients/account/composite'): return Response([])
        if path == '/users/runtime-user/role-mappings/clients/' + MANAGEMENT_ID: return Response([{'name':name} for name in ALLOWED])
        if '/roles/' in path: return Response({'id':'role','name':path.rsplit('/',1)[1]})
        if not path: return Response({'realm':'tenant-a','enabled':True,'attributes':{} if self.needs_setup else {'frontendUrl':'https://auth.invalid'},
            'smtpServer':{} if self.needs_setup else {'host':'smtp.invalid','from':'test@example.invalid'}})
        raise AssertionError('Unexpected test route ' + path)


class SetupCheckTests(unittest.TestCase):
    def run_check(self, transport, apply=False):
        env = {'KEYCLOAK_MIGRATION_ADMIN_TOKEN':'synthetic-bearer-never-print',
               'KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET':'synthetic-runtime-secret',
               'KEYCLOAK_TENANT_SMTP_JSON':json.dumps({'host':'smtp.invalid','from':'test@example.invalid','password':'synthetic-smtp-secret'})}
        output = io.StringIO()
        with patch.dict(os.environ,env), patch.object(sys,'argv',ARGS + (['--apply'] if apply else [])), patch.object(setup,'urlopen',transport), redirect_stdout(output):
            result = setup.main()
        self.assertTrue(all(request.method == 'GET' or request.full_url.endswith('/protocol/openid-connect/token') for request in transport.requests))
        self.assertNotIn('synthetic-bearer',output.getvalue())
        self.assertNotIn('synthetic-smtp-secret',output.getvalue())
        self.assertNotIn('synthetic-runtime-secret',output.getvalue())
        self.assertNotIn('synthetic-runtime-token',output.getvalue())
        return result

    def test_default_check_is_read_only_and_does_not_echo_credentials(self):
        self.assertEqual(2,self.run_check(FakeTransport(needs_setup=True)))

    def test_complete_configuration_is_read_only_and_clean(self):
        self.assertEqual(2,self.run_check(FakeTransport()))

    def test_configured_realm_requires_authenticated_extension_readiness(self):
        class Ready(FakeTransport):
            def __call__(self, request, timeout):
                if request.full_url.endswith('/realms/tenant-a/cleaning-identity/v1'):
                    self.requests.append(request)
                    assert request.get_header('Authorization') == 'Bearer synthetic-runtime-token'
                    return Response({'realm': 'tenant-a', 'enabled': True})
                return super().__call__(request, timeout)
        with patch.object(setup, 'configure', return_value={}):
            self.assertEqual(0, self.run_check(Ready()))

    def test_missing_extension_cannot_report_setup_ready(self):
        class Missing(FakeTransport):
            def __call__(self, request, timeout):
                if request.full_url.endswith('/realms/tenant-a/cleaning-identity/v1'):
                    self.requests.append(request)
                    raise HTTPError(request.full_url, 404, 'not found', {}, None)
                return super().__call__(request, timeout)
        with patch.object(setup, 'configure', return_value={}):
            with self.assertRaisesRegex(RuntimeError, 'extension is missing or denied'):
                self.run_check(Missing())

    def test_ticket_profile_apply_preserves_unrelated_fields_without_editing_user_tickets(self):
        original = {'attributes': [
            {'name': 'unrelated', 'annotations': {'keep': 'yes'}},
            {'name': 'cleaning_employee_id', 'multivalued': False, 'permissions': {'view': ['admin'], 'edit': ['admin']}},
            {'name': 'cleaning_invitation_ticket', 'multivalued': True, 'annotations': {'keep': 'ticket-config'},
             'permissions': {'view': ['user'], 'edit': ['user']}}
        ], 'unmanagedAttributePolicy': 'preserve-this'}
        class Profile(FakeTransport):
            def __call__(self, request, timeout):
                if request.full_url.endswith('/users/profile'):
                    self.requests.append(request)
                    return Response(original if request.method == 'GET' else None)
                if request.full_url.endswith('/realms/tenant-a/cleaning-identity/v1'):
                    self.requests.append(request)
                    return Response({'realm': 'tenant-a', 'enabled': True})
                return super().__call__(request, timeout)
        transport = Profile()
        env = {'KEYCLOAK_MIGRATION_ADMIN_TOKEN': 'synthetic-bearer-never-print',
               'KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET': 'synthetic-runtime-secret'}
        with patch.dict(os.environ, env), patch.object(sys, 'argv', ARGS + ['--apply', '--development']), \
                patch.object(setup, 'urlopen', transport), patch.object(setup, 'configure', return_value={}), redirect_stdout(io.StringIO()):
            self.assertEqual(0, setup.main())
        writes = [r for r in transport.requests if r.method not in ('GET',) and not r.full_url.endswith('/token')]
        self.assertEqual(1, len(writes))
        self.assertTrue(writes[0].full_url.endswith('/users/profile'))
        result = json.loads(writes[0].data)
        self.assertEqual('preserve-this', result['unmanagedAttributePolicy'])
        self.assertEqual(original['attributes'][0], result['attributes'][0])
        ticket = next(a for a in result['attributes'] if a['name'] == 'cleaning_invitation_ticket')
        self.assertFalse(ticket['multivalued'])
        self.assertEqual({'view': ['admin'], 'edit': ['admin']}, ticket['permissions'])
        self.assertEqual({'keep': 'ticket-config'}, ticket['annotations'])

    def test_mismatched_existing_secret_fails_check_and_apply_without_mutations(self):
        for apply in (False, True):
            transport=FakeTransport(wrong_secret=True)
            with self.assertRaisesRegex(RuntimeError, 'delivered secret must match'):
                self.run_check(transport,apply=apply)
            self.assertTrue(all(r.method=='GET' or r.full_url.endswith('/protocol/openid-connect/token') for r in transport.requests))

    def test_missing_web_client_fails_before_clean_result(self):
        with self.assertRaisesRegex(RuntimeError, 'enabled cleaning-suite-web'):
            self.run_check(FakeTransport(web=[]))

    def test_disabled_web_client_fails_before_clean_result(self):
        with self.assertRaisesRegex(RuntimeError, 'enabled cleaning-suite-web'):
            self.run_check(FakeTransport(web=[{'clientId':'cleaning-suite-web','enabled':False,'redirectUris':['https://site.invalid/*']}]))

    def test_confidential_web_client_is_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'enabled cleaning-suite-web'):
            self.run_check(FakeTransport(web=[{'clientId':'cleaning-suite-web','enabled':True,'publicClient':False,'standardFlowEnabled':True,'redirectUris':['https://site.invalid/*']}]))

    def test_unregistered_redirect_fails_before_clean_result(self):
        with self.assertRaisesRegex(RuntimeError, 'landing URI is not registered'):
            self.run_check(FakeTransport(web=[{'clientId':'cleaning-suite-web','enabled':True,'publicClient':True,'standardFlowEnabled':True,'redirectUris':['https://other.invalid/*']}]))

    def test_exact_redirect_is_supported(self):
        self.assertEqual(2,self.run_check(FakeTransport(web=[{'clientId':'cleaning-suite-web','enabled':True,'publicClient':True,'standardFlowEnabled':True,'redirectUris':['https://site.invalid/account/?tenant=tenant-a']}])))

    def test_effective_composite_privilege_is_rejected_even_if_direct_roles_are_safe(self):
        with self.assertRaisesRegex(RuntimeError, 'inherited effective administration'):
            self.run_check(FakeTransport(extra_effective='manage-realm'))

    def test_group_derived_privileges_are_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'group-derived'):
            self.run_check(FakeTransport(group=True))

    def test_tenant_local_human_admin_blocks_setup_before_any_writes(self):
        class HumanAdmin(FakeTransport):
            def __call__(self, request, timeout):
                if '/users?first=' in request.full_url:
                    self.requests.append(request)
                    return Response([{'id': 'human-admin', 'email': 'private-person@example.invalid'}])
                if '/users/human-admin/role-mappings/clients/' in request.full_url:
                    self.requests.append(request)
                    return Response([{'name': 'manage-users'}])
                return super().__call__(request, timeout)
        transport = HumanAdmin(needs_setup=True)
        with self.assertRaisesRegex(RuntimeError, 'separate master operator'):
            self.run_check(transport)
        self.assertTrue(all(r.method == 'GET' for r in transport.requests))

    def test_legacy_broad_roles_require_manual_review_without_revocation(self):
        class Broad(FakeTransport):
            def __call__(self, request, timeout):
                if request.full_url.endswith('/role-mappings/clients/' + MANAGEMENT_ID):
                    self.requests.append(request)
                    return Response([{'name':'manage-users'}])
                return super().__call__(request, timeout)
        transport = Broad()
        with self.assertRaisesRegex(RuntimeError, 'unexpected administration roles'):
            self.run_check(transport)
        self.assertFalse(any(r.method == 'DELETE' for r in transport.requests))

    def test_upstream_error_body_is_not_exposed(self):
        def broken(request, timeout):
            raise HTTPError(request.full_url, 500, 'synthetic-secret-body', {}, io.BytesIO(b'synthetic-secret-body'))
        with self.assertRaisesRegex(RuntimeError, '^Keycloak setup request failed with HTTP 500$'):
            self.run_check(broken)

    def test_master_is_rejected_before_network(self):
        args = list(ARGS); args[args.index('tenant-a')] = 'master'
        with patch.object(sys,'argv',args), patch.object(setup,'urlopen') as http, redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit): setup.main()
            http.assert_not_called()


if __name__ == '__main__': unittest.main()
