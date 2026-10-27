#!/usr/bin/env python3
"""Disposable-only Keycloak runtime privilege rehearsal; creates synthetic realms."""
import json, os, time, uuid, hashlib, base64, sys
from keycloak_identity_extension import configure
from urllib.request import Request, urlopen, build_opener, HTTPRedirectHandler
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit, parse_qs

server = os.environ['SYNTHETIC_KEYCLOAK_URL'].rstrip('/')
assert server.startswith(('http://127.0.0.1:', 'http://localhost:'))


def request(method, path, body=None, token=None, form=False, allowed=(200,201,204), observe=False):
    headers = {}
    if token: headers['Authorization'] = 'Bearer ' + token
    if body is not None: headers['Content-Type'] = 'application/x-www-form-urlencoded' if form else 'application/json'
    req = Request(server + path, method=method, headers=headers,
                  data=None if body is None else (urlencode(body) if form else json.dumps(body)).encode())
    try:
        with urlopen(req, timeout=20) as response:
            status, raw, location = response.status, response.read(), response.headers.get('Location')
    except HTTPError as error:
        status, raw, location = error.code, b'', None
    assert status in allowed, f'{method} {path.split("?")[0]} unexpected HTTP {status}'
    value=json.loads(raw) if raw else location
    return (status,value) if observe else value


def token(realm, client, secret):
    return request('POST', '/realms/' + realm + '/protocol/openid-connect/token',
                   {'grant_type':'client_credentials','client_id':client,'client_secret':secret}, form=True)['access_token']


