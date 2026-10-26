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

spec = importlib.util.spec_from_file_location('identity_setup', Path(__file__).with_name('keycloak-employee-setup.py'))
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)
ARGS = ['setup','--server','https://auth.invalid','--tenant','tenant-a','--public-url','https://auth.invalid',
        '--redirect-uri','https://site.invalid/account/?tenant=tenant-a']
ALLOWED = ['manage-users','query-users','view-users','view-realm']


class Response:
    def __init__(self, value): self.value = value
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def read(self): return json.dumps(self.value).encode()


class FakeTransport:
    def __init__(self, web=None, needs_setup=False, extra_effective=None, group=False):
        self.requests = []
        self.web = web if web is not None else [{'id':'web','clientId':'cleaning-suite-web','enabled':True,'publicClient':True,'standardFlowEnabled':True,'redirectUris':['https://site.invalid/*']}]
        self.needs_setup = needs_setup
        self.extra_effective = extra_effective
        self.group = group
    def __call__(self, request, timeout):
        self.requests.append(request)
        path = request.full_url.partition('/admin/realms/tenant-a')[2]
        if path == '/clients?clientId=cleaning-suite-web': return Response(self.web)
        if path == '/users/profile': return Response({'attributes': [] if self.needs_setup else [{
            'name':'cleaning_employee_id','multivalued':False,'permissions':{'view':['admin'],'edit':['admin']}}]})
        if path == '/clients?clientId=employee-runtime': return Response([] if self.needs_setup else [{
            'id':'runtime','clientId':'employee-runtime','enabled':True,'serviceAccountsEnabled':True,'publicClient':False}])
        if path == '/clients?clientId=realm-management': return Response([{'id':'management','clientId':'realm-management'}])
        if path == '/clients/runtime/service-account-user': return Response({'id':'runtime-user'})
        if path == '/users/runtime-user/groups?first=0&max=1': return Response([{'id':'unexpected-group'}] if self.group else [])
        if path == '/users/runtime-user/role-mappings/realm/composite': return Response([{'name':'default-roles-tenant-a'}])
        if path == '/clients?first=0&max=1000': return Response([{'id':'management','clientId':'realm-management'},{'id':'account','clientId':'account'}])
        if path.endswith('/role-mappings/clients/management/composite'):
            return Response([{'name':name} for name in ALLOWED + ([self.extra_effective] if self.extra_effective else [])])
        if path.endswith('/role-mappings/clients/account/composite'): return Response([])
        if path == '/users/runtime-user/role-mappings/clients/management': return Response([{'name':name} for name in ALLOWED])
        if '/roles/' in path: return Response({'id':'role','name':path.rsplit('/',1)[1]})
        if not path: return Response({'enabled':True,'attributes':{} if self.needs_setup else {'frontendUrl':'https://auth.invalid'},
            'smtpServer':{} if self.needs_setup else {'host':'smtp.invalid','from':'test@example.invalid'}})
        raise AssertionError('Unexpected test route ' + path)


class SetupCheckTests(unittest.TestCase):
    def run_check(self, transport):
        env = {'KEYCLOAK_MIGRATION_ADMIN_TOKEN':'synthetic-bearer-never-print',
               'KEYCLOAK_TENANT_SMTP_JSON':json.dumps({'host':'smtp.invalid','from':'test@example.invalid','password':'synthetic-smtp-secret'})}
        output = io.StringIO()
        with patch.dict(os.environ,env), patch.object(sys,'argv',ARGS), patch.object(setup,'urlopen',transport), redirect_stdout(output):
            result = setup.main()
        self.assertTrue(all(request.method == 'GET' for request in transport.requests))
        self.assertNotIn('synthetic-bearer',output.getvalue())
        self.assertNotIn('synthetic-smtp-secret',output.getvalue())
        return result

    def test_default_check_is_read_only_and_does_not_echo_credentials(self):
        self.assertEqual(2,self.run_check(FakeTransport(needs_setup=True)))

    def test_complete_configuration_is_read_only_and_clean(self):
        self.assertEqual(0,self.run_check(FakeTransport()))

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
        self.assertEqual(0,self.run_check(FakeTransport(web=[{'clientId':'cleaning-suite-web','enabled':True,'publicClient':True,'standardFlowEnabled':True,'redirectUris':['https://site.invalid/account/?tenant=tenant-a']}])))

    def test_effective_composite_privilege_is_rejected_even_if_direct_roles_are_safe(self):
        with self.assertRaisesRegex(RuntimeError, 'effective client/composite'):
            self.run_check(FakeTransport(extra_effective='manage-realm'))

    def test_group_derived_privileges_are_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'group-derived'):
            self.run_check(FakeTransport(group=True))

    def test_master_is_rejected_before_network(self):
        args = list(ARGS); args[args.index('tenant-a')] = 'master'
        with patch.object(sys,'argv',args), patch.object(setup,'urlopen') as http, redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit): setup.main()
            http.assert_not_called()


if __name__ == '__main__': unittest.main()
