"""Pinned development FGAP V2 contract; no identity enrollment operations."""
from urllib.parse import quote
from uuid import UUID

GROUPS = {'managed-users': None, 'app-employee': 'employee', 'app-manager': 'manager', 'app-admin': 'admin'}
MAIN_SCOPES = ['view', 'view-members', 'manage-members', 'manage-membership', 'manage-membership-of-members']
RUNTIME_ROLES = {'query-users', 'view-realm'}


def configure(api, change, realm, subject, apply=False):
    """Validate all existing authorization state before emitting additive changes.

    api(method, relative_path, body=None, missing=False); change(label,method,path,body).
    Returns fixed root name -> immutable server group ID (None when check finds missing).
    """
    transport = api
    def api(method, path, body=None, missing=False):
        value = transport(method, path, body, missing)
        if method != 'GET':
            return value
        if value is None and missing:
            return None
        list_response = ('?' in path or '/role-mappings/' in path
                         or path.endswith(('/resources', '/scopes', '/associatedPolicies')))
        if list_response:
            if not isinstance(value, list) or any(not isinstance(item, dict) for item in value):
                raise RuntimeError('Malformed Keycloak authorization response; manual review required')
            for item in value:
                if path.endswith('/resources') and '_id' in item and 'id' not in item:
                    item['id'] = item['_id']  # Native ResourceRepresentation uses JSON _id.
                if path.endswith('/resources') and (not isinstance(item.get('name'), str) or not item['name']):
                    raise RuntimeError('Malformed Keycloak resource identity; manual review required')
                if ('/clients?' in path or '/groups?' in path or '/members?' in path
                        or '/policy?' in path or '/permission?' in path
                        or path.endswith(('/resources', '/associatedPolicies'))):
                    if not isinstance(item.get('id'), str) or not item['id']:
                        raise RuntimeError('Malformed Keycloak identity response; manual review required')
        elif not isinstance(value, dict):
            raise RuntimeError('Malformed Keycloak authorization response; manual review required')
        if path.endswith('/scopes') and any(not isinstance(item.get('name'), str) or not item['name'] for item in value):
            raise RuntimeError('Malformed Keycloak scope response; manual review required')
        if path.startswith('/groups/') and not list_response and (not isinstance(value.get('id'), str) or not value['id']):
            raise RuntimeError('Malformed Keycloak group response; manual review required')
        return value
    if not isinstance(realm, dict) or not isinstance(subject, str) or not subject:
        raise RuntimeError('Malformed FGAP setup input')
    for role in ('employee', 'manager', 'admin'):

        representation = api('GET', '/roles/' + role, missing=True)
        if representation and representation.get('composite'):
            raise RuntimeError('Application role must be flat; manual review required')
    pending = []
    groups = {}
    inventory = api('GET', '/clients?first=0&max=1000')
    if len(inventory) >= 1000:
        raise RuntimeError('Client inventory incomplete; manual review required')
    management = [entry for entry in inventory if entry.get('clientId') == 'realm-management']
    if len(management) != 1:
        raise RuntimeError('Realm management client missing or ambiguous')
    try:
        if str(UUID(management[0]['id'])) != management[0]['id']:
            raise ValueError()
    except (ValueError, AttributeError, TypeError):
        raise RuntimeError('Realm management client identity is invalid; manual review required') from None
    attributes = realm.get('attributes') or {}
    if not isinstance(attributes, dict):
        raise RuntimeError('Malformed realm attributes')
    metadata_key = 'cleaning_runtime_management_client_id'
    metadata = attributes.get(metadata_key)
    if metadata is not None and metadata != management[0]['id']:
        raise RuntimeError('Trusted realm management metadata drift; manual review required')
    for name, role in GROUPS.items():
        matches = api('GET', '/groups?search=' + name + '&exact=true&first=0&max=2')
        if len(matches) > 1:
            raise RuntimeError('Ambiguous fixed group; manual review required')
        if not matches:
            groups[name] = None
            continue
        group = api('GET', '/groups/' + quote(matches[0]['id'], safe=''))
        if (group.get('name') != name or group.get('path') != '/' + name or group.get('parentId')
                or group.get('subGroupCount', 0) or group.get('subGroups')
                or api('GET', '/groups/' + quote(group['id'], safe='') + '/children?first=0&max=1')):
            raise RuntimeError('Fixed group must be a plain root without children; manual review required')
        groups[name] = group['id']
        gid = quote(group['id'], safe='')
        direct = api('GET', '/groups/' + gid + '/role-mappings/realm')
        effective = api('GET', '/groups/' + gid + '/role-mappings/realm/composite')
        expected = {role} if role else set()
        for mapped in (direct, effective):
            if any(r.get('name') not in expected or r.get('composite') for r in mapped):
                raise RuntimeError('Unexpected fixed group role/composite; manual review required')
        if {r['name'] for r in direct} != {r['name'] for r in effective}:
            raise RuntimeError('Inherited fixed group roles; manual review required')
        if role and not direct:
            pending.append(('map', name, role))
        for client in inventory:
            for suffix in ('', '/composite'):
                if api('GET', '/groups/' + gid + '/role-mappings/clients/' + quote(client['id'], safe='') + suffix):
                    raise RuntimeError('Fixed group client privileges; manual review required')
        members = api('GET', '/groups/' + gid + '/members?first=0&max=1000&briefRepresentation=false')
        if len(members) >= 1000:
            raise RuntimeError('Group member inventory incomplete; manual review required')
        for member in members:
            if member.get('serviceAccountClientId') or member.get('username', '').startswith('service-account-'):
                raise RuntimeError('Service account in fixed group; manual review required')
            uid = quote(member['id'], safe='')
            for client in inventory:
                effective_roles = api('GET', '/users/' + uid + '/role-mappings/clients/' + quote(client['id'], safe='') + '/composite')
                if client.get('clientId') == 'realm-management' and effective_roles:
                    raise RuntimeError('Protected administrator in fixed group; manual review required')
    authz_clients = api('GET', '/clients?clientId=admin-permissions') if realm.get('adminPermissionsEnabled') else []
    if len(authz_clients) > 1:
        raise RuntimeError('Ambiguous admin permissions client')
    authz = '/clients/' + quote(authz_clients[0]['id'], safe='') + '/authz/resource-server' if authz_clients else None
    policies = api('GET', authz + '/policy?first=0&max=1000') if authz else []
    permissions = api('GET', authz + '/permission?first=0&max=1000') if authz else []
    if len(policies) >= 1000 or len(permissions) >= 1000:
        raise RuntimeError('Permission inventory incomplete; manual review required')
    # A dedicated setup contract refuses any pre-existing foreign permission; no blanket reset.
    expected_names = {'runtime-subject', 'managed-members', 'fixed-application-role-groups'}
    if any(p.get('name') not in expected_names for p in policies + permissions):
        raise RuntimeError('Unexpected authorization policy/permission; manual review required')
    policy = {'name': 'runtime-subject', 'logic': 'POSITIVE', 'users': [subject]}
    def desired_permissions():
        return [dict(name='managed-members', resourceType='Groups', resources=[groups['managed-users']], scopes=list(MAIN_SCOPES),
                     policies=['runtime-subject'], decisionStrategy='UNANIMOUS'),
                dict(name='fixed-application-role-groups', resourceType='Groups', resources=[groups[n] for n in GROUPS if n != 'managed-users'],
                     scopes=['view', 'manage-membership'], policies=['runtime-subject'], decisionStrategy='UNANIMOUS')]
    # Verified pinned admin-client PolicyResource and server PolicyResourceService:
    # https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/integration/admin-client-core/src/main/java/org/keycloak/admin/client/resource/PolicyResource.java
    # Keycloak 26.8.1 scope GET is sparse. PolicyResource relationship endpoints
    # are canonical for resource, scope and associated-policy bindings.
    seen = set()
    for entry in policies + permissions:
        name = entry['name']
        if name in seen:
            continue  # /policy includes permission policies on Keycloak.
        seen.add(name)
        if name == 'runtime-subject':
            full = api('GET', authz + '/policy/user/' + quote(entry['id'], safe=''))
            if (full.get('type', 'user') != 'user' or full.get('logic') != 'POSITIVE'
                    or full.get('users') != [subject]):
                raise RuntimeError('Runtime user policy drift; manual review required')
        else:
            full = api('GET', authz + '/permission/scope/' + quote(entry['id'], safe=''))
            expected = next(p for p in desired_permissions() if p['name'] == name)
            relationship = authz + '/policy/' + quote(entry['id'], safe='')
            resources = api('GET', relationship + '/resources')
            scopes = api('GET', relationship + '/scopes')
            associated = api('GET', relationship + '/associatedPolicies')
            bound_policy = [p['id'] for p in policies if p.get('name') == 'runtime-subject']
            policy_matches = (len(bound_policy) == 1 and len(associated) == 1
                              and associated[0]['id'] == bound_policy[0]
                              and associated[0].get('name') == 'runtime-subject'
                              and associated[0].get('type') == 'user')
            # The resource's authorization _id is not the underlying group UUID.
            # FGAP names each resource using that group UUID; displayName is mutable.
            resource_ids = [r['name'] for r in resources]
            scope_names = [r.get('name') for r in scopes]
            if (not policy_matches or len(resource_ids) != len(expected['resources'])
                    or set(resource_ids) != set(expected['resources'])
                    or len(scope_names) != len(expected['scopes'])
                    or set(scope_names) != set(expected['scopes'])
                    or any(full.get(k) != expected[k] for k in ('resourceType', 'decisionStrategy'))
                    or full.get('type') != 'scope' or full.get('logic') != 'POSITIVE'):
                raise RuntimeError('Group scope permission drift; manual review required')
    if metadata is None:
        change('missing trusted realm management metadata', 'PUT', '', {'attributes': dict(attributes, **{metadata_key: management[0]['id']})})
    if not realm.get('adminPermissionsEnabled'):
        change('enable development FGAP V2', 'PUT', '', {'adminPermissionsEnabled': True})
    for name, gid in list(groups.items()):
        if gid is None:
            change('missing fixed group ' + name, 'POST', '/groups', {'name': name})
            if apply:
                found = api('GET', '/groups?search=' + name + '&exact=true&first=0&max=2')
                if len(found) != 1: raise RuntimeError('Created group could not be resolved')
                groups[name] = found[0]['id']
            if GROUPS[name]: pending.append(('map', name, GROUPS[name]))
    for _, name, role in pending:
        representation = api('GET', '/roles/' + role, missing=True)
        if representation and representation.get('composite'):
            raise RuntimeError('Application role must be flat')
        if representation:
            change('missing fixed role mapping ' + name, 'POST', '/groups/' + quote(groups[name] or 'missing', safe='') + '/role-mappings/realm', [representation])
    if not authz and apply:
        entries = api('GET', '/clients?clientId=admin-permissions')
        if len(entries) != 1: raise RuntimeError('FGAP V2 admin-permissions client missing')
        authz = '/clients/' + quote(entries[0]['id'], safe='') + '/authz/resource-server'
    authz = authz or '/clients/missing/authz/resource-server'
    if not any(p['name'] == 'runtime-subject' for p in policies):
        change('missing exact runtime user policy', 'POST', authz + '/policy/user', policy)
    for permission in desired_permissions():
        if not any(p['name'] == permission['name'] for p in policies + permissions):
            change('missing bounded group permission ' + permission['name'], 'POST', authz + '/permission/scope', permission)
    return groups