def legacy_diagnostic():
    admin = request('POST', '/realms/master/protocol/openid-connect/token',
                    {'grant_type':'password','client_id':'admin-cli','username':os.environ.get('SYNTHETIC_KEYCLOAK_ADMIN_USER','test-admin'),
                     'password':os.environ['SYNTHETIC_KEYCLOAK_ADMIN_PASSWORD']}, form=True)['access_token']
    suffix = uuid.uuid4().hex[:8]
    tenant, other = 'scoped-a-' + suffix, 'scoped-b-' + suffix
    for realm in (tenant, other):
        request('POST', '/admin/realms', {'realm':realm,'enabled':True}, admin)
    root = '/admin/realms/' + tenant
    secret = uuid.uuid4().hex
    for role in ('admin','manager','employee'):
        request('POST', root+'/roles', {'name':role}, admin)
    request('POST', root+'/clients', {'clientId':'employee-runtime','enabled':True,'publicClient':False,
        'serviceAccountsEnabled':True,'standardFlowEnabled':False,'directAccessGrantsEnabled':False,
        'secret':secret,'fullScopeAllowed':True}, admin)
    runtime = request('GET', root+'/clients?clientId=employee-runtime', token=admin)[0]
    service = request('GET', root+'/clients/'+runtime['id']+'/service-account-user', token=admin)
    management = request('GET', root+'/clients?clientId=realm-management', token=admin)[0]
    granted = []
    for name in ('manage-users','query-users','view-users','view-realm'):
        granted.append(request('GET', root+'/clients/'+management['id']+'/roles/'+name, token=admin))
    request('POST', root+'/users/'+service['id']+'/role-mappings/clients/'+management['id'], granted, admin)
    scoped = token(tenant,'employee-runtime',secret)
    # Client view grants expose confidential secrets, including another privileged
    # service account. Probe directly with the stolen-runtime-token threat model.
    request('POST', root+'/clients', {'clientId':'confidential-canary','publicClient':False,'enabled':True,
        'secret':'synthetic-confidential-canary'}, admin)
    canary=request('GET',root+'/clients?clientId=confidential-canary',token=admin)[0]
    for client in (runtime, canary):
        request('GET',root+'/clients/'+client['id']+'/client-secret',token=scoped,allowed=(403,))
    smtp_secret='synthetic-smtp-canary'
    request('PUT',root,{'smtpServer':{'host':'smtp.invalid','from':'test@example.invalid','password':smtp_secret}},admin)
    observed=request('GET',root,token=scoped)['smtpServer']
    assert observed.get('password') != smtp_secret and observed.get('host') == 'smtp.invalid', 'SMTP credential disclosure'
    # Public OIDC validation works with no administrative client view or cookies.
    landing='https://site.invalid/account/?tenant='+tenant
    request('POST',root+'/clients',{'clientId':'cleaning-suite-web','enabled':True,'publicClient':True,
        'standardFlowEnabled':True,'redirectUris':[landing]},admin)
    web=request('GET',root+'/clients?clientId=cleaning-suite-web',token=admin)[0]
    class NoRedirect(HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs): return None
    def probe(redirect=landing, client='cleaning-suite-web', success=True):
        state=uuid.uuid4().hex
        challenge=base64.urlsafe_b64encode(hashlib.sha256(uuid.uuid4().hex.encode()).digest()).rstrip(b'=').decode()
        query=urlencode({'client_id':client,'redirect_uri':redirect,'response_type':'code','response_mode':'query',
            'scope':'openid','prompt':'none','state':state,'code_challenge_method':'S256','code_challenge':challenge})
        try:
            response=build_opener(NoRedirect()).open(server+'/realms/'+tenant+'/protocol/openid-connect/auth?'+query,timeout=10)
            status,location=response.status,response.headers.get('Location')
            response.close()
        except HTTPError as error:
            status,location=error.code,error.headers.get('Location')
        if success:
            assert status==302 and location, 'Public authorization preflight failed'
            actual=urlsplit(location); expected=urlsplit(redirect); fields=parse_qs(actual.query)
            assert (actual.scheme,actual.netloc,actual.path)==(expected.scheme,expected.netloc,expected.path)
            assert fields.get('state')==[state] and fields.get('error') in (['login_required'],['interaction_required'])
            assert fields.get('tenant')==[tenant] and not any(k in fields for k in ('code','access_token','id_token'))
        else: assert status==400 and not location, 'Invalid client/redirect unexpectedly accepted'
    probe()
    probe(redirect='https://unregistered.invalid/account',success=False)
    probe(client='missing-web-client',success=False)
    request('PUT',root+'/clients/'+web['id'],{'enabled':False},admin)
    probe(success=False)
    request('PUT',root+'/clients/'+web['id'],{'enabled':True},admin)
    # Server-side denial, independent of application routing guards.
    for target in (other,'master'):
        request('GET','/admin/realms/'+target,token=scoped,allowed=(403,404))
    request('PUT',root,{'smtpServer':{'host':'must-not-change.invalid','from':'test@example.invalid'}},scoped,allowed=(403,))
    request('POST',root+'/clients',{'clientId':'must-not-exist'},scoped,allowed=(403,))
    request('PUT',root+'/users/profile',{'attributes':[]},scoped,allowed=(403,))
    request('POST',root+'/roles',{'name':'must-not-exist'},scoped,allowed=(403,))
    location=request('POST',root+'/users',{'username':'synthetic-worker','email':'worker@example.invalid','enabled':True},scoped)
    user=location.rsplit('/',1)[1]
    request('GET',root+'/users?email=worker%40example.invalid&exact=true',token=scoped)
    for name in ('employee','manager','admin'):
        role=request('GET',root+'/roles/'+name,token=scoped)
        request('POST',root+'/users/'+user+'/role-mappings/realm',[role],scoped)
        request('DELETE',root+'/users/'+user+'/role-mappings/realm',[role],scoped)
    forbidden=request('GET',root+'/clients/'+management['id']+'/roles/manage-realm',token=admin)
    request('POST',root+'/users/'+user+'/role-mappings/clients/'+management['id'],[forbidden],scoped,allowed=(403,))
    request('PUT',root+'/users/'+user,{'enabled':False},scoped)
    request('POST',root+'/users/'+user+'/logout',token=scoped)
    request('PUT',root+'/users/'+user,{'enabled':True},scoped)
    request('GET',root+'/roles/admin/users?first=0&max=100',token=scoped)
    # Capability probes deliberately report allowed operations as security blockers.
    # These are synthetic-only canaries; no production identity is touched.
    canary_user_location=request('POST',root+'/users',{'username':'synthetic-administration-canary','enabled':True},admin)
    canary_user=canary_user_location.rsplit('/',1)[1]
    request('POST',root+'/users/'+canary_user+'/role-mappings/clients/'+management['id'],[forbidden],admin)
    reset_status,_=request('PUT',root+'/users/'+canary_user+'/reset-password',
        {'type':'password','value':'SyntheticResetCanaryOnly2026','temporary':False},scoped,allowed=(204,403),observe=True)
    print('OBSERVED runtime reset-password on same-realm administrative canary: HTTP '+str(reset_status))
    group_location=request('POST',root+'/groups',{'name':'synthetic-admin-role-carrier'},admin)
    group=group_location.rsplit('/',1)[1]
    request('POST',root+'/groups/'+group+'/role-mappings/clients/'+management['id'],[forbidden],admin)
    join_status,_=request('PUT',root+'/users/'+service['id']+'/groups/'+group,token=scoped,allowed=(204,403),observe=True)
    print('OBSERVED runtime joins administrative-role group: HTTP '+str(join_status))
    escalated=False
    if join_status==204:
        refreshed=token(tenant,'employee-runtime',secret)
        elevated_status,_=request('PUT',root,{'displayName':'Synthetic escalation canary'},refreshed,allowed=(204,403),observe=True)
        escalated=elevated_status==204
        print('OBSERVED refreshed group-derived runtime token realm mutation: HTTP '+str(elevated_status))
    # Explicit deletion confined to these synthetic realms, no external targets.
    for realm in (tenant,other): request('DELETE','/admin/realms/'+realm,token=admin)
    assert reset_status==403 and join_status==403 and not escalated, 'SECURITY BLOCKER: built-in manage-users permits password reset or group-derived administration; architecture review required'
    print('PASS synthetic tenant identity operations and cross-realm/configuration/admin-role denials')


