#!/usr/bin/env python3
"""Disposable-only Keycloak runtime privilege rehearsal; creates synthetic realms."""
import json, os, time, uuid, hashlib, base64
from urllib.request import Request, urlopen, build_opener, HTTPRedirectHandler
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit, parse_qs

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
    # Explicit deletion confined to these synthetic realms, no external targets.
    for realm in (tenant,other): request('DELETE','/admin/realms/'+realm,token=admin)
    print('PASS synthetic tenant identity operations and cross-realm/configuration/admin-role denials')


if __name__ == '__main__': main()
