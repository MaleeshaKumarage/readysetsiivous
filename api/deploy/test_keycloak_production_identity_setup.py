"""Safety regressions for the production-only, explicit UUID enrollment boundary."""
import copy
import importlib.util
import json
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.parse import parse_qs

SPEC = importlib.util.spec_from_file_location('production_identity_setup', Path(__file__).with_name('keycloak-production-identity-setup.py'))
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)

USER = '11111111-1111-4111-8111-111111111111'
EMPLOYEE = '22222222-2222-4222-8222-222222222222'
RUNTIME = '33333333-3333-4333-8333-333333333333'
SUBJECT = '44444444-4444-4444-8444-444444444444'
MANAGEMENT = '55555555-5555-4555-8555-555555555555'
ROLE = '66666666-6666-4666-8666-666666666666'
DEFAULT = '77777777-7777-4777-8777-777777777777'
GROUPS = {name: f'88888888-8888-4888-8888-{i:012d}' for i, name in enumerate(('managed-users', 'app-employee', 'app-manager', 'app-admin'), 1)}


def plan():
    return {'schema_version': 1, 'tenant': 'tenant-a', 'expected_issuer': module.PUBLIC_ORIGIN + '/realms/tenant-a',
            'expected_version': '26.8.1', 'runtime_client_id': 'employee-runtime',
            'maintenance': {'api_container_name': 'production-api-1', 'api_container_id': 'a' * 64, 'compose_project': 'production'},
            'employees': [{'user_id': USER, 'employee_id': EMPLOYEE, 'email': 'employee@example.invalid',
                           'role': 'employee', 'expected_enabled': True, 'enabled': False, 'allow_missing_owner': True}],
            'tenant_admin_owners': []}


class NativeFixture:
    def __init__(self):
        self.realm = {'realm': 'tenant-a', 'enabled': True, 'adminPermissionsEnabled': False,
                      'attributes': {'frontendUrl': module.PUBLIC_ORIGIN, 'unrelated': 'preserved'}}
        self.user = {'id': USER, 'username': 'employee@example.invalid', 'email': 'employee@example.invalid',
                     'firstName': 'Synthetic', 'lastName': 'Employee', 'enabled': True, 'emailVerified': True,
                     'attributes': {'unrelated': ['keep']}, 'requiredActions': ['CONFIGURE_TOTP'],
                     'credentials': [{'type': 'password', 'value': 'never-replay-this-field'}]}
        self.memberships = []
        self.direct = [{'id': ROLE, 'name': 'employee', 'composite': False}, {'id': DEFAULT, 'name': 'default-roles-tenant-a'}]
        self.credential_metadata = [{'id': 'password-id', 'type': 'password', 'createdDate': 100}, {'id': 'otp-id', 'type': 'otp', 'createdDate': 200}]
        self.client = {'id': RUNTIME, 'clientId': 'employee-runtime', 'enabled': True, 'publicClient': False,
                       'serviceAccountsEnabled': True, 'standardFlowEnabled': False, 'directAccessGrantsEnabled': False, 'fullScopeAllowed': False}
        self.profile = {'attributes': [], 'unmanagedAttributePolicy': 'DISABLED', 'groups': [{'name': 'preserved'}]}
        self.writes = []
        self.privileged = False

    def api(self, method, path='', body=None, missing=False):
        if method != 'GET':
            self.writes.append((method, path, copy.deepcopy(body)))
            if method == 'PUT' and path == '/users/profile': self.profile = copy.deepcopy(body)
            elif method == 'PUT' and path == '/users/' + USER:
                self.user.update(copy.deepcopy(body)); self.realm['attributes']['cleaning_invitation_epoch'] = 'fresh-after-enrollment'
            elif method == 'PUT' and path.startswith('/users/' + USER + '/groups/'):
                identity = path.rsplit('/', 1)[1]
                name = next(name for name, gid in GROUPS.items() if gid == identity)
                self.memberships.append({'id': identity, 'path': '/' + name})
            elif method == 'DELETE' and path.endswith('/role-mappings/realm'):
                ids = {r['id'] for r in body}; self.direct = [r for r in self.direct if r['id'] not in ids]
            elif method == 'PUT' and not path:
                self.realm.update(copy.deepcopy(body))
            else: raise AssertionError((method, path))
            return None
        if not path: return copy.deepcopy(self.realm)
        if path == '@serverinfo': return {'systemInfo': {'version': '26.8.1'}}
        if path == '/users/profile': return copy.deepcopy(self.profile)
        if path == '/clients?clientId=realm-management': return [{'id': MANAGEMENT, 'clientId': 'realm-management'}]
        if path == '/clients?clientId=employee-runtime': return [copy.deepcopy(self.client)]
        if path == '/clients?first=0&max=1000': return [{'id': MANAGEMENT, 'clientId': 'realm-management'}, copy.deepcopy(self.client)]
        if path == '/clients/' + RUNTIME + '/service-account-user': return {'id': SUBJECT}
        if path.startswith('/clients/') and '/roles?' in path: return []
        if path.startswith('/roles?'): return []
        if path.startswith('/groups?'): return []
        if path.startswith('/roles/'):
            name = path.rsplit('/', 1)[1]
            return {'id': ROLE if name == 'employee' else DEFAULT, 'name': name, 'composite': False}
        if path.startswith('/users?'):
            query = parse_qs(path.partition('?')[2]); start = int(query['first'][0]); size = int(query['max'][0])
            return [copy.deepcopy(self.user)][start:start+size]
        if path == '/users/' + USER: return copy.deepcopy(self.user)
        if '/role-mappings/clients/' in path:
            return [{'id': 'privilege', 'name': 'manage-users'}] if self.privileged and USER in path else []
        if path.startswith('/users/' + USER + '/groups?'): return copy.deepcopy(self.memberships)
        if path == '/users/' + USER + '/credentials': return copy.deepcopy(self.credential_metadata)
        if path == '/users/' + USER + '/role-mappings/realm': return copy.deepcopy(self.direct)
        if path == '/users/' + USER + '/role-mappings/realm/composite':
            roles = copy.deepcopy(self.direct)
            if any(g['path'] == '/app-employee' for g in self.memberships): roles.append({'id': ROLE, 'name': 'employee', 'composite': False})
            return roles
        raise AssertionError(path)

    def configure(self, api, change, realm, client, subject, apply=False):
        if module.CLIENT_ATTRIBUTE not in realm['attributes']:
            change('bind extension to exact runtime client', 'PUT', '', {'attributes': dict(realm['attributes'], **{module.CLIENT_ATTRIBUTE: RUNTIME})})
        return GROUPS.copy()


