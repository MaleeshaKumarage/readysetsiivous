#!/usr/bin/env python3
"""Explicit tenant identity setup. Read-only by default; never prints secrets."""
import argparse
import json
import os
import re
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import Request, urlopen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', required=True)
    parser.add_argument('--tenant', required=True)
    parser.add_argument('--public-url', required=True)
    parser.add_argument('--redirect-uri', action='append', required=True, help='Expected account landing URI; repeat for each allowed frontend')
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', args.tenant) or args.tenant == 'master':
        parser.error('Specify one canonical tenant realm, never master')
    for value in [args.server, args.public_url]:
        parsed = urlsplit(value)
        if parsed.scheme not in ('http', 'https') or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
            parser.error('Provide an explicit HTTP(S) base URL without credentials/query/fragment')
    for redirect in args.redirect_uri:
        parsed = urlsplit(redirect)
        if parsed.scheme not in ('http', 'https') or not parsed.hostname or parsed.username or parsed.password or parsed.fragment:
            parser.error('Expected account redirect must be an HTTP(S) URI without credentials/fragment')
    token = os.environ.get('KEYCLOAK_MIGRATION_ADMIN_TOKEN')
    if not token:
        parser.error('Set KEYCLOAK_MIGRATION_ADMIN_TOKEN via protected environment')
    base = args.server.rstrip('/') + '/admin/realms/' + quote(args.tenant, safe='')
    changes = []

    def api(method, path='', body=None, missing=False):
        req = Request(base + path, method=method,
                      data=None if body is None else json.dumps(body).encode(),
                      headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'})
        try:
            with urlopen(req, timeout=30) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except HTTPError as error:
            if missing and error.code == 404:
                return None
            raise RuntimeError('Keycloak setup request failed with HTTP ' + str(error.code)) from None

    def change(label, method, path, body):
        changes.append(label)
        print(('APPLY ' if args.apply else 'NEEDS ') + label)
        if args.apply:
            api(method, path, body)

    realm = api('GET')
    if not realm.get('enabled'):
        raise RuntimeError('Realm disabled; enablement requires a separate explicit operation')
    web_clients = api('GET', '/clients?clientId=cleaning-suite-web')
    if len(web_clients) != 1 or web_clients[0].get('clientId') != 'cleaning-suite-web' or not web_clients[0].get('enabled') or web_clients[0].get('publicClient') is not True or web_clients[0].get('standardFlowEnabled') is not True:
        raise RuntimeError('The enabled cleaning-suite-web client must exist before identity setup')
    registered = web_clients[0].get('redirectUris', [])
    for actual in args.redirect_uri:
        if not any(isinstance(uri, str) and (uri == actual or uri.endswith('/*') and actual.startswith(uri[:-1])) for uri in registered):
            raise RuntimeError('The expected account landing URI is not registered on the enabled web client')
    attributes = dict(realm.get('attributes') or {})
    public = args.public_url.rstrip('/')
    if attributes.get('frontendUrl', '').rstrip('/') != public:
        attributes['frontendUrl'] = public
        change('realm frontend URL', 'PUT', '', {'attributes': attributes})
    smtp = realm.get('smtpServer') or {}
    if not smtp.get('host') or not smtp.get('from'):
        # SMTP is explicitly supplied as JSON in the protected environment, never in arguments.
        desired = json.loads(os.environ.get('KEYCLOAK_TENANT_SMTP_JSON', '{}'))
        if not desired.get('host') or not desired.get('from'):
            raise RuntimeError('Supply KEYCLOAK_TENANT_SMTP_JSON with host/from before setup')
        change('missing realm SMTP', 'PUT', '', {'smtpServer': desired})
    for role in ('admin', 'manager', 'employee'):
        if api('GET', '/roles/' + role, missing=True) is None:
            change('missing application role ' + role, 'POST', '/roles', {'name': role})
    profile = api('GET', '/users/profile')
    attributes = profile.setdefault('attributes', [])
    owners = [a for a in attributes if a.get('name') == 'cleaning_employee_id']
    if len(owners) > 1:
        raise RuntimeError('Ambiguous ownership profile; manual review required')
    desired = {'name': 'cleaning_employee_id', 'displayName': 'Employee ownership', 'multivalued': False,
               'permissions': {'view': ['admin'], 'edit': ['admin']}}
    if not owners or any(owners[0].get(k) != desired[k] for k in ('multivalued', 'permissions')):
        if owners:
            owners[0].update(desired)
        else:
            attributes.append(desired)
        change('administrator-only ownership profile', 'PUT', '/users/profile', profile)
    client_id = os.environ.get('KEYCLOAK_TENANT_RUNTIME_CLIENT_ID', 'employee-runtime')
    if not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', client_id):
        raise RuntimeError('Invalid explicit runtime client name')
    clients = api('GET', '/clients?clientId=' + quote(client_id, safe=''))
    if len(clients) > 1:
        raise RuntimeError('Ambiguous runtime client')
    if not clients:
        secret = os.environ.get('KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET')
        if args.apply and not secret:
            raise RuntimeError('Set KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET through protected environment')
        desired = {'clientId':client_id, 'enabled':True, 'publicClient':False,
                   'serviceAccountsEnabled':True, 'standardFlowEnabled':False,
                   'directAccessGrantsEnabled':False, 'fullScopeAllowed':True}
        if args.apply: desired['secret'] = secret
        change('missing scoped runtime service account', 'POST', '/clients', desired)
        clients = api('GET', '/clients?clientId=' + quote(client_id, safe='')) if args.apply else []
    if clients:
        client = clients[0]
        if (not client.get('enabled') or client.get('publicClient') or not client.get('serviceAccountsEnabled')
                or client.get('standardFlowEnabled') or client.get('directAccessGrantsEnabled')):
            raise RuntimeError('Existing runtime client configuration conflicts; review manually')
        user = api('GET', '/clients/' + quote(client['id'], safe='') + '/service-account-user')
        management = api('GET', '/clients?clientId=realm-management')
        if len(management) != 1:
            raise RuntimeError('Realm management client missing or ambiguous')
        management_id = quote(management[0]['id'], safe='')
        mappings = '/users/' + quote(user['id'], safe='') + '/role-mappings/clients/' + management_id
        existing = api('GET', mappings)
        allowed = {'manage-users', 'query-users', 'view-users', 'view-realm'}
        if any(role.get('name') not in allowed for role in existing):
            raise RuntimeError('Runtime has unexpected administration roles; review manually, no silent revocation')
        user_base = '/users/' + quote(user['id'], safe='')
        if api('GET', user_base + '/groups?first=0&max=1'):
            raise RuntimeError('Runtime service account has group-derived privileges; review manually')
        realm_roles = api('GET', user_base + '/role-mappings/realm/composite')
        allowed_realm = {'default-roles-' + args.tenant, 'offline_access', 'uma_authorization'}
        if any(role.get('name') not in allowed_realm for role in realm_roles):
            raise RuntimeError('Runtime has unexpected effective realm/composite roles; review manually')
        # Inspect every realm client, including roles inherited through realm composites.
        # Direct role mappings alone cannot establish least privilege.
        all_clients = api('GET', '/clients?first=0&max=1000')
        if len(all_clients) >= 1000:
            raise RuntimeError('Realm client inventory exceeds the bounded complete privilege check; review manually')
        for entry in all_clients:
            effective = api('GET', user_base + '/role-mappings/clients/' + quote(entry['id'], safe='') + '/composite')
            permitted = allowed if entry.get('clientId') == 'realm-management' else (
                {'manage-account', 'manage-account-links', 'view-profile'} if entry.get('clientId') == 'account' else set())
            if any(role.get('name') not in permitted for role in effective):
                raise RuntimeError('Runtime has unexpected effective client/composite privileges; review manually')
        names = {role['name'] for role in existing}
        missing = [api('GET', '/clients/' + management_id + '/roles/' + name) for name in sorted(allowed - names)]
        if missing:
            change('missing scoped identity grants', 'POST', mappings, missing)
    print('CHECK complete; validate exact-version real-service denials before rollout')
    return 2 if changes and not args.apply else 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, OSError):
        # Do not print exception bodies: SMTP JSON/environment may contain credentials.
        print('Identity setup failed; check explicit targets/configuration and server availability')
        raise SystemExit(1)
