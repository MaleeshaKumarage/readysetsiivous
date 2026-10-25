#!/usr/bin/env python3
"""Disposable-only Keycloak runtime privilege rehearsal; creates synthetic realms."""
import json, os, time, uuid
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from urllib.parse import urlencode

server = os.environ['SYNTHETIC_KEYCLOAK_URL'].rstrip('/')
assert server.startswith(('http://127.0.0.1:', 'http://localhost:'))


def request(method, path, body=None, token=None, form=False, allowed=(200,201,204)):
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
    return json.loads(raw) if raw else location


def token(realm, client, secret):
    return request('POST', '/realms/' + realm + '/protocol/openid-connect/token',
                   {'grant_type':'client_credentials','client_id':client,'client_secret':secret}, form=True)['access_token']


def main():
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
    for name in ('manage-users','query-users','view-users','view-realm','view-clients'):
        granted.append(request('GET', root+'/clients/'+management['id']+'/roles/'+name, token=admin))
    request('POST', root+'/users/'+service['id']+'/role-mappings/clients/'+management['id'], granted, admin)
    scoped = token(tenant,'employee-runtime',secret)
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
    # Explicit deletion confined to these synthetic realms, no external targets.
    for realm in (tenant,other): request('DELETE','/admin/realms/'+realm,token=admin)
    print('PASS synthetic tenant identity operations and cross-realm/configuration/admin-role denials')


if __name__ == '__main__': main()