class ProductionEnrollmentTests(unittest.TestCase):
    def run_migration(self, fixture, document=None, apply=True, maintenance=None):
        with patch.object(module, 'inspect_groups', return_value=GROUPS.copy()), patch.object(module, 'configure', side_effect=fixture.configure):
            return module.migrate(fixture.api, document or plan(), 'protected-runtime-secret', maintenance or (lambda: None),
                                  lambda: None, lambda: None, apply=apply)

    def test_duplicate_ids_abort_before_network_or_mutation(self):
        document = plan(); document['employees'].append(dict(document['employees'][0]))
        fixture = NativeFixture()
        with self.assertRaisesRegex(RuntimeError, 'Duplicate'):
            self.run_migration(fixture, document)
        self.assertEqual([], fixture.writes)

    def test_conflicting_owner_aborts_before_any_profile_or_client_change(self):
        fixture = NativeFixture(); fixture.user['attributes'][module.OWNER] = ['99999999-9999-4999-8999-999999999999']
        with self.assertRaisesRegex(RuntimeError, 'ownership conflicts'):
            self.run_migration(fixture)
        self.assertEqual([], fixture.writes)

    def test_privileged_target_aborts_before_enrollment(self):
        fixture = NativeFixture(); fixture.privileged = True
        with self.assertRaisesRegex(RuntimeError, 'administration roles'):
            self.run_migration(fixture)
        self.assertEqual([], fixture.writes)

    def test_maintenance_failure_permits_no_write(self):
        fixture = NativeFixture()
        def active(): raise RuntimeError('API is still active')
        with self.assertRaisesRegex(RuntimeError, 'active'):
            self.run_migration(fixture, maintenance=active)
        self.assertEqual([], fixture.writes)

    def test_readonly_reports_changes_without_writes(self):
        fixture = NativeFixture()
        result = self.run_migration(fixture, apply=False)
        self.assertEqual('check', result['mode']); self.assertTrue(result['changes'])
        self.assertEqual([], fixture.writes)

    def test_activation_is_last_and_preserves_fresh_attributes(self):
        fixture = NativeFixture(); calls = []
        self.run_migration(fixture, maintenance=lambda: calls.append('checked'))
        self.assertEqual(('PUT', ''), fixture.writes[-1][:2])
        self.assertEqual(RUNTIME, fixture.realm['attributes'][module.CLIENT_ATTRIBUTE])
        self.assertEqual('fresh-after-enrollment', fixture.realm['attributes']['cleaning_invitation_epoch'])
        self.assertEqual('preserved', fixture.realm['attributes']['unrelated'])
        self.assertGreaterEqual(len(calls), len(fixture.writes))

    def test_credential_mfa_required_actions_and_nonapp_roles_are_preserved(self):
        fixture = NativeFixture(); original = copy.deepcopy(fixture.user)
        self.run_migration(fixture)
        patches = [body for method, path, body in fixture.writes if method == 'PUT' and path == '/users/' + USER]
        self.assertTrue(patches)
        self.assertTrue(all(not set(body) & {'credentials', 'requiredActions', 'emailVerified', 'federationLink'} for body in patches))
        self.assertEqual(original['requiredActions'], fixture.user['requiredActions'])
        self.assertEqual(original['credentials'], fixture.user['credentials'])
        self.assertEqual(original['firstName'], fixture.user['firstName'])
        self.assertEqual([{'id': DEFAULT, 'name': 'default-roles-tenant-a'}], fixture.direct)
        joins = [i for i, (_, path, _) in enumerate(fixture.writes) if '/groups/' in path]
        removal = next(i for i, (method, _, _) in enumerate(fixture.writes) if method == 'DELETE')
        self.assertLess(max(joins), removal)

    def test_exact_plan_rerun_converges_without_mutations(self):
        fixture = NativeFixture(); self.run_migration(fixture)
        fixture.writes.clear()
        result = self.run_migration(fixture)
        self.assertEqual([], fixture.writes)
        self.assertEqual([], result['changes'])

    def test_ownerless_admin_requires_explicit_approval(self):
        document = plan(); document['employees'] = []
        document['tenant_admin_owners'] = [{'user_id': USER, 'email': 'employee@example.invalid', 'expected_enabled': True, 'approve_ownerless_admin': False}]
        with self.assertRaisesRegex(RuntimeError, 'approval'):
            module.validate_plan(document)

    def test_public_admin_transport_and_wrong_issuer_are_refused(self):
        with self.assertRaisesRegex(RuntimeError, 'loopback'):
            module.Transport('https://auth.readysetsiivous.fi', 'tenant-a', 'admin-token', 'employee-runtime', 'runtime-secret')
        document = plan(); document['expected_issuer'] = 'https://unexpected.invalid/realms/tenant-a'
        with self.assertRaisesRegex(RuntimeError, 'issuer'):
            module.validate_plan(document)

    def maintenance_runner(self, active=False, wrong_id=False, replica=False):
        def run(command, **kwargs):
            if command[0] == 'systemctl':
                return subprocess.CompletedProcess(command, 0, 'ActiveState=' + ('active' if active else 'inactive') + '\nLoadState=loaded\n', '')
            if command[1] == 'inspect':
                payload = {'Id': ('b' if wrong_id else 'a') * 64,
                           'State': {'Running': False, 'Restarting': False, 'Status': 'exited'},
                           'Labels': {'com.docker.compose.service': 'api', 'com.docker.compose.project': 'production'}}
                return subprocess.CompletedProcess(command, 0, json.dumps(payload), '')
            if command[1] == 'ps':
                return subprocess.CompletedProcess(command, 0, 'unexpected-replica\n' if replica else '', '')
            raise AssertionError(command)
        return run

    def test_maintenance_accepts_only_exact_stopped_api(self):
        with patch.object(module.subprocess, 'run', side_effect=self.maintenance_runner()):
            module.Maintenance(plan()['maintenance'])()

    def test_maintenance_rejects_active_ingress(self):
        with patch.object(module.subprocess, 'run', side_effect=self.maintenance_runner(active=True)):
            with self.assertRaisesRegex(RuntimeError, 'ingress'):
                module.Maintenance(plan()['maintenance'])()

    def test_maintenance_rejects_stale_container_identity(self):
        with patch.object(module.subprocess, 'run', side_effect=self.maintenance_runner(wrong_id=True)):
            with self.assertRaisesRegex(RuntimeError, 'exact reviewed API'):
                module.Maintenance(plan()['maintenance'])()

    def test_maintenance_rejects_running_replica(self):
        with patch.object(module.subprocess, 'run', side_effect=self.maintenance_runner(replica=True)):
            with self.assertRaisesRegex(RuntimeError, 'replicas'):
                module.Maintenance(plan()['maintenance'])()

    def test_failed_group_join_keeps_legacy_access_grant(self):
        fixture = NativeFixture()
        original_api = fixture.api
        def ignored_membership(method, path='', body=None, missing=False):
            if method == 'PUT' and '/groups/' in path:
                return None
            return original_api(method, path, body, missing)
        fixture.api = ignored_membership
        with self.assertRaisesRegex(RuntimeError, 'legacy grants were retained'):
            self.run_migration(fixture)
        self.assertTrue(any(r['name'] == 'employee' for r in fixture.direct))
        self.assertFalse(any(method == 'DELETE' for method, _, _ in fixture.writes))


if __name__ == '__main__': unittest.main()
