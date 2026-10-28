#!/usr/bin/env python3
"""Explicit maintenance-only production enrollment; check by default, no email or passwords."""
import argparse
import copy
import json
import os
import re
import subprocess
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
from uuid import UUID

from keycloak_identity_extension import configure, validate_defaults, validate_tenant_admins, CLIENT_ATTRIBUTE, AUDIENCE
from keycloak_fgap import configure as inspect_groups, GROUPS

VERSION = '26.8.1'
PUBLIC_ORIGIN = 'https://auth.readysetsiivous.fi'
OWNER = 'cleaning_employee_id'
APP_ROLES = {'admin', 'manager', 'employee'}


def fail(message):
    raise RuntimeError(message)


def uid(value):
    try:
        if not isinstance(value, str) or str(UUID(value)) != value or UUID(value).int == 0:
            raise ValueError()
    except (ValueError, TypeError, AttributeError):
        fail('A migration identity is not a canonical nonzero UUID')
    return value


def objects(value):
    if not isinstance(value, list) or any(not isinstance(item, dict) for item in value):
        fail('The identity inventory is malformed')
    return value


def fields(value, allowed, required=None):
    if not isinstance(value, dict) or set(value) - set(allowed) or set(required or allowed) - set(value):
        fail('Migration plan fields do not match the reviewed schema')


def validate_plan(plan):
    fields(plan, {'schema_version', 'tenant', 'expected_issuer', 'expected_version', 'runtime_client_id', 'maintenance', 'employees', 'tenant_admin_owners'})
    if (type(plan['schema_version']) is not int or plan['schema_version'] != 1 or plan['expected_version'] != VERSION
            or not isinstance(plan['tenant'], str) or not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', plan['tenant'])
            or plan['tenant'] == 'master' or plan['expected_issuer'] != PUBLIC_ORIGIN + '/realms/' + plan['tenant']):
        fail('Migration target must match the reviewed production issuer and version')
    if not isinstance(plan['runtime_client_id'], str) or not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', plan['runtime_client_id']):
        fail('Invalid runtime client name')
    fields(plan['maintenance'], {'api_container_name', 'api_container_id', 'compose_project'})
    maintenance = plan['maintenance']
    if (not isinstance(maintenance['api_container_name'], str) or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}', maintenance['api_container_name'])
            or not isinstance(maintenance['api_container_id'], str) or not re.fullmatch(r'[0-9a-f]{64}', maintenance['api_container_id'])
            or not isinstance(maintenance['compose_project'], str) or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}', maintenance['compose_project'])):
        fail('Maintenance target must identify the exact reviewed API container')
    if not isinstance(plan['employees'], list) or not isinstance(plan['tenant_admin_owners'], list):
        fail('Migration entries must be explicit lists')
    seen_users, seen_employees, seen_emails = set(), set(), set()
    entries = []
    for ownerless, source in ((False, plan['employees']), (True, plan['tenant_admin_owners'])):
        for raw in source:
            required = {'user_id', 'email', 'expected_enabled', 'approve_ownerless_admin'} if ownerless else {
                'user_id', 'employee_id', 'email', 'expected_enabled', 'enabled', 'role', 'allow_missing_owner'}
            fields(raw, required)
            entry = dict(raw, ownerless=ownerless)
            user = uid(entry['user_id'])
            email = entry['email']
            if (not isinstance(email, str) or email != email.strip() or len(email) > 254
                    or not re.fullmatch(r'[^\s@<>\x00-\x1f]+@[^\s@<>\x00-\x1f]+\.[^\s@<>\x00-\x1f]+', email)):
                fail('A migration email is malformed')
            if user in seen_users or email.casefold() in seen_emails:
                fail('Duplicate migration user or email identity')
            seen_users.add(user); seen_emails.add(email.casefold())
            if type(entry['expected_enabled']) is not bool:
                fail('Expected account state must be explicit')
            if ownerless:
                if entry['approve_ownerless_admin'] is not True:
                    fail('Ownerless tenant administrator enrollment requires explicit plan approval')
                entry.update(role='admin', enabled=entry['expected_enabled'], employee_id=None, allow_missing_owner=False)
            else:
                employee = uid(entry['employee_id'])
                if employee in seen_employees:
                    fail('Duplicate migration employee identity')
                seen_employees.add(employee)
                if (not isinstance(entry['role'], str) or entry['role'] not in APP_ROLES or type(entry['enabled']) is not bool
                        or type(entry['allow_missing_owner']) is not bool):
                    fail('Employee role, target status and ownership approval must be explicit')
            entries.append(entry)
    if not entries or len(entries) > 10000:
        fail('Migration plan must have a bounded explicit identity inventory')
    return entries


