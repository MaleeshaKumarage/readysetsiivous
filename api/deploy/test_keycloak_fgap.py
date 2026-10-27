"""FGAP fixed schema and additive setup regression tests."""
import copy
import unittest
from keycloak_fgap import configure, GROUPS, MAIN_SCOPES


class Server:
    def __init__(self):
        self.groups = {}; self.policies = {}; self.permissions = {}; self.writes = []
        self.realm = {'adminPermissionsEnabled': True}
    def api(self, method, path, body=None, missing=False):
        if method != 'GET':
            self.writes.append((method, path, copy.deepcopy(body)))
            if path == '': self.realm.update(body)
            elif path == '/groups': self.groups[body['name']] = {'id': body['name'], 'name': body['name'], 'path': '/' + body['name'], 'roles': []}
            elif path.endswith('/role-mappings/realm'): self.groups[path.split('/')[2]]['roles'] = body
            elif path.endswith('/policy/user'): self.policies[body['name']] = dict(body, id=body['name'])
            elif path.endswith('/permission/scope'): self.permissions[body['name']] = dict(body, id=body['name'])
            return
        if path.startswith('/roles/'): return {'id': path.rsplit('/', 1)[1], 'name': path.rsplit('/', 1)[1], 'composite': False}
        if path == '/clients?first=0&max=1000': return [{'id':'11111111-1111-1111-1111-111111111111','clientId':'realm-management'}]
        if path == '/clients?clientId=admin-permissions': return [{'id':'authz'}]
        if path.startswith('/groups?search='):
            group = self.groups.get(path.split('search=')[1].split('&')[0]); return [group] if group else []
        if path.startswith('/groups/'):
            group = self.groups[path.split('/')[2]]
            if path.endswith('/role-mappings/realm') or path.endswith('/role-mappings/realm/composite'): return group['roles']
            if '/role-mappings/clients/' in path or '/members?' in path or '/children?' in path: return []
            return group
        if '/policy/user/' in path: return self.policies[path.rsplit('/',1)[1]]
        if '/permission/scope/' in path:
            permission = self.permissions[path.rsplit('/',1)[1]]
            return dict({k:permission[k] for k in ('id','name','resourceType','decisionStrategy')}, type='scope',logic='POSITIVE')
        if '/policy/' in path and path.endswith(('/resources','/scopes','/associatedPolicies')):
            permission = self.permissions[path.split('/')[-2]]
            if path.endswith('/resources'): return [{'_id':'authorization-resource-'+identity,'name':identity} for identity in permission['resources']]
            if path.endswith('/scopes'): return [{'id':'scope-'+name,'name':name} for name in permission['scopes']]
            result=[]
            for binding in permission['policies']:
                policy = next((p for p in self.policies.values() if p['id']==binding or p['name']==binding),None)
                result.append({'id':policy['id'] if policy else binding,'name':policy['name'] if policy else binding,'type':'user'})
            return result
        if '/policy?' in path: return list(self.policies.values())
        if '/permission?' in path: return list(self.permissions.values())
        raise AssertionError(path)
    def run(self, apply=False):
        return configure(self.api, lambda label, method, path, body: self.api(method,path,body) if apply else None, self.realm, 'runtime-user', apply)


class FixedContractTests(unittest.TestCase):
    def test_check_is_read_only_and_apply_is_idempotent(self):
        server = Server(); self.assertTrue(all(v is None for v in server.run().values())); self.assertEqual([], server.writes)
        self.assertEqual(set(GROUPS), set(server.run(True)))
        self.assertEqual(['runtime-user'],server.policies['runtime-subject']['users'])
        self.assertEqual(MAIN_SCOPES,server.permissions['managed-members']['scopes'])
        self.assertEqual(['view','manage-membership'],server.permissions['fixed-application-role-groups']['scopes'])
        before = len(server.writes); server.run(True); self.assertEqual(before,len(server.writes))
    def test_existing_permission_requires_exact_single_policy_binding(self):
        for binding, remove_policy in (([], True), ([], False), (['foreign'], False),
                                       (['runtime-subject', 'foreign'], False),
                                       (['runtime-subject', 'runtime-subject'], False)):
            with self.subTest(binding=binding, remove_policy=remove_policy):
                server = Server(); server.run(True)
                server.permissions['managed-members']['policies'] = binding
                if remove_policy: server.policies.clear()
                before = len(server.writes)
                with self.assertRaisesRegex(RuntimeError, 'permission drift'):
                    server.run(True)
                self.assertEqual(before, len(server.writes))

    def test_existing_permission_accepts_exact_known_policy_id(self):
        server = Server(); server.run(True)
        server.policies['runtime-subject']['id'] = 'exact-runtime-policy-id'
        server.policies['exact-runtime-policy-id'] = server.policies.pop('runtime-subject')
        for permission in server.permissions.values():
            permission['policies'] = ['exact-runtime-policy-id']
        before = len(server.writes); server.run(True)
        self.assertEqual(before, len(server.writes))

    def test_malformed_native_relationships_fail_before_changes(self):
        for suffix in ('/resources', '/scopes', '/associatedPolicies'):
            for malformed in (None, {}, [None], [{'id':None, 'name':None}]):
                with self.subTest(suffix=suffix, malformed=malformed):
                    server=Server(); server.run(True); before=len(server.writes)
                    original=server.api
                    def broken(method,path,body=None,missing=False):
                        if path.endswith(suffix): return malformed
                        return original(method,path,body,missing)
                    server.api=broken
                    with self.assertRaisesRegex(RuntimeError, 'Malformed'):
                        server.run(True)
                    self.assertEqual(before,len(server.writes))

    def test_trusted_management_metadata_is_additive_and_mismatch_fails(self):
        server=Server(); server.realm['attributes']={'other':'preserved'}
        server.run(); self.assertNotIn('cleaning_runtime_management_client_id',server.realm['attributes'])
        server.run(True)
        self.assertEqual({'other':'preserved','cleaning_runtime_management_client_id':'11111111-1111-1111-1111-111111111111'},server.realm['attributes'])
        server.realm['attributes']['cleaning_runtime_management_client_id']='foreign-client'
        before=len(server.writes)
        with self.assertRaisesRegex(RuntimeError,'metadata drift'): server.run(True)
        self.assertEqual(before,len(server.writes))

    def test_drift_is_rejected_without_mutations(self):
        for change in ('path','parent','child','composite','permission','policy','foreign'):
            with self.subTest(change=change):
                server=Server(); server.run(True)
                if change=='path': server.groups['managed-users']['path']='/other'
                if change=='parent': server.groups['managed-users']['parentId']='parent'
                if change=='child': server.groups['managed-users']['subGroupCount']=1
                if change=='composite': server.groups['app-admin']['roles'][0]['composite']=True
                if change=='permission': server.permissions['managed-members']['scopes'].append('manage')
                if change=='policy': server.policies['runtime-subject']['users'].append('foreign')
                if change=='foreign': server.permissions['foreign']={'id':'foreign','name':'foreign'}
                before=len(server.writes)
                with self.assertRaises(RuntimeError): server.run(True)
                self.assertEqual(before,len(server.writes))


if __name__=='__main__': unittest.main()
