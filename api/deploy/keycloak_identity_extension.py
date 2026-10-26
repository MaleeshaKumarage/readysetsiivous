"""Explicit development identity-extension setup; never grants Admin REST access."""
from urllib.parse import quote
from uuid import UUID

from keycloak_fgap import configure as validate_legacy_contract, GROUPS, RUNTIME_ROLES

AUDIENCE = 'cleaning-identity-runtime'
CLIENT_ATTRIBUTE = 'cleaning_identity_runtime_client_id'
MAPPER_NAME = 'cleaning-identity-runtime-audience'


def validate_defaults(api, realm):
    """Do not create an enabled service account into unsafe default role grants."""
    inventory = api('GET', '/clients?first=0&max=1000')
    if not isinstance(inventory, list) or len(inventory) >= 1000 or any(not isinstance(c, dict) for c in inventory):
        raise RuntimeError('Client inventory incomplete before runtime creation')
    accounts = [c for c in inventory if c.get('clientId') == 'account']
    if len(accounts) > 1:
        raise RuntimeError('Ambiguous account client before runtime creation')
    account_id = accounts[0].get('id') if accounts else None
    name = 'default-roles-' + realm['realm']
    root = api('GET', '/roles/' + quote(name, safe=''), missing=True)
    if not isinstance(root, dict):
        raise RuntimeError('Realm default role could not be verified')
    pending, visited = [root], set()
    while pending:
        role = pending.pop()
        if not isinstance(role, dict) or not isinstance(role.get('id'), str) or not role['id']:
            raise RuntimeError('Malformed default role graph')
        identity = role['id']
        if identity in visited:
            continue
        visited.add(identity)
        if len(visited) > 1000:
            raise RuntimeError('Default role graph incomplete; manual review required')
        if role.get('clientRole'):
            if role.get('containerId') != account_id or role.get('name') not in {'manage-account', 'manage-account-links', 'view-profile'}:
                raise RuntimeError('Unsafe realm default client privileges; runtime was not created')
        elif role.get('name') not in {name, 'offline_access', 'uma_authorization'}:
            raise RuntimeError('Unexpected realm default roles; runtime was not created')
        if role.get('composite'):
            children = api('GET', '/roles-by-id/' + quote(identity, safe='') + '/composites')
            if not isinstance(children, list):
                raise RuntimeError('Malformed default role graph')
            pending.extend(children)


