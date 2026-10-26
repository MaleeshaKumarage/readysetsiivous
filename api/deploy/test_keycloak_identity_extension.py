"""Safety regression tests for explicit extension activation and reviewed migration."""
import copy
import unittest

from keycloak_identity_extension import configure, validate_defaults, AUDIENCE, CLIENT_ATTRIBUTE, MAPPER_NAME
from test_keycloak_fgap import Server

CLIENT_ID = '22222222-2222-2222-2222-222222222222'
MANAGEMENT_ID = '11111111-1111-1111-1111-111111111111'


class ExtensionServer(Server):
    def __init__(self):
        super().__init__()
        self.realm = {'realm': 'tenant-a', 'enabled': True, 'adminPermissionsEnabled': False,
                      'attributes': {'other': 'preserved'}}
        self.client = {'id': CLIENT_ID, 'clientId': 'employee-runtime', 'fullScopeAllowed': False}
        self.direct = []
        self.extra_effective = []
        self.mappers = []
        self.refuse_revoke = False

    def api(self, method, path, body=None, missing=False):
        if method == 'GET':
            if not path:
                return copy.deepcopy(self.realm)
            if path == '/users/runtime-user/groups?first=0&max=1':
                return []
            if path == '/users/runtime-user/role-mappings/realm/composite':
                return [{'name': 'default-roles-tenant-a'}]
            if path == '/users/runtime-user/role-mappings/clients/' + MANAGEMENT_ID:
                return copy.deepcopy(self.direct)
            if path == '/users/runtime-user/role-mappings/clients/' + MANAGEMENT_ID + '/composite':
                return copy.deepcopy(self.direct + self.extra_effective)
            if path == '/clients/' + CLIENT_ID + '/protocol-mappers/models':
                return copy.deepcopy(self.mappers)
        if method == 'DELETE':
            self.writes.append((method, path, copy.deepcopy(body)))
            if '/authz/resource-server/policy/' in path:
                identity = path.rsplit('/', 1)[1]
                self.permissions.pop(identity, None)
                self.policies.pop(identity, None)
            elif path.endswith('/role-mappings/clients/' + MANAGEMENT_ID):
                if not self.refuse_revoke:
                    self.direct = []
            else:
                raise AssertionError(path)
            return
        if method == 'POST' and path == '/clients/' + CLIENT_ID + '/protocol-mappers/models':
            self.writes.append((method, path, copy.deepcopy(body)))
            self.mappers.append(dict(body, id='audience-mapper'))
            return
        if method == 'PUT' and path == '/clients/' + CLIENT_ID:
            self.writes.append((method, path, copy.deepcopy(body)))
            self.client.update(body)
            return
        return super().api(method, path, body, missing)

    def extension(self, apply=False, migrate=False):
        changes = []
        def change(label, method, path, body):
            changes.append(label)
            if apply:
                self.api(method, path, body)
        result = configure(self.api, change, copy.deepcopy(self.realm), copy.deepcopy(self.client),
                           'runtime-user', apply=apply, migrate_fgap=migrate)
        return result, changes

    def seed_legacy(self):
        self.realm['adminPermissionsEnabled'] = True
        super().run(True)
        self.direct = [{'id': 'query', 'name': 'query-users'}, {'id': 'view', 'name': 'view-realm'}]
        self.writes.clear()


