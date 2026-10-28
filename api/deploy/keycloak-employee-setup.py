#!/usr/bin/env python3
"""Explicit tenant identity setup. Read-only by default; never prints secrets."""
import argparse
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from keycloak_identity_extension import configure, validate_defaults, validate_tenant_admins
import json
import os
import re
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit, urlencode
from urllib.request import Request, urlopen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', required=True)
    parser.add_argument('--tenant', required=True)
    parser.add_argument('--public-url', required=True)
    parser.add_argument('--redirect-uri', action='append', required=True, help='Expected account landing URI; repeat for each allowed frontend')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--development', action='store_true', help='Explicitly authorize development-only pinned identity extension setup')
    parser.add_argument('--migrate-fgap', action='store_true', help='Remove only reviewed legacy FGAP access in an isolated development fixture')
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
    queued = []

    def api(method, path='', body=None, missing=False):
        target = args.server.rstrip('/') + '/admin/serverinfo' if path == '@serverinfo' else base + path
        req = Request(target, method=method,
                      data=None if body is None else json.dumps(body).encode(),
                      headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'})
        try:
            with urlopen(req, timeout=30) as response:
                raw = response.read()
                result = json.loads(raw) if raw else None
                if method == 'GET' and not isinstance(result, (dict, list)):
                    raise RuntimeError('Malformed Keycloak setup response')
                return result
        except HTTPError as error:
            if missing and error.code == 404:
                return None
            raise RuntimeError('Keycloak setup request failed with HTTP ' + str(error.code)) from None

    def change(label, method, path, body):
        changes.append(label)
        print(('APPLY ' if args.apply else 'NEEDS ') + label)
        if args.apply:
            queued.append((method, path, body))

    client_id = os.environ.get('KEYCLOAK_TENANT_RUNTIME_CLIENT_ID', 'employee-runtime')
    if not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', client_id):
        raise RuntimeError('Invalid explicit runtime client name')
    secret = os.environ.get('KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET')
    clients = api('GET', '/clients?clientId=' + quote(client_id, safe=''))
    if len(clients) > 1:
        raise RuntimeError('Ambiguous runtime client')

    def verify_runtime_credentials():
        if not secret:
            raise RuntimeError('Supply the protected runtime client secret to verify delivered credentials')
        # Never rotate an existing secret to make verification pass. No admin bearer
        # is sent to the realm-scoped client-credentials endpoint.
        req = Request(args.server.rstrip('/') + '/realms/' + quote(args.tenant, safe='') + '/protocol/openid-connect/token',
            data=urlencode({'grant_type':'client_credentials','client_id':client_id,'client_secret':secret}).encode(),
            headers={'Content-Type':'application/x-www-form-urlencoded'}, method='POST')
        try:
            with urlopen(req, timeout=10) as response:
                raw=response.read(65537)
                if len(raw)>65536:
                    raise RuntimeError('Runtime credential verification failed')
                payload=json.loads(raw)
                if not isinstance(payload,dict) or not isinstance(payload.get('access_token'),str) or not payload['access_token'].strip():
                    raise RuntimeError('Runtime credential verification failed')
        except HTTPError:
            raise RuntimeError('Runtime credential verification failed; delivered secret must match existing account') from None
        return payload['access_token']

    def verify_extension():
        # Fetch a fresh token after setup so the new audience mapper is included.
        runtime_token = verify_runtime_credentials()
        req = Request(args.server.rstrip('/') + '/realms/' + quote(args.tenant, safe='') + '/cleaning-identity/v1',
                      headers={'Authorization': 'Bearer ' + runtime_token}, method='GET')
        try:
            with urlopen(req, timeout=10) as response:
                raw = response.read(65537)
                if len(raw) > 65536:
                    raise RuntimeError('Identity extension readiness verification failed')
                result = json.loads(raw)
                if not isinstance(result, dict) or result.get('realm') != args.tenant or result.get('enabled') is not True:
                    raise RuntimeError('Identity extension readiness verification failed')
        except HTTPError:
            raise RuntimeError('Identity extension is missing or denied; setup is not ready') from None

    if clients:
        verify_runtime_credentials()  # Fail before any --apply mutation for an existing account.

    if (args.apply or args.migrate_fgap) and not args.development:
        raise RuntimeError('Identity extension apply/migration is development-only; supply --development for an isolated fixture')
    info = api('GET', '@serverinfo')
    if info.get('systemInfo', {}).get('version') != '26.8.1':
        raise RuntimeError('Development identity extension requires pinned Keycloak 26.8.1')
    realm = api('GET')
    if not realm.get('enabled'):
        raise RuntimeError('Realm disabled; enablement requires a separate explicit operation')
    runtime_user = api('GET', '/clients/' + quote(clients[0]['id'], safe='') + '/service-account-user') if clients else None
    validate_tenant_admins(api, realm, runtime_user.get('id') if runtime_user else None, migrate_fgap=args.migrate_fgap)
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
        realm['attributes'] = attributes
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
    profile_changed = False
    for name, display in (('cleaning_employee_id', 'Employee ownership'), ('cleaning_invitation_ticket', 'Invitation ticket')):
        matches = [a for a in attributes if a.get('name') == name]
        if len(matches) > 1:
            raise RuntimeError('Ambiguous server-owned profile attribute; manual review required')
        desired = {'name': name, 'displayName': display, 'multivalued': False,
                   'permissions': {'view': ['admin'], 'edit': ['admin']}}
        if not matches or any(matches[0].get(k) != desired[k] for k in ('multivalued', 'permissions')):
            if matches:
                matches[0].update(desired)
            else:
                attributes.append(desired)
            profile_changed = True
    if profile_changed:
        change('administrator-only ownership and invitation ticket profile', 'PUT', '/users/profile', profile)
    if not clients:
        from keycloak_fgap import configure as validate_legacy_contract
        validate_legacy_contract(api, lambda *a: None, realm, 'missing-runtime-subject', apply=False)
        validate_defaults(api, realm)
        if args.apply and not secret:
            raise RuntimeError('Set KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET through protected environment')
        desired = {'clientId':client_id, 'enabled':True, 'publicClient':False,
                   'serviceAccountsEnabled':True, 'standardFlowEnabled':False,
                   'directAccessGrantsEnabled':False, 'fullScopeAllowed':False}
        if args.apply: desired['secret'] = secret
        change('missing isolated runtime service account', 'POST', '/clients', desired)
        if args.apply:
            for method, path, body in queued: api(method, path, body)
            queued.clear()
        clients = api('GET', '/clients?clientId=' + quote(client_id, safe='')) if args.apply else []
        if clients:
            verify_runtime_credentials()
    if clients:
        client = clients[0]
        if (not client.get('enabled') or client.get('publicClient') or not client.get('serviceAccountsEnabled')
                or client.get('standardFlowEnabled') or client.get('directAccessGrantsEnabled')):
            raise RuntimeError('Existing runtime client configuration conflicts; review manually')
        user = api('GET', '/clients/' + quote(client['id'], safe='') + '/service-account-user')
        # Full validation precedes queued prerequisites or extension activation.
        configure(api, lambda *a: None, realm, client, user['id'], apply=False, migrate_fgap=args.migrate_fgap)
        if args.apply:
            for method, path, body in queued: api(method, path, body)
            queued.clear()
        def extension_change(label, method, path, body):
            changes.append(label)
            print(('APPLY ' if args.apply else 'NEEDS ') + label)
            if args.apply: api(method, path, body)
        configure(api, extension_change, realm, client, user['id'], apply=args.apply, migrate_fgap=args.migrate_fgap)
        if args.apply or not changes:
            verify_extension()
    print('CHECK complete; validate exact-version real-service denials before rollout')
    return 2 if changes and not args.apply else 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, OSError):
        # Do not print exception bodies: SMTP JSON/environment may contain credentials.
        print('Identity setup failed; check explicit targets/configuration and server availability')
        raise SystemExit(1)