def configure(api, change, realm, client, subject, apply=False, migrate_fgap=False):
    """Validate completely, then emit ordered changes; activate the extension last.

    api(method, relative_path, body=None, missing=False); change(label,method,path,body).
    Only the caller's explicit migrate_fgap opt-in allows removal of the exact
    previously reviewed FGAP grants. Unknown privileges/policies always fail.
    """
    def items(path):
        value = api('GET', path)
        if not isinstance(value, list) or any(not isinstance(entry, dict) for entry in value):
            raise RuntimeError('Malformed identity setup inventory')
        return value

    if not isinstance(realm, dict) or not isinstance(client, dict) or not subject:
        raise RuntimeError('Malformed identity extension setup input')
    cid = client.get('id')
    try:
        if str(UUID(cid)) != cid:
            raise ValueError()
    except (ValueError, TypeError, AttributeError):
        raise RuntimeError('Runtime client identity must be a canonical UUID') from None
    attrs = realm.get('attributes') or {}
    if not isinstance(attrs, dict):
        raise RuntimeError('Malformed realm attributes')
    bound = attrs.get(CLIENT_ATTRIBUTE)
    if bound is not None and bound != cid:
        raise RuntimeError('Extension runtime client binding drift; manual review required')
    user_path = '/users/' + quote(subject, safe='')
    if items(user_path + '/groups?first=0&max=1'):
        raise RuntimeError('Runtime service account has group-derived privileges; review manually')
    allowed_realm = {'default-roles-' + realm['realm'], 'offline_access', 'uma_authorization'}
    if any(r.get('name') not in allowed_realm for r in items(user_path + '/role-mappings/realm/composite')):
        raise RuntimeError('Runtime has unexpected effective realm/composite roles; review manually')
    clients = items('/clients?first=0&max=1000')
    if len(clients) >= 1000:
        raise RuntimeError('Client inventory incomplete; manual review required')
    management = [c for c in clients if c.get('clientId') == 'realm-management']
    if len(management) != 1:
        raise RuntimeError('Realm management client missing or ambiguous')
    mappings = user_path + '/role-mappings/clients/' + quote(management[0]['id'], safe='')
    direct = items(mappings)
    if any(r.get('name') not in RUNTIME_ROLES for r in direct):
        raise RuntimeError('Runtime has unexpected administration roles; no silent revocation')
    direct_names = {r['name'] for r in direct}
    for entry in clients:
        effective = items(user_path + '/role-mappings/clients/' + quote(entry['id'], safe='') + '/composite')
        names = {r.get('name') for r in effective}
        if entry.get('clientId') == 'realm-management':
            # A known role inherited from a composite is not a known direct grant.
            if names != direct_names:
                raise RuntimeError('Runtime has inherited effective administration privileges; review manually')
        elif names - ({'manage-account', 'manage-account-links', 'view-profile'} if entry.get('clientId') == 'account' else set()):
            raise RuntimeError('Runtime has unexpected effective client/composite privileges; review manually')

    # Inspect even disabled FGAP state: dormant grants must not survive activation.
    authz_clients = items('/clients?clientId=admin-permissions')
    if len(authz_clients) > 1:
        raise RuntimeError('Ambiguous authorization client')
    # This read-only pass proves exact legacy policy bindings, fixed root groups,
    # flat roles and complete member privilege inventories. Its proposed grants
    # are intentionally discarded: this setup never grants FGAP permissions.
    inspection_realm = dict(realm, adminPermissionsEnabled=bool(authz_clients))
    groups = validate_legacy_contract(api, lambda *args: None, inspection_realm, subject, apply=False)
    authz = '/clients/' + quote(authz_clients[0]['id'], safe='') + '/authz/resource-server' if authz_clients else None
    policies = items(authz + '/policy?first=0&max=1000') if authz else []
    permissions = items(authz + '/permission?first=0&max=1000') if authz else []
    legacy = {p['id']: p for p in policies + permissions}
    if (direct or legacy or realm.get('adminPermissionsEnabled')) and not migrate_fgap:
        raise RuntimeError('Legacy FGAP access requires explicit --migrate-fgap development review')

    client_path = '/clients/' + quote(cid, safe='')
    mappers = items(client_path + '/protocol-mappers/models')
    named = [m for m in mappers if m.get('name') == MAPPER_NAME]
    desired_mapper = {'name': MAPPER_NAME, 'protocol': 'openid-connect', 'protocolMapper': 'oidc-audience-mapper',
                      'consentRequired': False, 'config': {'included.custom.audience': AUDIENCE,
                                                          'access.token.claim': 'true', 'id.token.claim': 'false',
                                                          'introspection.token.claim': 'true', 'userinfo.token.claim': 'false'}}
    if len(named) > 1:
        raise RuntimeError('Ambiguous identity audience mapper')
    if named:
        config = named[0].get('config')
        if not isinstance(config, dict):
            raise RuntimeError('Identity audience mapper drift; manual review required')
        # Native 26.8.1 materializes these two protocol defaults on POST/GET.
        # Normalize only their verified values; unknown keys or security-claim
        # changes still fail instead of being silently overwritten.
        normalized = dict({'introspection.token.claim': 'true', 'userinfo.token.claim': 'false'}, **config)
        if (any(named[0].get(k) != desired_mapper[k] for k in ('protocol', 'protocolMapper'))
                or normalized != desired_mapper['config'] or named[0].get('consentRequired', False) is not False):
            raise RuntimeError('Identity audience mapper drift; manual review required')
    if any(m.get('name') != MAPPER_NAME and m.get('config', {}).get('included.custom.audience') == AUDIENCE for m in mappers):
        raise RuntimeError('Unexpected identity audience mapper; manual review required')

    # All validation precedes every deletion or activation. Generic PolicyResource
    # delete is the same native endpoint whose relationship reads validate FGAP.
    # https://github.com/keycloak/keycloak/blob/26.8.1/integration/admin-client-core/src/main/java/org/keycloak/admin/client/resource/PolicyResource.java
    for entry in sorted(legacy.values(), key=lambda p: p['name'] == 'runtime-subject'):
        change('remove reviewed legacy FGAP policy ' + entry['name'], 'DELETE', authz + '/policy/' + quote(entry['id'], safe=''), None)
    if direct:
        change('remove reviewed direct Admin REST grants', 'DELETE', mappings, direct)
    if apply:
        if items(mappings) or items(mappings + '/composite'):
            raise RuntimeError('Admin REST grants remain; extension was not activated')
        if authz and (items(authz + '/policy?first=0&max=1000') or items(authz + '/permission?first=0&max=1000')):
            raise RuntimeError('FGAP policies remain; extension was not activated')
    if realm.get('adminPermissionsEnabled'):
        change('disable reviewed development FGAP path', 'PUT', '', {'adminPermissionsEnabled': False})
        if apply and api('GET', '').get('adminPermissionsEnabled'):
            raise RuntimeError('FGAP path remains enabled; extension was not activated')
    for name, gid in list(groups.items()):
        if gid is None:
            change('missing fixed group ' + name, 'POST', '/groups', {'name': name})
            if apply:
                found = items('/groups?search=' + name + '&exact=true&first=0&max=2')
                if len(found) != 1:
                    raise RuntimeError('Created fixed group could not be resolved')
                groups[name] = found[0]['id']
        role = GROUPS[name]
        if role:
            path = '/groups/' + quote(groups[name] or 'missing', safe='') + '/role-mappings/realm'
            existing = items(path) if groups[name] else []
            if not existing:
                representation = api('GET', '/roles/' + role, missing=True)
                if representation:
                    change('missing fixed role mapping ' + name, 'POST', path, [representation])
    if not named:
        change('missing dedicated identity audience', 'POST', client_path + '/protocol-mappers/models', desired_mapper)
    if client.get('fullScopeAllowed') is not False:
        change('restrict runtime token role scope', 'PUT', client_path, {'fullScopeAllowed': False})
    if bound is None:
        change('bind extension to exact runtime client', 'PUT', '', {'attributes': dict(attrs, **{CLIENT_ATTRIBUTE: cid})})
    return groups
