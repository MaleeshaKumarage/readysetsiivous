#!/usr/bin/env python3
"""Real Keycloak/Mailpit invitation regressions; synthetic loopback fixtures only.

No action URL, token, password, recipient or response body is printed. Every
password assertion uses the real HTML form and a native password-grant login.
"""
import base64
import hashlib
import hmac
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from html import unescape
from html.parser import HTMLParser
from http.cookiejar import CookieJar, DefaultCookiePolicy
import json
import os
from pathlib import Path
import re
import sys
import time
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urljoin, urlsplit
from urllib.request import Request, HTTPCookieProcessor, HTTPRedirectHandler, build_opener
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'deploy'))
from keycloak_identity_extension import configure


def loopback(value):
    parsed = urlsplit(value)
    if (parsed.scheme != 'http' or parsed.hostname not in ('127.0.0.1', 'localhost')
            or not parsed.port or parsed.username or parsed.password or parsed.path not in ('', '/')):
        raise ValueError('Only an explicitly configured loopback synthetic fixture is allowed')
    return value.rstrip('/')


SERVER = loopback(os.environ['SYNTHETIC_KEYCLOAK_URL'])
MAILBOX = loopback(os.environ['SYNTHETIC_MAILBOX_URL'])
OLD_PASSWORD = 'SyntheticExistingPassword2026!'
NEW_PASSWORD = 'SyntheticChangedPassword2026!'
OTHER_PASSWORD = 'SyntheticConcurrentPassword2026!'
CALLBACK = 'http://127.0.0.1:59998/account/'
OWNER = 'cleaning_employee_id'
RESULTS = []


def check(name, predicate):
    if not predicate:
        raise AssertionError(name)
    RESULTS.append(name)
    print(json.dumps({'check': name, 'passed': True}), flush=True)


def api(method, path, body=None, token=None, form=False, allowed=(200, 201, 204)):
    headers = {}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if body is not None:
        headers['Content-Type'] = 'application/x-www-form-urlencoded' if form else 'application/json'
    data = None if body is None else (urlencode(body) if form else json.dumps(body)).encode()
    try:
        response = build_opener().open(Request(SERVER + path, method=method, data=data, headers=headers), timeout=30)
    except HTTPError as error:
        response = error
    with response:
        raw = response.read(2 * 1024 * 1024)
        if response.status not in allowed:
            raise AssertionError('Synthetic API operation returned unexpected HTTP ' + str(response.status))
        try:
            value = json.loads(raw) if raw else None
        except ValueError:
            value = None
        return response.status, value, response.headers.get('Location')


def admin_token():
    return api('POST', '/realms/master/protocol/openid-connect/token', {
        'grant_type': 'password', 'client_id': 'admin-cli',
        'username': os.environ['SYNTHETIC_KEYCLOAK_ADMIN_USER'],
        'password': os.environ['SYNTHETIC_KEYCLOAK_ADMIN_PASSWORD']}, form=True)[1]['access_token']


def jwt_type(link):
    value = parse_qs(urlsplit(link).query)['key'][0].split('.')[1]
    return json.loads(base64.urlsafe_b64decode(value + '=' * (-len(value) % 4)))['typ']


class Page(HTMLParser):
    def __init__(self, text):
        super().__init__()
        self.forms, self.links, self.current, self.anchor = [], [], None, None
        self.feed(text)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'form':
            self.current = {'action': attrs.get('action', ''), 'fields': {}, 'id': attrs.get('id', '')}
            self.forms.append(self.current)
        elif tag == 'input' and self.current is not None and attrs.get('name'):
            if attrs.get('type') not in ('submit', 'button', 'checkbox'):
                self.current['fields'][attrs['name']] = attrs.get('value', '')
        elif tag == 'a':
            self.anchor = {'href': attrs.get('href', ''), 'text': ''}
            self.links.append(self.anchor)

    def handle_data(self, value):
        if self.anchor is not None:
            self.anchor['text'] += value

    def handle_endtag(self, tag):
        if tag == 'form':
            self.current = None
        elif tag == 'a':
            self.anchor = None