class Maintenance:
    """Run only on the production host; never shells/interpolates plan strings."""
    def __init__(self, expected):
        self.expected = expected

    def __call__(self):
        state = subprocess.run(['systemctl', 'show', '--property=ActiveState', '--property=LoadState', 'cloudflared.service'],
                               capture_output=True, text=True, timeout=15, check=False)
        details = dict(line.split('=', 1) for line in state.stdout.splitlines() if '=' in line)
        if state.returncode != 0 or details.get('ActiveState') != 'inactive' or details.get('LoadState') != 'loaded':
            fail('Production ingress must be inactive throughout identity maintenance')
        representation = '{"Id":{{json .Id}},"State":{{json .State}},"Labels":{{json .Config.Labels}}}'
        inspected = subprocess.run(['docker', 'inspect', '--format', representation, self.expected['api_container_name']],
                                   capture_output=True, text=True, timeout=15, check=False)
        try:
            container = json.loads(inspected.stdout)
            labels = container['Labels']
            correct = (container['Id'] == self.expected['api_container_id'] and container['State']['Running'] is False
                       and container['State'].get('Restarting') is False and container['State'].get('Status') in {'exited', 'created'}
                       and labels['com.docker.compose.service'] == 'api'
                       and labels['com.docker.compose.project'] == self.expected['compose_project'])
        except (ValueError, TypeError, IndexError, KeyError):
            correct = False
        running = subprocess.run(['docker', 'ps', '--filter', 'label=com.docker.compose.service=api',
                                  '--filter', 'label=com.docker.compose.project=' + self.expected['compose_project'], '--format', '{{.ID}}'],
                                 capture_output=True, text=True, timeout=15, check=False)
        if inspected.returncode != 0 or not correct or running.returncode != 0 or running.stdout.strip():
            fail('The exact reviewed API and all same-project API replicas must be stopped')


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class Transport:
    def __init__(self, server, tenant, admin_token, runtime_client, runtime_secret):
        endpoint = urlsplit(server)
        if (endpoint.scheme != 'http' or endpoint.hostname not in {'127.0.0.1', '::1', 'localhost'} or endpoint.port is None
                or endpoint.username or endpoint.password or endpoint.path not in ('', '/') or endpoint.query or endpoint.fragment
                or any(c.isspace() or ord(c) < 32 for c in server) or '\\' in server):
            fail('Use an explicit literal-loopback HTTP administration port on the maintenance host')
        if not admin_token or not runtime_secret or any(ord(c) < 32 for c in admin_token + runtime_secret):
            fail('Protected administration and runtime credentials are required')
        self.server, self.tenant = server.rstrip('/'), tenant
        self.admin_token, self.runtime_client, self.runtime_secret = admin_token, runtime_client, runtime_secret
        self.opener = build_opener(NoRedirect())

    def http(self, method, path, body=None, token=None, form=False, allowed=(200, 201, 204)):
        request = Request(self.server + path, method=method,
                          headers={'Accept': 'application/json', **({'Authorization': 'Bearer ' + token} if token else {}),
                                   **({'Content-Type': 'application/x-www-form-urlencoded' if form else 'application/json'} if body is not None else {})},
                          data=None if body is None else (urlencode(body) if form else json.dumps(body)).encode())
        try:
            response = self.opener.open(request, timeout=30)
        except HTTPError as error:
            response = error
        with response:
            if response.status not in allowed:
                fail('Identity maintenance request failed with HTTP ' + str(response.status))
            raw = response.read(1048577)
            if len(raw) > 1048576:
                fail('Identity maintenance response exceeds its verified bound')
            value = json.loads(raw) if raw and response.status not in (403, 404) else None
            return response.status, value

    def api(self, method, path='', body=None, missing=False):
        target = '/admin/serverinfo' if path == '@serverinfo' else '/admin/realms/' + quote(self.tenant, safe='') + path
        status, value = self.http(method, target, body, token=self.admin_token,
                                  allowed=(200, 201, 204, 404) if missing else (200, 201, 204))
        return None if status == 404 else value

    def discovery(self, issuer):
        _, metadata = self.http('GET', '/realms/' + self.tenant + '/.well-known/openid-configuration')
        if (not isinstance(metadata, dict) or metadata.get('issuer') != issuer
                or metadata.get('token_endpoint') != issuer + '/protocol/openid-connect/token'
                or metadata.get('jwks_uri') != issuer + '/protocol/openid-connect/certs'):
            fail('Private discovery must prove the fixed public HTTPS issuer and endpoints')

    def runtime_token(self):
        _, response = self.http('POST', '/realms/' + self.tenant + '/protocol/openid-connect/token',
                                {'grant_type': 'client_credentials', 'client_id': self.runtime_client, 'client_secret': self.runtime_secret}, form=True)
        if not isinstance(response, dict) or not isinstance(response.get('access_token'), str) or not response['access_token']:
            fail('Runtime credential verification failed; existing credentials are never rotated')
        return response['access_token']

    def verify(self):
        token = self.runtime_token()
        _, value = self.http('GET', '/realms/' + self.tenant + '/cleaning-identity/v1', token=token)
        if not isinstance(value, dict) or value.get('realm') != self.tenant or value.get('enabled') is not True:
            fail('The isolated identity extension is not ready')
        self.http('GET', '/admin/realms/' + self.tenant + '/users?first=0&max=1', token=token, allowed=(403,))
        self.http('GET', '/admin/realms/' + self.tenant, token=token, allowed=(403,))
        self.http('GET', '/admin/realms/master', token=token, allowed=(403,))
        clients = objects(self.api('GET', '/clients?clientId=' + quote(self.runtime_client, safe='')))
        if len(clients) != 1:
            fail('The exact runtime client became ambiguous')
        self.http('GET', '/admin/realms/' + self.tenant + '/clients/' + uid(clients[0].get('id')) + '/client-secret', token=token, allowed=(403,))