def main():
    """Exercise the extension setup and direct server-side security canaries.

    Do not turn a known denial failure into an accepted HTTP status. Collect all
    observations before failing so one upstream 500 cannot hide privilege drift.
    """
    admin = request('POST', '/realms/master/protocol/openid-connect/token',
                    {'grant_type': 'password', 'client_id': 'admin-cli',
                     'username': os.environ.get('SYNTHETIC_KEYCLOAK_ADMIN_USER', 'test-admin'),
                     'password': os.environ['SYNTHETIC_KEYCLOAK_ADMIN_PASSWORD']}, form=True)['access_token']
    version = request('GET', '/admin/serverinfo', token=admin)['systemInfo']['version']
    assert version == '26.8.1', 'This FGAP rehearsal requires the reviewed Keycloak 26.8.1 build'
    created, failures = [], []

    def observe(name, method, path, body=None, expected=(403,), bearer=None):
        status, value = request(method, path, body, scoped if bearer is None else bearer,
                                allowed=tuple(range(200, 600)), observe=True)
        passed = status in expected
        print(json.dumps({'check': name, 'status': status, 'expected': list(expected), 'passed': passed}), flush=True)
        if not passed:
            failures.append(name)
        return status, value

    try:
        suffix = uuid.uuid4().hex
        tenant, other = 'fgap-ci-a-' + suffix, 'fgap-ci-b-' + suffix
        for realm in (tenant, other):
            request('POST', '/admin/realms', {'realm': realm, 'enabled': True}, admin)
            created.append(realm)
        admin_root = root = '/admin/realms/' + tenant
        secret = uuid.uuid4().hex
        for name in ('employee', 'manager', 'admin'):
            request('POST', root + '/roles', {'name': name}, admin)
        request('POST', root + '/clients', {'clientId': 'employee-runtime', 'enabled': True,
                'publicClient': False, 'serviceAccountsEnabled': True, 'standardFlowEnabled': False,
                'directAccessGrantsEnabled': False, 'secret': secret, 'fullScopeAllowed': True}, admin)
        client = request('GET', root + '/clients?clientId=employee-runtime', token=admin)[0]['id']
        subject = request('GET', root + '/clients/' + client + '/service-account-user', token=admin)['id']
        management = request('GET', root + '/clients?clientId=realm-management', token=admin)[0]['id']

        def api(method, path, body=None, missing=False):
            status, value = request(method, admin_root + path, body, admin,
                                    allowed=(200, 201, 204, 404) if missing else (200, 201, 204), observe=True)
            return None if status == 404 else value

        def change(label, method, path, body):
            api(method, path, body)

        profile = api('GET', '/users/profile')
        profile['attributes'].append({'name': 'cleaning_employee_id', 'displayName': 'Employee ownership',
                                      'multivalued': False, 'permissions': {'view': ['admin'], 'edit': ['admin']}})
        api('PUT', '/users/profile', profile)
        groups = configure(api, change, api('GET', ''), api('GET', '/clients/' + client), subject, apply=True)
        # A recheck uses actual sparse native permission representations.
        def no_change(*args):
            raise AssertionError('Native setup is not idempotent')
        configure(api, no_change, api('GET', ''), api('GET', '/clients/' + client), subject, apply=False)
        scoped = token(tenant, 'employee-runtime', secret)
        root = '/realms/' + tenant + '/cleaning-identity/v1'
        observe('anonymous-denied', 'GET', root, expected=(401, 403), bearer='')
        observe('other-issuer-denied', 'GET', root, expected=(401, 403), bearer=admin)
        alternate_secret = uuid.uuid4().hex
        api('POST', '/clients', {'clientId': 'alternate-runtime', 'enabled': True, 'publicClient': False,
                               'serviceAccountsEnabled': True, 'standardFlowEnabled': False,
                               'directAccessGrantsEnabled': False, 'fullScopeAllowed': False,
                               'secret': alternate_secret})
        alternate_token = token(tenant, 'alternate-runtime', alternate_secret)
        observe('missing-audience-denied', 'GET', root, expected=(401, 403), bearer=alternate_token)
        alternate_client = api('GET', '/clients?clientId=alternate-runtime')[0]
        api('POST', '/clients/' + alternate_client['id'] + '/protocol-mappers/models', {
            'name': 'synthetic-same-audience', 'protocol': 'openid-connect', 'protocolMapper': 'oidc-audience-mapper',
            'config': {'included.custom.audience': 'cleaning-identity-runtime', 'access.token.claim': 'true', 'id.token.claim': 'false'}})
        alternate_token = token(tenant, 'alternate-runtime', alternate_secret)
        observe('alternate-client-same-audience-denied', 'GET', root, expected=(401, 403), bearer=alternate_token)
        observe('direct-admin-read-denied', 'GET', admin_root + '/users', expected=(403,))
        observe('direct-admin-create-denied', 'POST', admin_root + '/users', {'username': 'direct-forbidden'}, expected=(403,))
        location = request('POST', root + '/users', {'username': 'managed-worker', 'enabled': True,
                           'email': 'worker@example.invalid', 'firstName': 'Synthetic', 'lastName': 'Worker',
                           'emailVerified': False, 'requiredActions': ['VERIFY_EMAIL', 'UPDATE_PASSWORD'],
                           'groups': ['/managed-users'],
                           'attributes': {'cleaning_employee_id': [str(uuid.uuid4())]}}, scoped)
        worker = location.rsplit('/', 1)[1]
        prefix = root + '/users/' + worker
        invite_query = '?' + urlencode({'client_id': 'cleaning-suite-web',
                                       'redirect_uri': 'https://site.invalid/account/', 'lifespan': 3600})
        observe('managed-read', 'GET', prefix, expected=(200,))
        observe('managed-update', 'PUT', prefix, {'firstName': 'Synthetic'}, expected=(204,))
        observe('managed-logout', 'POST', prefix + '/logout', expected=(204,))
        observe('null-invitation-action-rejected', 'PUT', prefix + '/execute-actions-email' + invite_query,
                [None], expected=(400,))
        observe('ownership-change-denied', 'PUT', prefix,
                {'attributes': {'cleaning_employee_id': [str(uuid.uuid4())]}})
        observe('credential-injection-denied', 'PUT', prefix,
                {'credentials': [{'type': 'password', 'value': 'SyntheticForbiddenOnly2026!'}]})
        for group in ('app-employee', 'app-manager', 'app-admin'):
            path = prefix + '/groups/' + groups[group]
            observe('join-' + group, 'PUT', path, expected=(204,))
            observe('leave-' + group, 'DELETE', path, expected=(204,))
        privileged = request('GET', admin_root + '/clients/' + management + '/roles/manage-users', token=admin)
        guarded_attrs = api('GET', '').get('attributes', {})
        unguarded_attrs = dict(guarded_attrs)
        unguarded_attrs.pop('cleaning_identity_runtime_client_id', None)
        # Unsafe legacy canaries are deliberately seeded with the boundary OFF;
        # the live native administration surface must never create these grants.
        api('PUT', '', {'attributes': unguarded_attrs})
        protected = request('POST', admin_root + '/users', {'username': 'protected-admin', 'enabled': True}, admin).rsplit('/', 1)[1]
        request('POST', admin_root + '/users/' + protected + '/role-mappings/clients/' + management, [privileged], admin)
        hidden = request('POST', admin_root + '/groups', {'name': 'protected-role-carrier'}, admin).rsplit('/', 1)[1]
        request('POST', admin_root + '/groups/' + hidden + '/role-mappings/clients/' + management, [privileged], admin)
        api('PUT', '', {'attributes': guarded_attrs})
        for label, uid in (('protected-admin', protected), ('service-account', subject)):
            target = root + '/users/' + uid
            observe(label + '-update-denied', 'PUT', target, {'firstName': 'Forbidden'})
            observe(label + '-reset-denied', 'PUT', target + '/reset-password',
                    {'type': 'password', 'value': 'SyntheticOnly2026!', 'temporary': False}, expected=(404, 405))
            observe(label + '-enrollment-denied', 'PUT', target + '/groups/' + groups['managed-users'])
        observe('hidden-group-creation-denied', 'POST', root + '/users',
                {'username': 'forbidden-worker', 'email': 'forbidden@example.invalid',
                 'firstName': 'Synthetic', 'lastName': 'Forbidden', 'enabled': True, 'emailVerified': False,
                 'attributes': {'cleaning_employee_id': [str(uuid.uuid4())]},
                 'groups': ['/managed-users', '/protected-role-carrier']})
        partial = request('GET', admin_root + '/users?username=forbidden-worker&exact=true', token=admin)
        if partial:
            failures.append('hidden-group-partial-user-created')
        observe('privileged-group-join-denied', 'PUT', prefix + '/groups/' + hidden)
        observe('client-secret-denied', 'GET', root + '/clients/' + client + '/client-secret', expected=(404, 405))
        observe('direct-admin-client-secret-denied', 'GET', admin_root + '/clients/' + client + '/client-secret')
        observe('realm-change-denied', 'PUT', root, {'displayName': 'Forbidden'}, expected=(404, 405))
        for target in (other, 'master'):
            observe('cross-realm-' + target, 'GET', '/admin/realms/' + target, expected=(403, 404))
        # Remove all unsafe carriers before exercising native write validation.
        api('DELETE', '/users/' + protected + '/role-mappings/clients/' + management, [privileged])
        api('DELETE', '/groups/' + hidden + '/role-mappings/clients/' + management, [privileged])
        observe('native-administration-promotion-vetoed', 'POST', admin_root + '/users/' + worker + '/role-mappings/clients/' + management,
                [privileged], expected=(400, 403, 409, 500), bearer=admin)
        effective = api('GET', '/users/' + worker + '/role-mappings/clients/' + management + '/composite')
        if effective:
            failures.append('native-promotion-persisted-despite-veto')
        # Retain defense against unsafe pre-existing state without weakening the
        # native veto. Complete role reads remain inside the privileged fixture.
        guarded_attrs = api('GET', '').get('attributes', {})
        unguarded_attrs = dict(guarded_attrs)
        unguarded_attrs.pop('cleaning_identity_runtime_client_id', None)
        api('PUT', '', {'attributes': unguarded_attrs})
        api('POST', '/users/' + worker + '/role-mappings/clients/' + management, [privileged])
        api('PUT', '', {'attributes': guarded_attrs})
        observe('drifted-admin-read-denied', 'GET', prefix)
        observe('drifted-admin-update-denied', 'PUT', prefix, {'firstName': 'ForbiddenAfterPromotion'})
        observe('drifted-admin-reset-denied', 'PUT', prefix + '/reset-password',
                {'type': 'password', 'value': 'SyntheticOnly2026!', 'temporary': False}, expected=(404, 405))
        observe('drifted-admin-invite-denied', 'PUT', prefix + '/execute-actions-email' + invite_query, ['UPDATE_PASSWORD'])
        _, listed = observe('managed-search', 'GET', root + '/users?email=worker%40example.invalid&exact=true', expected=(200,))
        if not isinstance(listed, list) or any(item.get('id') == worker for item in listed):
            failures.append('drifted-admin-search-disclosure')
        # The same target is now privileged through a realm composite, rather
        # than a direct client mapping. A direct-mappings-only check misses it.
        api('DELETE', '/users/' + worker + '/role-mappings/clients/' + management, [privileged])
        api('POST', '/roles', {'name': 'synthetic-administration-composite', 'composite': True})
        observe('unassigned-administration-carrier-vetoed', 'POST', admin_root + '/roles/synthetic-administration-composite/composites',
                [privileged], expected=(400, 403, 409, 500), bearer=admin)
        if api('GET', '/roles/synthetic-administration-composite/composites'):
            failures.append('native-unassigned-administration-carrier-persisted')
        composite = api('GET', '/roles/synthetic-administration-composite')
        guarded_attrs = api('GET', '').get('attributes', {})
        unguarded_attrs = dict(guarded_attrs)
        unguarded_attrs.pop('cleaning_identity_runtime_client_id', None)
        api('PUT', '', {'attributes': unguarded_attrs})
        api('POST', '/roles/synthetic-administration-composite/composites', [privileged])
        api('POST', '/users/' + worker + '/role-mappings/realm', [composite])
        api('PUT', '', {'attributes': guarded_attrs})
        observe('composite-admin-read-denied', 'GET', prefix)
        observe('composite-admin-update-denied', 'PUT', prefix, {'firstName': 'ForbiddenComposite'})
        api('DELETE', '/users/' + worker + '/role-mappings/realm', [composite])
        api('DELETE', '/roles/synthetic-administration-composite/composites', [privileged])
    finally:
        # Only realms successfully created in this run; never prefix-enumerate.
        cleanup_failures = []
        for realm in reversed(created):
            try:
                request('DELETE', '/admin/realms/' + realm, token=admin)
            except Exception:
                cleanup_failures.append(realm)
        if cleanup_failures:
            raise AssertionError('Disposable realm cleanup failed')
    assert not failures, 'SECURITY BLOCKERS: ' + ', '.join(failures)
    print('PASS native extension setup and server-side boundary canaries')


if __name__ == '__main__':
    if sys.argv[1:] == ['--legacy-diagnostic']:
        legacy_diagnostic()
    elif sys.argv[1:]:
        raise SystemExit('Usage: test-keycloak-runtime-privileges.py [--legacy-diagnostic]')
    else:
        main()