class InternalRedirects(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        if urlsplit(newurl)[:2] != urlsplit(SERVER)[:2]:
            return None
        return super().redirect_request(request, fp, code, msg, headers, newurl)


@dataclass
class Response:
    status: int
    url: str
    html: str
    location: str | None


class Browser:
    def __init__(self):
        # Keycloak's action flow emits Secure cookies even on this owned HTTP
        # loopback fixture. Browsers treat loopback as potentially trustworthy;
        # urllib does not. Confine the exception to this isolated jar: SERVER is
        # validated loopback-only, and requests plus redirects enforce its exact
        # scheme/authority. Never use this policy for public HTTP or production.
        self.cookies = CookieJar(policy=DefaultCookiePolicy(secure_protocols=('https', 'wss', 'http')))
        self.opener = build_opener(HTTPCookieProcessor(self.cookies), InternalRedirects())

    def request(self, url, fields=None):
        if urlsplit(url)[:2] != urlsplit(SERVER)[:2]:
            raise AssertionError('Browser fixture attempted an external request')
        data = None if fields is None else urlencode(fields).encode()
        try:
            response = self.opener.open(Request(url, data=data,
                headers={} if fields is None else {'Content-Type': 'application/x-www-form-urlencoded'}), timeout=30)
        except HTTPError as error:
            response = error
        with response:
            return Response(response.status, response.url, response.read(2 * 1024 * 1024).decode('utf-8', 'replace'), response.headers.get('Location'))

    def open(self, link):
        response = self.request(link)
        for _ in range(4):
            page = Page(response.html)
            if any('password-new' in form['fields'] for form in page.forms):
                return response
            proceed = [a for a in page.links if 'proceed' in a['text'].lower()]
            if not proceed:
                return response
            response = self.request(urljoin(response.url, proceed[0]['href']))
        raise AssertionError('Too many native setup confirmation pages')

    def submit_password(self, response, password, confirm=None):
        forms = [form for form in Page(response.html).forms if 'password-new' in form['fields']]
        if len(forms) != 1:
            raise AssertionError('Expected exactly one real native password form')
        fields = dict(forms[0]['fields'], **{'password-new': password, 'password-confirm': confirm or password})
        return self.request(urljoin(response.url, forms[0]['action']), fields)


def has_password_form(response):
    return any('password-new' in form['fields'] for form in Page(response.html).forms)


def completed_password(response):
    # Native execute-actions completes with this info page; an expired/rejected
    # flow has a different error page, even when both responses are HTTP 200.
    return bool(re.search(r'(?:account|password) has been updated', response.html, re.IGNORECASE))


class Fixture:
    def __init__(self):
        self.realm = 'guarded-invite-' + uuid.uuid4().hex
        self.root = '/admin/realms/' + self.realm
        self.extension = '/realms/' + self.realm + '/cleaning-identity/v1'
        self.created = False
        self.secret = uuid.uuid4().hex
        self.mail_ids = set()

    def admin(self, method, suffix, body=None, missing=False):
        status, value, location = api(method, self.root + suffix, body, admin_token(),
                                      allowed=(200, 201, 204, 404) if missing else (200, 201, 204))
        return None if status == 404 else value if value is not None else location

    def runtime(self, method, suffix, body=None):
        return api(method, self.extension + suffix, body, self.runtime_token)[1:]

    def setup(self):
        api('POST', '/admin/realms', {'realm': self.realm, 'enabled': True, 'resetPasswordAllowed': True,
            'passwordPolicy': 'length(12)', 'smtpServer': {'host': 'invite-review-mail', 'port': '1025',
                'from': 'synthetic@example.invalid', 'auth': 'false', 'starttls': 'false', 'ssl': 'false'}}, admin_token())
        self.created = True
        for role in ('employee', 'manager', 'admin'):
            self.admin('POST', '/roles', {'name': role})
        profile = self.admin('GET', '/users/profile')
        profile['attributes'].append({'name': OWNER, 'multivalued': False,
            'permissions': {'view': ['admin'], 'edit': ['admin']}})
        profile['attributes'].append({'name': 'cleaning_invitation_ticket', 'multivalued': False,
            'permissions': {'view': ['admin'], 'edit': ['admin']}})
        self.admin('PUT', '/users/profile', profile)
        self.admin('POST', '/clients', {'clientId': 'cleaning-suite-web', 'enabled': True, 'publicClient': True,
            'standardFlowEnabled': True, 'directAccessGrantsEnabled': True, 'redirectUris': [CALLBACK]})
        self.admin('POST', '/clients', {'clientId': 'employee-runtime', 'enabled': True, 'publicClient': False,
            'serviceAccountsEnabled': True, 'standardFlowEnabled': False, 'directAccessGrantsEnabled': False,
            'secret': self.secret, 'fullScopeAllowed': False})
        client = self.admin('GET', '/clients?clientId=employee-runtime')[0]
        subject = self.admin('GET', '/clients/' + client['id'] + '/service-account-user')['id']
        self.groups = configure(self.admin, lambda label, method, path, body: self.admin(method, path, body),
            self.admin('GET', ''), client, subject, apply=True)
        self.runtime_token = api('POST', '/realms/' + self.realm + '/protocol/openid-connect/token', {
            'grant_type': 'client_credentials', 'client_id': 'employee-runtime', 'client_secret': self.secret}, form=True)[1]['access_token']
        self.management = self.admin('GET', '/clients?clientId=realm-management')[0]['id']
        self.privileged = self.admin('GET', '/clients/' + self.management + '/roles/manage-users')
        self.admin('POST', '/roles', {'name': 'synthetic-operator-composite', 'composite': True})
        self.composite = self.admin('GET', '/roles/synthetic-operator-composite')
        self.admin('POST', '/roles', {'name': 'synthetic-benign-cycle'})
        self.cycle_role = self.admin('GET', '/roles/synthetic-benign-cycle')
        self.cycle_group = self.admin('POST', '/groups', {'name': 'synthetic-benign-cycle'}).rsplit('/', 1)[1]

    def user(self, label, owned=True):
        email = label + '-' + uuid.uuid4().hex[:8] + '@example.invalid'
        representation = {'username': email, 'email': email, 'firstName': 'Synthetic', 'lastName': 'Invitation',
            'enabled': True, 'emailVerified': False, 'groups': ['/managed-users', '/app-employee'],
            'requiredActions': ['VERIFY_EMAIL', 'UPDATE_PASSWORD'],
            'credentials': [{'type': 'password', 'value': OLD_PASSWORD, 'temporary': False}]}
        if owned:
            representation['attributes'] = {OWNER: [str(uuid.uuid4())]}
        uid = self.admin('POST', '/users', representation).rsplit('/', 1)[1]
        return {'id': uid, 'email': email}

    def state(self, user):
        value = self.admin('GET', '/users/' + user['id'])
        credentials = self.admin('GET', '/users/' + user['id'] + '/credentials')
        return value['emailVerified'], [(c['id'], c.get('createdDate')) for c in credentials if c['type'] == 'password']

    def update(self, user, changes):
        value = self.admin('GET', '/users/' + user['id'])
        value.pop('credentials', None)
        value.update(changes)
        self.admin('PUT', '/users/' + user['id'], value)

    def invite(self, user, legacy=False):
        query = '?' + urlencode({'client_id': 'cleaning-suite-web', 'redirect_uri': CALLBACK, 'lifespan': 3600})
        if legacy:
            self.admin('PUT', '/users/' + user['id'] + '/execute-actions-email' + query, ['VERIFY_EMAIL', 'UPDATE_PASSWORD'])
        else:
            self.runtime('PUT', '/users/' + user['id'] + '/execute-actions-email' + query, ['VERIFY_EMAIL', 'UPDATE_PASSWORD'])
        return self.mail_link(user['email'])

    def mail_link(self, email):
        for _ in range(50):
            with build_opener().open(MAILBOX + '/api/v1/messages', timeout=20) as response:
                messages = json.load(response).get('messages', [])
            for message in messages:
                if message['ID'] in self.mail_ids or not any(to.get('Address') == email for to in message.get('To', [])):
                    continue
                with build_opener().open(MAILBOX + '/api/v1/message/' + message['ID'], timeout=20) as response:
                    body = json.load(response)
                match = re.search(r'href="([^"]*login-actions/action-token[^\"]*)"', body.get('HTML', ''))
                if match:
                    self.mail_ids.add(message['ID'])
                    link = unescape(match.group(1))
                    if urlsplit(link).netloc != urlsplit(SERVER).netloc:
                        raise AssertionError('Captured synthetic action link has the wrong origin')
                    return link
            time.sleep(0.1)
        raise AssertionError('Captured synthetic setup email was not delivered')

    def set_binding(self, enabled):
        realm = self.admin('GET', '')
        attrs = dict(realm.get('attributes') or {})
        self.binding = attrs.get('cleaning_identity_runtime_client_id', getattr(self, 'binding', None))
        if enabled:
            attrs['cleaning_identity_runtime_client_id'] = self.binding
        else:
            attrs.pop('cleaning_identity_runtime_client_id', None)
        self.admin('PUT', '', {'attributes': attrs})

    def promote(self, user, kind):
        if kind == 'direct':
            path = '/users/' + user['id'] + '/role-mappings/clients/' + self.management
        elif kind == 'composite':
            name = 'benign-before-promotion-' + uuid.uuid4().hex
            self.admin('POST', '/roles', {'name': name})
            role = self.admin('GET', '/roles/' + name)
            self.admin('POST', '/users/' + user['id'] + '/role-mappings/realm', [role])
            path = '/roles/' + name + '/composites'
        else:
            group = self.admin('POST', '/groups', {'name': 'operator-' + uuid.uuid4().hex}).rsplit('/', 1)[1]
            self.admin('PUT', '/users/' + user['id'] + '/groups/' + group)
            path = '/groups/' + group + '/role-mappings/clients/' + self.management
        status, _, _ = api('POST', self.root + path, [self.privileged], admin_token(), allowed=(400, 403, 409, 500))
        check(kind + '-native-promotion-vetoed', status not in (200, 201, 204))
        effective = self.admin('GET', '/users/' + user['id'] + '/role-mappings/clients/' + self.management + '/composite')
        check(kind + '-native-promotion-did-not-persist', not effective)

    def assert_password(self, user, password, expected=True):
        # Snapshot assertions are performed before clearing pending actions. This
        # test-only clearing enables a native password grant to prove the secret.
        current = self.admin('GET', '/users/' + user['id'])
        self.update(user, {'requiredActions': [], 'emailVerified': True, 'enabled': True})
        status, _, _ = api('POST', '/realms/' + self.realm + '/protocol/openid-connect/token', {
            'grant_type': 'password', 'client_id': 'cleaning-suite-web', 'username': current['username'],
            'password': password}, form=True, allowed=(200, 400, 401))
        if (status == 200) != expected:
            raise AssertionError('Native credential verification did not match expected password')

    def close(self):
        if self.created:
            api('DELETE', self.root, token=admin_token())
            self.created = False


def rejected_before_open(fixture, label, mutate):
    user = fixture.user(label)
    link = fixture.invite(user)
    before = fixture.state(user)
    mutate(user)
    baseline = fixture.state(user)
    check(label + '-mutation-did-not-change-password', baseline[1] == before[1])
    response = Browser().open(link)
    check(label + '-no-password-form', not has_password_form(response))
    check(label + '-credentials-and-verification-unchanged', fixture.state(user) == baseline)
    fixture.assert_password(user, OLD_PASSWORD)
    fixture.assert_password(user, NEW_PASSWORD, False)


def rejected_after_open(fixture, label, mutate):
    user = fixture.user(label)
    link = fixture.invite(user)
    browser = Browser()
    form = browser.open(link)
    check(label + '-real-form-opened', has_password_form(form))
    before = fixture.state(user)
    mutate(user)
    baseline = fixture.state(user)
    check(label + '-mutation-did-not-change-password', baseline[1] == before[1])
    browser.submit_password(form, NEW_PASSWORD)
    check(label + '-credential-and-verification-write-blocked', fixture.state(user) == baseline)
    fixture.assert_password(user, OLD_PASSWORD)
    fixture.assert_password(user, NEW_PASSWORD, False)


def ordinary_forgot_password(fixture, user=None):
    unbound = user is None
    user = user or fixture.user('unbound-owner', owned=False)
    fixture.update(user, {'requiredActions': [], 'emailVerified': True})
    browser = Browser()
    query = urlencode({'client_id': 'cleaning-suite-web', 'redirect_uri': CALLBACK,
                       'response_type': 'code', 'scope': 'openid', 'state': uuid.uuid4().hex})
    login = browser.request(SERVER + '/realms/' + fixture.realm + '/protocol/openid-connect/auth?' + query)
    forgot = [a for a in Page(login.html).links if 'forgot' in a['text'].lower()]
    check('ordinary-forgot-password-link', len(forgot) == 1)
    reset = browser.request(urljoin(login.url, forgot[0]['href']))
    forms = [f for f in Page(reset.html).forms if 'username' in f['fields']]
    check('ordinary-reset-request-form', len(forms) == 1)
    browser.request(urljoin(reset.url, forms[0]['action']), dict(forms[0]['fields'], username=user['email']))
    link = fixture.mail_link(user['email'])
    form = browser.open(link)
    check('ordinary-password-reset-form', has_password_form(form))
    browser.submit_password(form, NEW_PASSWORD)
    fixture.assert_password(user, NEW_PASSWORD)
    check('ordinary-unbound-owner-recovery-works' if unbound else 'ordinary-owned-employee-recovery-works', True)


def verification_after_promotion(fixture):
    user = fixture.user('verification')
    fixture.admin('PUT', '/users/' + user['id'] + '/send-verify-email?' + urlencode({
        'client_id': 'cleaning-suite-web', 'redirect_uri': CALLBACK}))
    link = fixture.mail_link(user['email'])
    check('native-email-verification-token-obtained', jwt_type(link) == 'verify-email')
    fixture.set_binding(False)
    try:
        fixture.admin('POST', '/users/' + user['id'] + '/role-mappings/clients/' + fixture.management, [fixture.privileged])
    finally:
        fixture.set_binding(True)
    try:
        before = fixture.state(user)
        Browser().open(link)
        check('verification-handler-blocks-legacy-privileged-target', fixture.state(user) == before and before[0] is False)
    finally:
        fixture.admin('DELETE', '/users/' + user['id'] + '/role-mappings/clients/' + fixture.management, [fixture.privileged])


def native_console_cycles(fixture):
    def disabled(user):
        fixture.update(user, {'enabled': False})
        fixture.update(user, {'enabled': True})
    def membership(user):
        path = '/users/' + user['id'] + '/groups/' + fixture.cycle_group
        fixture.admin('PUT', path)
        fixture.admin('DELETE', path)
    def role_mapping(user):
        path = '/users/' + user['id'] + '/role-mappings/realm'
        fixture.admin('POST', path, [fixture.cycle_role])
        fixture.admin('DELETE', path, [fixture.cycle_role])
    def role_graph(user):
        path = '/roles/synthetic-operator-composite/composites'
        fixture.admin('POST', path, [fixture.cycle_role])
        fixture.admin('DELETE', path, [fixture.cycle_role])
    for name, mutate in (('disabled-cycle', disabled), ('group-cycle', membership),
                         ('role-mapping-cycle', role_mapping), ('role-graph-cycle', role_graph)):
        rejected_before_open(fixture, 'native-' + name + '-before-open', mutate)
        rejected_after_open(fixture, 'native-' + name + '-after-open', mutate)


def native_role_edge_vetoes(fixture):
    # Even unassigned carriers must be rejected; otherwise a concurrent benign
    # role assignment and privilege-definition write could evade user checks.
    for name in ('synthetic-operator-composite', 'default-roles-' + fixture.realm):
        path = '/roles/' + name + '/composites'
        before = fixture.admin('GET', path)
        status, _, _ = api('POST', fixture.root + path, [fixture.privileged], admin_token(), allowed=(400, 403, 409, 500))
        check('unassigned-or-default-composite-native-grant-vetoed', status not in (200, 201, 204))
        check('unassigned-or-default-composite-grant-not-persisted', fixture.admin('GET', path) == before)
    path = '/groups/' + fixture.cycle_group + '/role-mappings/clients/' + fixture.management
    status, _, _ = api('POST', fixture.root + path, [fixture.privileged], admin_token(), allowed=(400, 403, 409, 500))
    check('unassigned-group-management-grant-vetoed', status not in (200, 201, 204))
    check('unassigned-group-management-grant-not-persisted', not fixture.admin('GET', path + '/composite'))
    user = fixture.user('concurrent-role')
    name = 'concurrent-benign-' + uuid.uuid4().hex
    fixture.admin('POST', '/roles', {'name': name})
    role = fixture.admin('GET', '/roles/' + name)
    with ThreadPoolExecutor(max_workers=2) as pool:
        assignment = pool.submit(api, 'POST', fixture.root + '/users/' + user['id'] + '/role-mappings/realm',
            [role], admin_token(), allowed=(204, 400, 403, 409, 500))
        graph = pool.submit(api, 'POST', fixture.root + '/roles/' + name + '/composites',
            [fixture.privileged], admin_token(), allowed=(204, 400, 403, 409, 500))
        assigned, changed = assignment.result(), graph.result()
    check('concurrent-benign-assignment-remains-supported', assigned[0] == 204)
    check('concurrent-privileged-composite-definition-vetoed', changed[0] != 204)
    check('concurrent-writes-cannot-create-privileged-role-edge', not fixture.admin('GET', '/roles/' + name + '/composites'))
    check('concurrent-writes-cannot-promote-user', not fixture.admin('GET', '/users/' + user['id'] + '/role-mappings/clients/' + fixture.management + '/composite'))


def ordinary_reset_revokes_invitation(fixture):
    user = fixture.user('ordinary-reset')
    old = fixture.invite(user)
    browser = Browser()
    old_form = browser.open(old)
    check('ordinary-reset-old-invitation-form-open', has_password_form(old_form))
    ordinary_forgot_password(fixture, user)
    before = fixture.state(user)
    check('ordinary-reset-invalidates-old-invitation-link', not has_password_form(Browser().open(old)))
    browser.submit_password(old_form, OTHER_PASSWORD)
    check('ordinary-reset-invalidates-old-open-password-form', fixture.state(user) == before)
    fixture.assert_password(user, NEW_PASSWORD)
    fixture.assert_password(user, OTHER_PASSWORD, False)


def separate_master_operator(fixture):
    """Prove native master setup remains functional without touching its admin."""
    root = '/admin/realms/master'
    original = api('GET', root, token=admin_token())[1]
    user_id = client_id = None
    client_name = 'guarded-master-test-' + uuid.uuid4().hex
    email = 'synthetic-master-' + uuid.uuid4().hex + '@example.invalid'
    try:
        api('PUT', root, {'smtpServer': {'host': 'invite-review-mail', 'port': '1025',
            'from': 'synthetic@example.invalid', 'auth': 'false', 'starttls': 'false', 'ssl': 'false'}}, admin_token())
        api('POST', root + '/clients', {'clientId': client_name, 'enabled': True, 'publicClient': True,
            'standardFlowEnabled': True, 'directAccessGrantsEnabled': True, 'redirectUris': [CALLBACK]}, admin_token())
        client_id = api('GET', root + '/clients?' + urlencode({'clientId': client_name}), token=admin_token())[1][0]['id']
        user_id = api('POST', root + '/users', {'username': email, 'email': email, 'firstName': 'Synthetic',
            'lastName': 'Operator', 'enabled': True, 'emailVerified': False,
            'requiredActions': ['VERIFY_EMAIL', 'UPDATE_PASSWORD', 'CONFIGURE_TOTP'],
            'credentials': [{'type': 'password', 'value': OLD_PASSWORD, 'temporary': False}]}, admin_token())[2].rsplit('/', 1)[1]
        role = api('GET', root + '/roles/admin', token=admin_token())[1]
        api('POST', root + '/users/' + user_id + '/role-mappings/realm', [role], admin_token())
        api('PUT', root + '/users/' + user_id + '/execute-actions-email?' + urlencode({
            'client_id': client_name, 'redirect_uri': CALLBACK, 'lifespan': 3600}), ['VERIFY_EMAIL', 'UPDATE_PASSWORD', 'CONFIGURE_TOTP'], admin_token())
        link = fixture.mail_link(email)
        check('master-operator-native-token-preserved', jwt_type(link) == 'execute-actions')
        browser = Browser()
        response = browser.open(link)
        secret = None
        password_complete = otp_complete = False
        period = original.get('otpPolicyPeriod', 30)
        digits = original.get('otpPolicyDigits', 6)
        digest = original.get('otpPolicyAlgorithm', 'HmacSHA1').removeprefix('Hmac').lower()
        def code(step):
            # Pinned TotpBean exposes the raw generated secret in the native
            # hidden totpSecret field; its displayed QR/manual version is base32.
            value = hmac.new(secret.encode(), int(step).to_bytes(8, 'big'), getattr(hashlib, digest)).digest()
            offset = value[-1] & 15
            number = int.from_bytes(value[offset:offset + 4], 'big') & 0x7fffffff
            return str(number % (10 ** digits)).zfill(digits)
        for _ in range(5):
            if has_password_form(response):
                check('master-operator-native-password-form', True)
                response = browser.submit_password(response, NEW_PASSWORD)
                password_complete = True
                continue
            forms = [f for f in Page(response.html).forms if 'totp' in f['fields'] and 'totpSecret' in f['fields']]
            if forms:
                check('master-operator-required-mfa-form', len(forms) == 1)
                secret = forms[0]['fields']['totpSecret']
                setup_step = int(time.time() // period)
                fields = dict(forms[0]['fields'], totp=code(setup_step), userLabel='Synthetic test authenticator')
                response = browser.request(urljoin(response.url, forms[0]['action']), fields)
                otp_complete = True
                continue
            break
        check('master-operator-completed-password-and-mfa', password_complete and otp_complete)
        status, _, _ = api('POST', '/realms/master/protocol/openid-connect/token', {
            'grant_type': 'password', 'client_id': client_name, 'username': email,
            'password': NEW_PASSWORD}, form=True, allowed=(200, 400, 401))
        check('master-operator-login-without-mfa-denied', status != 200)
        # Avoid reusing the OTP consumed during enrollment, including policies
        # that reject same-period replay. This wait is bounded to one OTP period.
        while int(time.time() // period) == setup_step:
            time.sleep(0.2)
        status, _, _ = api('POST', '/realms/master/protocol/openid-connect/token', {
            'grant_type': 'password', 'client_id': client_name, 'username': email,
            'password': NEW_PASSWORD, 'totp': code(int(time.time() // period))}, form=True, allowed=(200, 400, 401))
        check('master-operator-native-login-with-mfa-works', status == 200)
        current = api('GET', root + '/users/' + user_id, token=admin_token())[1]
        memberships = api('GET', root + '/users/' + user_id + '/groups', token=admin_token())[1]
        check('master-operator-not-an-employee', OWNER not in current.get('attributes', {}) and not memberships)
    finally:
        if user_id:
            api('DELETE', root + '/users/' + user_id, token=admin_token())
        if client_id:
            api('DELETE', root + '/clients/' + client_id, token=admin_token())
        api('PUT', root, {'smtpServer': original.get('smtpServer') or {}}, admin_token())


def run(fixture):
    user = fixture.user('success')
    link = fixture.invite(user)
    check('dedicated-signed-invitation-type', jwt_type(link) == 'cleaning-invitation')
    visible, _ = fixture.runtime('GET', '/users/' + user['id'])
    check('invitation-ticket-hidden-from-application', 'cleaning_invitation_ticket' not in json.dumps(visible))
    status, _, _ = api('PUT', fixture.extension + '/users/' + user['id'],
        {'attributes': {'cleaning_invitation_ticket': ['forbidden']}}, fixture.runtime_token, allowed=(403,))
    check('application-cannot-forge-invitation-ticket', status == 403)
    browser = Browser()
    form = browser.open(link)
    check('guarded-native-password-form', has_password_form(form))
    rejected = browser.submit_password(form, 'short')
    check('invalid-password-allows-retry', has_password_form(rejected))
    browser.submit_password(rejected, NEW_PASSWORD)
    check('successful-setup-verifies-email', fixture.state(user)[0] is True)
    fixture.assert_password(user, NEW_PASSWORD)
    check('successful-setup-native-login', True)
    before = fixture.state(user)
    check('consumed-link-replay-rejected', not has_password_form(Browser().open(link)))
    check('replay-cannot-change-credentials', fixture.state(user) == before)

    user = fixture.user('superseded')
    old = fixture.invite(user)
    new = fixture.invite(user)
    check('superseded-link-rejected', not has_password_form(Browser().open(old)))
    browser = Browser()
    form = browser.open(new)
    check('replacement-link-opens', has_password_form(form))
    browser.submit_password(form, NEW_PASSWORD)
    fixture.assert_password(user, NEW_PASSWORD)
    check('replacement-invitation-works', True)

    user = fixture.user('delivery-failure')
    old = fixture.invite(user)
    smtp = fixture.admin('GET', '')['smtpServer']
    try:
        fixture.admin('PUT', '', {'smtpServer': dict(smtp, port='1')})
        status, _, _ = api('PUT', fixture.extension + '/users/' + user['id'] + '/execute-actions-email?' + urlencode({
            'client_id': 'cleaning-suite-web', 'redirect_uri': CALLBACK, 'lifespan': 3600}),
            ['VERIFY_EMAIL', 'UPDATE_PASSWORD'], fixture.runtime_token, allowed=(503,))
        check('failed-delivery-reported', status == 503)
    finally:
        fixture.admin('PUT', '', {'smtpServer': smtp})
    browser = Browser()
    form = browser.open(old)
    check('failed-delivery-does-not-supersede-existing-link', has_password_form(form))
    browser.submit_password(form, NEW_PASSWORD)
    fixture.assert_password(user, NEW_PASSWORD)
    check('existing-link-works-after-failed-reinvite', True)

    user = fixture.user('invalid-then-revoked')
    browser = Browser()
    form = browser.open(fixture.invite(user))
    retry = browser.submit_password(form, 'short')
    check('invalid-password-retry-form-before-revocation', has_password_form(retry))
    fixture.runtime('POST', '/users/' + user['id'] + '/logout')
    before = fixture.state(user)
    browser.submit_password(retry, NEW_PASSWORD)
    check('invalid-password-retry-cannot-revive-revoked-ticket', fixture.state(user) == before)
    fixture.assert_password(user, OLD_PASSWORD)
    fixture.assert_password(user, NEW_PASSWORD, False)

    def disable_reactivate(user):
        fixture.runtime('PUT', '/users/' + user['id'], {'enabled': False})
        fixture.runtime('PUT', '/users/' + user['id'], {'enabled': True})

    for before_open, runner in ((True, rejected_before_open), (False, rejected_after_open)):
        stage = 'before-open' if before_open else 'after-open'
        runner(fixture, 'deactivate-reactivate-' + stage, disable_reactivate)
        runner(fixture, 'owner-change-' + stage,
               lambda u: fixture.update(u, {'attributes': dict(fixture.admin('GET', '/users/' + u['id']).get('attributes', {}), **{OWNER: [str(uuid.uuid4())]})}))
        runner(fixture, 'email-change-' + stage,
               lambda u: fixture.update(u, {'email': 'changed-' + uuid.uuid4().hex + '@example.invalid'}))
        for kind in ('direct', 'composite', 'group'):
            runner(fixture, kind + '-promotion-' + stage, lambda u, k=kind: fixture.promote(u, k))

    rejected_after_open(fixture, 'superseded-open-form', lambda u: fixture.invite(u))
    rejected_after_open(fixture, 'logout-open-form', lambda u: fixture.runtime('POST', '/users/' + u['id'] + '/logout'))
    rejected_after_open(fixture, 'role-change-open-form', lambda u: fixture.runtime('PUT', '/users/' + u['id'] + '/groups/' + fixture.groups['app-manager']))

    # Legacy native action-token handler and already-open password-form override.
    for opened in (False, True):
        user = fixture.user('legacy')
        fixture.set_binding(False)
        link = fixture.invite(user, legacy=True)
        check('legacy-native-token-type-' + str(opened), jwt_type(link) == 'execute-actions')
        browser = Browser()
        form = browser.open(link) if opened else None
        if opened:
            check('legacy-form-opened-before-enabling-guard', has_password_form(form))
        fixture.set_binding(True)
        before = fixture.state(user)
        if opened:
            browser.submit_password(form, NEW_PASSWORD)
        else:
            check('legacy-token-rejected-by-selected-provider', not has_password_form(browser.open(link)))
        check('legacy-state-unchanged-' + str(opened), fixture.state(user) == before)
        fixture.assert_password(user, OLD_PASSWORD)
        fixture.assert_password(user, NEW_PASSWORD, False)

    user = fixture.user('concurrent')
    link = fixture.invite(user)
    first, second = Browser(), Browser()
    forms = (first.open(link), second.open(link))
    check('concurrent-two-native-forms-open', all(has_password_form(f) for f in forms))
    with ThreadPoolExecutor(max_workers=2) as pool:
        a = pool.submit(first.submit_password, forms[0], NEW_PASSWORD)
        b = pool.submit(second.submit_password, forms[1], OTHER_PASSWORD)
        submissions = [a.result(), b.result()]
    accepted = [completed_password(response) for response in submissions]
    check('concurrent-password-exactly-one-successful-submit', sum(accepted) == 1)
    # Login results, rather than page wording, identify the single stored winner.
    fixture.update(user, {'requiredActions': [], 'emailVerified': True})
    winners = []
    for password in (NEW_PASSWORD, OTHER_PASSWORD):
        status, _, _ = api('POST', '/realms/' + fixture.realm + '/protocol/openid-connect/token', {
            'grant_type': 'password', 'client_id': 'cleaning-suite-web', 'username': user['email'],
            'password': password}, form=True, allowed=(200, 400, 401))
        winners.append(status == 200)
    check('concurrent-password-single-credential-winner', sum(winners) == 1)
    check('concurrent-winning-response-matches-stored-password', accepted == winners)
    check('concurrent-link-consumed', not has_password_form(Browser().open(link)))
    ordinary_forgot_password(fixture)
    ordinary_reset_revokes_invitation(fixture)
    native_console_cycles(fixture)
    native_role_edge_vetoes(fixture)
    verification_after_promotion(fixture)
    separate_master_operator(fixture)


def main():
    fixture = Fixture()
    try:
        fixture.setup()
        run(fixture)
    finally:
        fixture.close()
    print(json.dumps({'result': 'passed', 'checks': len(RESULTS), 'cleanup': 'complete'}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Exceptions may contain a browser URL carrying signed action tokens.
        detail = str(error)
        print(json.dumps({'result': 'failed', 'error_type': type(error).__name__,
                          'safe_detail': detail if re.fullmatch(r'[A-Za-z0-9 -]{1,160}', detail) else None,
                          'last_passed_check': RESULTS[-1] if RESULTS else None}), flush=True)
        raise SystemExit(1)