class ExtensionSetupTests(unittest.TestCase):
    def test_default_role_graph_rejects_indirect_administration_before_creation(self):
        writes = []
        def api(method, path, body=None, missing=False):
            if method != 'GET':
                writes.append((method, path))
                raise AssertionError('Default preflight must never mutate')
            if path == '/clients?first=0&max=1000':
                return [{'id': 'account-client', 'clientId': 'account'}]
            if path.startswith('/roles/default-roles-'):
                return {'id': 'default', 'name': 'default-roles-tenant-a', 'composite': True}
            if path == '/roles-by-id/default/composites':
                return [{'id': 'nested', 'name': 'offline_access', 'composite': True}]
            if path == '/roles-by-id/nested/composites':
                return [{'id': 'admin', 'name': 'manage-users', 'clientRole': True, 'containerId': MANAGEMENT_ID}]
            raise AssertionError(path)
        with self.assertRaisesRegex(RuntimeError, 'Unsafe realm default client privileges'):
            validate_defaults(api, {'realm': 'tenant-a'})
        self.assertEqual([], writes)

    def test_standard_default_account_roles_are_safe_without_mutation(self):
        def api(method, path, body=None, missing=False):
            self.assertEqual('GET', method)
            if path == '/clients?first=0&max=1000':
                return [{'id': 'account-client', 'clientId': 'account'}]
            if path.startswith('/roles/default-roles-'):
                return {'id': 'default', 'name': 'default-roles-tenant-a', 'composite': True}
            if path == '/roles-by-id/default/composites':
                return [{'id': 'account-role', 'name': 'view-profile', 'clientRole': True, 'containerId': 'account-client'}]
            raise AssertionError(path)
        validate_defaults(api, {'realm': 'tenant-a'})

    def test_fresh_check_is_read_only_and_never_plans_admin_grants(self):
        server = ExtensionServer()
        groups, changes = server.extension()
        self.assertEqual([], server.writes)
        self.assertTrue(all(v is None for v in groups.values()))
        self.assertIn('missing dedicated identity audience', changes)
        self.assertNotIn(CLIENT_ATTRIBUTE, server.realm['attributes'])

    def test_fresh_apply_is_idempotent_and_activation_is_last(self):
        server = ExtensionServer()
        groups, _ = server.extension(True)
        self.assertTrue(all(groups.values()))
        self.assertEqual(CLIENT_ID, server.realm['attributes'][CLIENT_ATTRIBUTE])
        self.assertEqual('preserved', server.realm['attributes']['other'])
        self.assertEqual(AUDIENCE, server.mappers[0]['config']['included.custom.audience'])
        self.assertEqual(('PUT', ''), server.writes[-1][:2])
        self.assertFalse(any('/authz/' in p or '/role-mappings/clients/' in p for _, p, _ in server.writes))
        before = len(server.writes)
        self.assertEqual([], server.extension(True)[1])
        self.assertEqual(before, len(server.writes))

    def test_legacy_access_requires_explicit_migration_without_mutations(self):
        server = ExtensionServer(); server.seed_legacy()
        with self.assertRaisesRegex(RuntimeError, 'explicit --migrate-fgap'):
            server.extension(True)
        self.assertEqual([], server.writes)

    def test_reviewed_migration_removes_permissions_and_roles_before_binding(self):
        server = ExtensionServer(); server.seed_legacy()
        server.extension(True, migrate=True)
        self.assertEqual([], server.direct)
        self.assertEqual({}, server.policies)
        self.assertEqual({}, server.permissions)
        self.assertFalse(server.realm['adminPermissionsEnabled'])
        self.assertEqual(('PUT', ''), server.writes[-1][:2])
        self.assertIn(CLIENT_ATTRIBUTE, server.writes[-1][2]['attributes'])
        self.assertTrue(all(m == 'DELETE' for m, _, _ in server.writes[:4]))

    def test_incomplete_role_revocation_does_not_activate_extension(self):
        server = ExtensionServer(); server.seed_legacy(); server.refuse_revoke = True
        with self.assertRaisesRegex(RuntimeError, 'grants remain'):
            server.extension(True, migrate=True)
        self.assertNotIn(CLIENT_ATTRIBUTE, server.realm['attributes'])

    def test_unexpected_direct_or_inherited_privileges_are_never_revoked(self):
        for inherited in (False, True):
            server = ExtensionServer()
            if inherited:
                server.extra_effective = [{'name': 'query-users'}]
            else:
                server.direct = [{'name': 'manage-users'}]
            with self.assertRaisesRegex(RuntimeError, 'administration'):
                server.extension(True, migrate=True)
            self.assertEqual([], server.writes)

    def test_foreign_policy_prevents_even_explicit_migration(self):
        for enabled in (True, False):
            with self.subTest(fgap_enabled=enabled):
                server = ExtensionServer(); server.seed_legacy()
                server.realm['adminPermissionsEnabled'] = enabled
                server.policies['foreign'] = {'id': 'foreign', 'name': 'foreign'}
                with self.assertRaisesRegex(RuntimeError, 'Unexpected authorization'):
                    server.extension(True, migrate=True)
                self.assertEqual([], server.writes)

    def test_mapper_or_binding_drift_never_gets_overwritten(self):
        for field in ('mapper', 'binding'):
            server = ExtensionServer()
            if field == 'mapper':
                server.mappers = [{'name': MAPPER_NAME, 'protocol': 'openid-connect',
                                   'protocolMapper': 'oidc-audience-mapper', 'config': {}}]
            else:
                server.realm['attributes'][CLIENT_ATTRIBUTE] = '33333333-3333-3333-3333-333333333333'
            with self.assertRaisesRegex(RuntimeError, 'drift'):
                server.extension(True)
            self.assertEqual([], server.writes)

    def test_verified_native_mapper_defaults_are_idempotent_but_not_arbitrary_changes(self):
        server = ExtensionServer(); server.extension(True)
        mapper = server.mappers[0]
        self.assertEqual('true', mapper['config']['introspection.token.claim'])
        self.assertEqual('false', mapper['config']['userinfo.token.claim'])
        for key in ('introspection.token.claim', 'userinfo.token.claim'):
            mapper['config'].pop(key)
        before = len(server.writes)
        self.assertEqual([], server.extension(True)[1])
        self.assertEqual(before, len(server.writes))
        for key, value in (('id.token.claim', 'true'), ('userinfo.token.claim', 'true'),
                           ('introspection.token.claim', 'false'), ('unknown.claim', 'true')):
            with self.subTest(key=key):
                unsafe = ExtensionServer(); unsafe.extension(True); unsafe.writes.clear()
                unsafe.mappers[0]['config'][key] = value
                with self.assertRaisesRegex(RuntimeError, 'audience mapper drift'):
                    unsafe.extension(True)
                self.assertEqual([], unsafe.writes)


if __name__ == '__main__':
    unittest.main()