def credentials(api, user):
    values = objects(api('GET', '/users/' + user + '/credentials'))
    if (any(not isinstance(c.get('id'), str) or not c['id'] or not isinstance(c.get('type'), str) or not c['type'] for c in values)
            or len({c['id'] for c in values}) != len(values)):
        fail('Credential preservation metadata is malformed')
    return sorted((c['id'], c['type'], c.get('createdDate'), c.get('userLabel')) for c in values)


def validate_operator_boundary(api):
    """Reject dormant privileged carriers and service accounts before activation."""
    clients = objects(api('GET', '/clients?first=0&max=1000'))
    if len(clients) >= 1000:
        fail('Complete client inventory exceeds the reviewed bound')
    management = [c for c in clients if c.get('clientId') == 'realm-management']
    if len(management) != 1:
        fail('Realm administration client is missing or ambiguous')
    management_id = uid(management[0].get('id'))
    candidates = objects(api('GET', '/roles?first=0&max=10001'))
    if len(candidates) > 10000:
        fail('Complete role inventory exceeds the reviewed bound')
    for client in clients:
        client_id = uid(client.get('id'))
        if client.get('serviceAccountsEnabled'):
            subject = api('GET', '/clients/' + client_id + '/service-account-user')
            identity = uid(subject.get('id'))
            if objects(api('GET', '/users/' + identity + '/role-mappings/clients/' + management_id + '/composite')):
                fail('A tenant service account has administration privileges')
        if client_id != management_id:
            roles = objects(api('GET', '/clients/' + client_id + '/roles?first=0&max=10001'))
            if len(roles) > 10000:
                fail('Complete client role inventory exceeds the reviewed bound')
            candidates.extend(roles)
            if len(candidates) > 50000:
                fail('Complete role graph exceeds the reviewed bound')
    for role in candidates:
        if role.get('composite'):
            children = objects(api('GET', '/roles-by-id/' + uid(role.get('id')) + '/composites'))
            if len(children) > 10000 or any(r.get('clientRole') and r.get('containerId') == management_id for r in children):
                fail('A tenant role has a dormant administration grant')
    pending = objects(api('GET', '/groups?first=0&max=10001&briefRepresentation=false'))
    seen = set()
    while pending:
        group = pending.pop()
        identity = uid(group.get('id'))
        if identity in seen or len(seen) >= 10000:
            fail('Complete group inventory is repeated or exceeds the reviewed bound')
        seen.add(identity)
        if objects(api('GET', '/groups/' + identity + '/role-mappings/clients/' + management_id + '/composite')):
            fail('A tenant group has a dormant administration grant')
        children = objects(api('GET', '/groups/' + identity + '/children?first=0&max=10001'))
        if len(pending) + len(seen) + len(children) > 10000:
            fail('Complete group inventory exceeds the reviewed bound')
        pending.extend(children)


