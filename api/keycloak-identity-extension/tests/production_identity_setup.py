#!/usr/bin/env python3
"""Disposable native rehearsal; never addresses production or opens ingress."""
import copy
import importlib.util
import json
import os
import re
import subprocess
import sys
import uuid
from pathlib import Path

deploy = Path(os.environ.get('SYNTHETIC_DEPLOY_DIR', str(Path(__file__).resolve().parents[2] / 'deploy'))).resolve()
sys.path.insert(0, str(deploy))
spec = importlib.util.spec_from_file_location('production_identity', deploy / 'keycloak-production-identity-setup.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
server = os.environ['SYNTHETIC_KEYCLOAK_URL']
container_name = os.environ['SYNTHETIC_STOPPED_API_CONTAINER']
purpose = os.environ['SYNTHETIC_FIXTURE_PURPOSE']
if not re.fullmatch(r'identity-[a-z0-9-]+', purpose):
    raise SystemExit('Use a uniquely purpose-labelled disposable identity fixture')


def inspected():
    result = subprocess.run(['docker', 'inspect', '--format', '{"Id":{{json .Id}},"State":{{json .State}},"Labels":{{json .Config.Labels}}}', container_name],
                            capture_output=True, text=True, check=True, timeout=15)
    return json.loads(result.stdout)


def maintenance():
    value = inspected()
    if (value['State']['Running'] is not False or value['State'].get('Restarting') is not False
            or value['State'].get('Status') not in {'exited', 'created'} or value['Labels'].get('purpose') != purpose
            or value['Labels'].get('com.docker.compose.service') != 'api'):
        raise RuntimeError('The owned synthetic API fixture must remain stopped')


realm = 'production-rehearsal-' + uuid.uuid4().hex[:20]
runtime_secret = uuid.uuid4().hex + uuid.uuid4().hex
# The transport itself refuses public/non-loopback administration targets.
temporary = module.Transport(server, realm, 'not-used-for-public-token-request', 'employee-runtime', runtime_secret)
_, auth = temporary.http('POST', '/realms/master/protocol/openid-connect/token',
                         {'grant_type': 'password', 'client_id': 'admin-cli',
                          'username': os.environ['SYNTHETIC_KEYCLOAK_ADMIN_USER'],
                          'password': os.environ['SYNTHETIC_KEYCLOAK_ADMIN_PASSWORD']}, form=True)
transport = module.Transport(server, realm, auth['access_token'], 'employee-runtime', runtime_secret)
created = False
checks = []


def check(name, condition):
    if not condition:
        raise AssertionError(name)
    checks.append(name)


def api(method, path='', body=None, missing=False):
    return transport.api(method, path, body, missing)


try:
    maintenance()
    transport.http('POST', '/admin/realms', {'realm': realm, 'enabled': True,
                    'attributes': {'frontendUrl': module.PUBLIC_ORIGIN},
                    'smtpServer': {'host': 'smtp.invalid', 'from': 'fixture@example.invalid'}}, token=transport.admin_token)
    created = True
    transport.discovery(module.PUBLIC_ORIGIN + '/realms/' + realm)
    for name in ('admin', 'manager', 'employee', 'legacy-retained'):
        api('POST', '/roles', {'name': name})
    profile = api('GET', '/users/profile')
    profile['attributes'].append({'name': module.OWNER, 'multivalued': False, 'permissions': {'view': ['admin'], 'edit': ['admin']}})
    api('PUT', '/users/profile', profile)
    api('POST', '/clients', {'clientId': 'cleaning-suite-web', 'enabled': True, 'publicClient': True,
                           'standardFlowEnabled': True, 'directAccessGrantsEnabled': True,
                           'redirectUris': ['https://readysetsiivous.fi/account/*']})
    employees, owners, snapshots = [], [], []
    definitions = [('bound', 'employee', True, True), ('inactive', 'employee', False, False), ('owner', 'admin', True, False), ('unmatched', 'employee', True, False)]
    for label, role, target_enabled, bound in definitions:
        employee = str(uuid.uuid4())
        email = label + '-' + uuid.uuid4().hex + '@example.invalid'
        body = {'username': email, 'email': email, 'enabled': True, 'emailVerified': True,
                'firstName': 'Synthetic', 'lastName': 'Fixture', 'requiredActions': ['VERIFY_EMAIL'],
                'credentials': [{'type': 'password', 'value': 'SyntheticFixtureOnly2026!', 'temporary': False},
                                {'type': 'otp', 'userLabel': 'Synthetic MFA',
                                 'secretData': json.dumps({'value': 'JBSWY3DPEHPK3PXP'}),
                                 'credentialData': json.dumps({'subType': 'totp', 'digits': 6, 'counter': 0, 'period': 30, 'algorithm': 'HmacSHA1'})}]}
        if bound:
            body['attributes'] = {module.OWNER: [employee]}
        api('POST', '/users', body)
        user = api('GET', '/users?' + module.urlencode({'email': email, 'exact': 'true', 'first': 0, 'max': 2}))[0]
        for name in (role, 'legacy-retained'):
            api('POST', '/users/' + user['id'] + '/role-mappings/realm', [api('GET', '/roles/' + name)])
        current = api('GET', '/users/' + user['id'])
        credentials = module.credentials(api, user['id'])
        check('native-password-and-mfa-seeded-' + label, {c[1] for c in credentials} == {'password', 'otp'})
        snapshots.append((label, copy.deepcopy(current), credentials))
        if label == 'owner':
            owners.append({'user_id': user['id'], 'email': email, 'expected_enabled': True, 'approve_ownerless_admin': True})
        elif label != 'unmatched':
            employees.append({'user_id': user['id'], 'employee_id': employee, 'email': email, 'expected_enabled': True,
                              'enabled': target_enabled, 'role': role, 'allow_missing_owner': not bound})
    stopped = inspected()
    document = {'schema_version': 1, 'tenant': realm, 'expected_issuer': module.PUBLIC_ORIGIN + '/realms/' + realm,
                'expected_version': module.VERSION, 'runtime_client_id': 'employee-runtime',
                'maintenance': {'api_container_name': container_name, 'api_container_id': stopped['Id'],
                                'compose_project': stopped['Labels']['com.docker.compose.project']},
                'employees': employees, 'tenant_admin_owners': owners}
    proposed = module.migrate(api, document, runtime_secret, maintenance, transport.verify, transport.runtime_token, apply=False)
    check('check-proposes-without-client-creation', bool(proposed['changes']) and api('GET', '/clients?clientId=employee-runtime') == [])
    applied = module.migrate(api, document, runtime_secret, maintenance, transport.verify, transport.runtime_token, apply=True)
    check('three-exact-identities-enrolled', applied['planned_identities'] == 3)
    for label, original, credential_state in snapshots:
        current = api('GET', '/users/' + original['id'])
        check('native-credentials-and-mfa-preserved-' + label, module.credentials(api, original['id']) == credential_state)
        check('required-actions-and-verification-preserved-' + label, all(current.get(k) == original.get(k) for k in ('requiredActions', 'emailVerified', 'username', 'email', 'firstName', 'lastName')))
        groups = api('GET', '/users/' + original['id'] + '/groups?first=0&max=101&briefRepresentation=false')
        direct = api('GET', '/users/' + original['id'] + '/role-mappings/realm')
        check('non-application-role-preserved-' + label, any(r['name'] == 'legacy-retained' for r in direct))
        if label == 'unmatched':
            check('unmatched-account-not-adopted', current == original and groups == [])
        elif label == 'owner':
            check('owner-remains-without-employee-binding', not current.get('attributes', {}).get(module.OWNER)
                  and {g['path'] for g in groups} == {'/managed-users', '/app-admin'})
        elif label == 'inactive':
            check('known-ownerless-inactive-login-bound-and-disabled', current['enabled'] is False
                  and current.get('attributes', {}).get(module.OWNER) == [employees[1]['employee_id']])
    result = module.migrate(api, document, runtime_secret, maintenance, transport.verify, transport.runtime_token, apply=False)
    check('same-plan-check-converges', result['changes'] == [])
    again = module.migrate(api, document, runtime_secret, maintenance, transport.verify, transport.runtime_token, apply=True)
    check('same-plan-apply-is-noop', again['changes'] == [])
    transport.verify()
    check('runtime-extension-and-admin-rest-denials', True)
    maintenance()
    print(json.dumps({'native_checks': len(checks), 'passed': True, 'scope': 'disposable-maintenance-rehearsal'}))
finally:
    if created:
        api('DELETE', '')