def preflight(api, plan, entries):
    realm = api('GET', '')
    if (not isinstance(realm, dict) or realm.get('realm') != plan['tenant'] or realm.get('enabled') is not True
            or realm.get('adminPermissionsEnabled') or not isinstance(realm.get('attributes', {}), dict)
            or realm.get('attributes', {}).get('frontendUrl', '').rstrip('/') != PUBLIC_ORIGIN):
        fail('The enabled realm, fixed frontend origin and disabled FGAP path are required')
    info = api('GET', '@serverinfo')
    if info.get('systemInfo', {}).get('version') != VERSION:
        fail('The reviewed Keycloak version is required')
    validate_tenant_admins(api, realm)
    validate_operator_boundary(api)
    validate_defaults(api, realm)
    inspect_groups(api, lambda *args: None, realm, 'missing-runtime-subject', apply=False)
    roles = {}
    for name in sorted(APP_ROLES):
        role = api('GET', '/roles/' + name)
        if not isinstance(role, dict) or role.get('name') != name or role.get('composite') or role.get('clientRole'):
            fail('All fixed application roles must exist and be flat realm roles')
        uid(role.get('id')); roles[name] = role
    profile = api('GET', '/users/profile')
    if (not isinstance(profile, dict) or not isinstance(profile.get('attributes'), list)
            or any(not isinstance(a, dict) for a in profile['attributes'])):
        fail('Native user-profile configuration is malformed')
    for name in (OWNER, 'cleaning_invitation_ticket'):
        if len([a for a in profile['attributes'] if a.get('name') == name]) > 1:
            fail('Server-owned profile metadata is ambiguous')
    inventory = {}
    for first in range(0, 10001, 100):
        page = objects(api('GET', '/users?' + urlencode({'first': first, 'max': 100, 'briefRepresentation': 'false'})))
        if len(page) > 100:
            fail('User inventory is malformed')
        for user in page:
            identity = uid(user.get('id'))
            if identity in inventory or len(inventory) >= 10000:
                fail('User inventory is repeated or exceeds the verified bound')
            inventory[identity] = user
        if len(page) < 100:
            break
    else:
        fail('User inventory is incomplete')
    employee_links = {entry['employee_id']: entry['user_id'] for entry in entries if not entry['ownerless']}
    for user in inventory.values():
        attributes = user.get('attributes') or {}
        if not isinstance(attributes, dict):
            fail('An identity ownership inventory is malformed')
        owners = attributes.get(OWNER, [])
        if not isinstance(owners, list):
            fail('An identity ownership inventory is malformed')
        if any(not isinstance(owner, str) for owner in owners):
            fail('An identity ownership inventory is malformed')
        if any(owner in employee_links and employee_links[owner] != user['id'] for owner in owners):
            fail('The planned employee is already bound to another login')
    snapshots = []
    for entry in entries:
        identity = entry['user_id']
        if identity not in inventory:
            fail('A planned exact login UUID is missing; no replacement or email adoption is performed')
        user = api('GET', '/users/' + identity)
        if (not isinstance(user, dict) or user.get('id') != identity or user.get('serviceAccountClientId')
                or str(user.get('username', '')).startswith('service-account-')
                or str(user.get('email', '')).casefold() != entry['email'].casefold()
                or type(user.get('enabled')) is not bool or not isinstance(user.get('attributes', {}), dict)):
            fail('A planned exact login does not match its trusted identity snapshot')
        if sum(str(u.get('email', '')).casefold() == entry['email'].casefold() for u in inventory.values()) != 1:
            fail('A planned login email is ambiguous; no identity is adopted')
        owner = user.get('attributes', {}).get(OWNER, [])
        if not isinstance(owner, list) or (owner and owner != [entry['employee_id']]):
            fail('Employee ownership conflicts with the trusted database link')
        if entry['ownerless'] and owner:
            fail('An approved ownerless administrator must remain unbound to an employee')
        if not entry['ownerless'] and not owner and not entry['allow_missing_owner']:
            fail('Missing employee ownership requires explicit known-UUID plan approval')
        memberships = objects(api('GET', '/users/' + identity + '/groups?first=0&max=101&briefRepresentation=false'))
        allowed = {'/managed-users', '/app-' + entry['role']}
        if len(memberships) > 100 or any(g.get('path') not in allowed or g.get('parentId') for g in memberships):
            fail('A planned login has unexpected group membership')
        direct = objects(api('GET', '/users/' + identity + '/role-mappings/realm'))
        effective = objects(api('GET', '/users/' + identity + '/role-mappings/realm/composite'))
        application = [r for r in direct if r.get('name') in APP_ROLES]
        if (any(r.get('name') != entry['role'] or r.get('id') != roles[entry['role']]['id'] or r.get('composite') for r in application)
                or {r.get('name') for r in effective if r.get('name') in APP_ROLES} != {entry['role']}):
            fail('The login application role differs from the trusted database role')
        ready = ({g['path'] for g in memberships} == allowed and not application and user['enabled'] == entry['enabled']
                 and (entry['ownerless'] or owner == [entry['employee_id']]))
        if user['enabled'] != entry['expected_enabled'] and not ready:
            fail('Account enabled state changed since the reviewed snapshot')
        snapshots.append({'entry': entry, 'user': copy.deepcopy(user), 'direct': direct, 'credentials': credentials(api, identity), 'ready': ready})
    return realm, profile, roles, snapshots


def migrate(api, plan, runtime_secret, maintenance, verify, runtime_proof, apply=False):
    entries = validate_plan(plan)
    if not isinstance(runtime_secret, str) or not runtime_secret.strip():
        fail('Supply the protected runtime credential; no secret is generated or rotated')
    realm, profile, roles, snapshots = preflight(api, plan, entries)
    clients = objects(api('GET', '/clients?clientId=' + quote(plan['runtime_client_id'], safe='')))
    if len(clients) > 1:
        fail('Runtime client identity is ambiguous')
    if clients:
        client = clients[0]
        uid(client.get('id'))
        if (client.get('clientId') != plan['runtime_client_id'] or client.get('enabled') is not True or client.get('publicClient') is not False
                or client.get('serviceAccountsEnabled') is not True or client.get('standardFlowEnabled') is not False
                or client.get('directAccessGrantsEnabled') is not False or client.get('fullScopeAllowed') is not False):
            fail('An existing runtime client has unexpected settings')
        subject = api('GET', '/clients/' + client['id'] + '/service-account-user')
        uid(subject.get('id'))
        runtime_proof()  # Existing secret must work before any write.
        configure(api, lambda *args: None, realm, client, subject['id'], apply=False)
    elif realm.get('attributes', {}).get(CLIENT_ATTRIBUTE) is not None:
        fail('An extension binding exists without its exact runtime client')
    changes = []

    def write(label, method, path, body):
        changes.append(label)
        if apply:
            maintenance()
            api(method, path, body)

    if apply:
        maintenance()
    updated = copy.deepcopy(profile)
    changed = False
    for name, display in ((OWNER, 'Employee ownership'), ('cleaning_invitation_ticket', 'Invitation ticket')):
        matches = [a for a in updated['attributes'] if a.get('name') == name]
        desired = {'name': name, 'displayName': display, 'multivalued': False, 'permissions': {'view': ['admin'], 'edit': ['admin']}}
        if not matches:
            updated['attributes'].append(desired); changed = True
        elif any(matches[0].get(k) != desired[k] for k in ('multivalued', 'permissions')):
            matches[0].update(desired); changed = True
    if changed:
        write('protect server-owned profile metadata', 'PUT', '/users/profile', updated)
    if not clients:
        desired = {'clientId': plan['runtime_client_id'], 'enabled': True, 'publicClient': False, 'serviceAccountsEnabled': True,
                   'standardFlowEnabled': False, 'directAccessGrantsEnabled': False, 'fullScopeAllowed': False, 'secret': runtime_secret}
        write('create isolated runtime client', 'POST', '/clients', desired)
        if apply:
            clients = objects(api('GET', '/clients?clientId=' + quote(plan['runtime_client_id'], safe='')))
            if len(clients) != 1:
                fail('The created runtime client could not be resolved exactly')
            client = clients[0]
            uid(client.get('id'))
            subject = api('GET', '/clients/' + client['id'] + '/service-account-user')
            uid(subject.get('id'))
            runtime_proof()
        else:
            changes.append('configure fixed groups and isolated runtime audience before enrollment')
    deferred = []
    groups = None
    if clients:
        def extension_change(label, method, path, body):
            if label == 'bind extension to exact runtime client':
                deferred.append(client['id'])
            else:
                write(label, method, path, body)
        groups = configure(api, extension_change, api('GET', ''), client, subject['id'], apply=apply)
    if apply:
        maintenance()
    for snapshot in snapshots:
        entry, original = snapshot['entry'], snapshot['user']
        if snapshot['ready']:
            continue
        user_path = '/users/' + entry['user_id']
        if apply:
            current = api('GET', user_path)
            if any(current.get(k) != original.get(k) for k in ('username', 'email', 'emailVerified', 'requiredActions', 'enabled', 'attributes')):
                fail('A planned identity changed after maintenance preflight')
        # Native USER_API update removes absent raw profile fields when attributes
        # are supplied. Preserve only the existing identity/name fields explicitly;
        # never replay credentials, requiredActions, verification or federation.
        identity_fields = {k: original.get(k) for k in ('username', 'email', 'firstName', 'lastName')}
        if not entry['ownerless'] and not original.get('attributes', {}).get(OWNER):
            attributes = copy.deepcopy(original.get('attributes') or {})
            attributes[OWNER] = [entry['employee_id']]
            write('bind approved exact employee UUID', 'PUT', user_path, dict(identity_fields, attributes=attributes))
        membership_paths = {g['path'] for g in objects(api('GET', user_path + '/groups?first=0&max=101&briefRepresentation=false'))}
        for name in ('managed-users', 'app-' + entry['role']):
            if '/' + name not in membership_paths:
                if apply and (not groups or not groups.get(name)):
                    fail('A fixed group identity is unavailable')
                write('enroll exact login into fixed application groups', 'PUT', user_path + '/groups/' + (groups.get(name) if groups else 'pending-fixed-group'), None)
        if original['enabled'] != entry['enabled']:
            write('synchronize approved employee enabled state', 'PUT', user_path, dict(identity_fields, enabled=entry['enabled']))
        if apply:
            joined = objects(api('GET', user_path + '/groups?first=0&max=101&briefRepresentation=false'))
            if ({g.get('path') for g in joined} != {'/managed-users', '/app-' + entry['role']}
                    or any(g.get('id') != groups[g['path'][1:]] for g in joined)):
                fail('Fixed group enrollment did not persist; legacy grants were retained')
        matched = [r for r in snapshot['direct'] if r.get('name') == entry['role'] and r.get('id') == roles[entry['role']]['id']]
        if matched:
            write('remove only matched legacy direct application role', 'DELETE', user_path + '/role-mappings/realm', matched)
        if apply:
            current = api('GET', user_path)
            memberships = objects(api('GET', user_path + '/groups?first=0&max=101&briefRepresentation=false'))
            direct = objects(api('GET', user_path + '/role-mappings/realm'))
            effective = objects(api('GET', user_path + '/role-mappings/realm/composite'))
            stable = ('username', 'email', 'firstName', 'lastName', 'emailVerified', 'requiredActions')
            if (any(current.get(k) != original.get(k) for k in stable) or credentials(api, entry['user_id']) != snapshot['credentials']
                    or current.get('enabled') is not entry['enabled']
                    or current.get('attributes', {}).get(OWNER, []) != ([] if entry['ownerless'] else [entry['employee_id']])
                    or {g.get('path') for g in memberships} != {'/managed-users', '/app-' + entry['role']}
                    or any(r.get('name') in APP_ROLES for r in direct)
                    or {r.get('id') for r in direct} != {r.get('id') for r in snapshot['direct'] if r.get('name') not in APP_ROLES}
                    or {r.get('name') for r in effective if r.get('name') in APP_ROLES} != {entry['role']}):
                fail('Enrollment did not preserve the approved identity, role and authentication state')
            maintenance()
    if apply:
        maintenance()
        validate_operator_boundary(api)
        # Re-read rather than replaying configure's earlier attributes snapshot.
        if deferred:
            fresh = api('GET', '')
            attrs = dict(fresh.get('attributes') or {})
            if attrs.get(CLIENT_ATTRIBUTE) not in (None, client['id']):
                fail('The final extension binding changed during maintenance')
            attrs[CLIENT_ATTRIBUTE] = client['id']
            write('activate exact runtime client after all enrollment', 'PUT', '', {'attributes': attrs})
        verify()
        configure(api, lambda *args: fail('Identity setup has not converged'), api('GET', ''),
                  objects(api('GET', '/clients?clientId=' + quote(plan['runtime_client_id'], safe='')))[0], subject['id'], apply=False)
        maintenance()
    elif deferred:
        changes.append('activate exact runtime client after all enrollment')
    return {'mode': 'applied' if apply else 'check', 'planned_identities': len(entries), 'changes': changes,
            'requires_whole_database_restore_on_failure': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', required=True, help='Literal-loopback private administration base with explicit port')
    parser.add_argument('--plan', required=True, help='Protected host JSON generated from the reviewed database snapshot')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--production', action='store_true')
    args = parser.parse_args()
    if args.apply and not args.production:
        parser.error('Production writes require both --apply and --production')
    path = Path(args.plan)
    if not path.is_file() or path.is_symlink() or path.stat().st_size > 1048576:
        fail('The protected migration plan is missing or exceeds its bound')
    if os.name != 'posix' or path.stat().st_mode & 0o077 or path.stat().st_uid != os.geteuid():
        fail('Use an owner-only protected plan on the production maintenance host')
    plan = json.loads(path.read_text(encoding='utf-8'))
    validate_plan(plan)
    transport = Transport(args.server, plan['tenant'], os.environ.get('KEYCLOAK_MIGRATION_ADMIN_TOKEN', ''),
                          plan['runtime_client_id'], os.environ.get('KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET', ''))
    transport.discovery(plan['expected_issuer'])
    result = migrate(transport.api, plan, transport.runtime_secret, Maintenance(plan['maintenance']), transport.verify, transport.runtime_token, apply=args.apply)
    print(json.dumps(result))
    return 2 if not args.apply and result['changes'] else 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, TypeError, KeyError, IndexError, AttributeError, OSError, subprocess.SubprocessError):
        # No upstream body, account identity, plan content, URL or secret in diagnostics.
        print('Production identity maintenance failed; keep ingress/API stopped and use the reviewed whole-database rollback')
        raise SystemExit(1)
