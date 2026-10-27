#!/usr/bin/env python3
"""Prepare one separate master operator; check by default, never print identity/secrets."""
import argparse
import json
import os
import re
from urllib.error import HTTPError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
from uuid import UUID


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


# Never forward a privileged bearer or trust discovery through a redirect. The
# default HTTPS handler retains normal certificate/hostname verification.
urlopen = build_opener(NoRedirect()).open
ACTIONS = ['VERIFY_EMAIL', 'UPDATE_PASSWORD', 'CONFIGURE_TOTP']
LOOPBACK = {'127.0.0.1', 'localhost', '::1'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', required=True, help='Private Keycloak administration base URL')
    parser.add_argument('--public-url', required=True, help='Expected public Keycloak HTTPS base URL')
    parser.add_argument('--development', action='store_true', help='Allow literal-loopback HTTP synthetic fixtures only')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--send-setup-email', action='store_true', help='Explicitly authorize the one new operator setup invitation')
    args = parser.parse_args()
    if args.apply != args.send_setup_email:
        parser.error('Mutations require both --apply and --send-setup-email')
    parsed = []
    for value in (args.server, args.public_url):
        endpoint = urlsplit(value)
        if (endpoint.scheme not in ('http', 'https') or not endpoint.hostname or endpoint.username or endpoint.password
                or endpoint.query or endpoint.fragment or any(c.isspace() or ord(c) < 32 for c in value)
                or any(part in ('.', '..') for part in endpoint.path.split('/')) or '%' in endpoint.path or '\\' in value):
            parser.error('Use explicit HTTP(S) base URLs without credentials/query/fragment or encoded path')
        parsed.append(endpoint)
    if parsed[1].scheme != 'https' and not (args.development and parsed[1].hostname in LOOPBACK):
        parser.error('Public operator setup requires HTTPS; HTTP is only for explicit loopback development fixtures')
    synthetic = args.development and all(p.hostname in LOOPBACK and p.scheme == 'http' for p in parsed)
    server, public = args.server.rstrip('/'), args.public_url.rstrip('/')
    email = os.environ.get('KEYCLOAK_OPERATOR_EMAIL', '').strip().lower()
    token = os.environ.get('KEYCLOAK_MIGRATION_ADMIN_TOKEN', '')
    if not re.fullmatch(r'[^\s@<>\x00-\x1f]+@[^\s@<>\x00-\x1f]+\.[^\s@<>\x00-\x1f]+', email) or len(email) > 254:
        parser.error('Supply KEYCLOAK_OPERATOR_EMAIL through protected environment')
    if not token.strip() or any(ord(c) < 32 for c in token):
        parser.error('Supply KEYCLOAK_MIGRATION_ADMIN_TOKEN through protected environment')

    def http(method, target, body=None, bearer=True, allowed=(200,)):
        headers = {'Accept': 'application/json'}
        if bearer:
            headers['Authorization'] = 'Bearer ' + token
        if body is not None:
            headers['Content-Type'] = 'application/json'
        request = Request(target, method=method, headers=headers,
                          data=None if body is None else json.dumps(body).encode())
        try:
            with urlopen(request, timeout=15) as response:
                if response.status not in allowed:
                    raise RuntimeError('Operator setup request returned an unexpected status')
                raw = response.read(65537)
                if len(raw) > 65536:
                    raise RuntimeError('Operator setup response exceeded the safe limit')
                return (json.loads(raw) if raw else None), response.headers.get('Location')
        except HTTPError as error:
            # No upstream body, URL, headers, identity or token in diagnostics.
            raise RuntimeError('Operator setup request failed with HTTP ' + str(error.code)) from None

    root = server + '/admin/realms/master'
    def api(method, path='', body=None, allowed=(200,)):
        return http(method, root + path, body, allowed=allowed)
    def objects(value):
        if not isinstance(value, list) or any(not isinstance(item, dict) for item in value):
            raise RuntimeError('Operator setup received a malformed inventory')
        return value
    def uid(value):
        try:
            if not isinstance(value, str) or str(UUID(value)) != value:
                raise ValueError()
        except (ValueError, TypeError, AttributeError):
            raise RuntimeError('Operator setup received a noncanonical identity') from None
        return value

    # This public request has no administrative token/cookies. HTTPS verifies the
    # expected hostname and certificate, and NoRedirect rejects issuer relocation.
    expected_issuer = public + '/realms/master'
    for transport in (public, server):
        discovery, _ = http('GET', transport + '/realms/master/.well-known/openid-configuration', bearer=False)
        if not isinstance(discovery, dict) or discovery.get('issuer') != expected_issuer:
            raise RuntimeError('The master issuer through public and private transport must match the expected HTTPS origin')
    # Native action-email URLs use Keycloak's frontend context on the private
    # administration request. Public-only discovery would not prove that this
    # second transport also uses the fixed public HTTPS frontend origin.
    realm, _ = api('GET')
    if not isinstance(realm, dict) or realm.get('realm') != 'master' or realm.get('enabled') is not True:
        raise RuntimeError('The enabled master realm is required')
    admin_role, _ = api('GET', '/roles/admin')
    if (not isinstance(admin_role, dict) or admin_role.get('name') != 'admin'
            or admin_role.get('clientRole') is True or not admin_role.get('id')):
        raise RuntimeError('The master administration role could not be verified')

    matches, _ = api('GET', '/users?' + urlencode({'email': email, 'exact': 'true', 'first': 0, 'max': 3}))
    usernames, _ = api('GET', '/users?' + urlencode({'username': email, 'exact': 'true', 'first': 0, 'max': 3}))
    matches, usernames = objects(matches), objects(usernames)
    if len(matches) > 1 or len(usernames) > 1:
        raise RuntimeError('Operator identity is ambiguous; manual reconciliation required')
    if any(str(u.get('email', '')).lower() != email for u in matches) or any(str(u.get('username', '')).lower() != email for u in usernames):
        raise RuntimeError('Operator identity lookup was not exact')
    if bool(matches) != bool(usernames) or (matches and matches[0].get('id') != usernames[0].get('id')):
        raise RuntimeError('Existing operator identity or username collision requires manual reconciliation')

    def dedicated(user, identity, existing=False):
        if (not isinstance(user, dict) or user.get('id') != identity or str(user.get('username', '')).lower() != email
                or str(user.get('email', '')).lower() != email or user.get('enabled') is not True
                or user.get('serviceAccountClientId') or not isinstance(user.get('attributes', {}), dict)
                or any(name in user.get('attributes', {}) for name in ('cleaning_employee_id', 'cleaning_invitation_ticket'))):
            raise RuntimeError('Existing identity is not a dedicated master operator; no adoption performed')
        groups, _ = api('GET', '/users/' + identity + '/groups?first=0&max=1')
        if objects(groups):
            raise RuntimeError('Operator identity has group memberships; manual reconciliation required')
        if existing:
            direct, _ = api('GET', '/users/' + identity + '/role-mappings/realm')
            direct = objects(direct)
            if (not any(r.get('id') == admin_role['id'] and r.get('name') == 'admin' for r in direct)
                    or any(r.get('name') not in {'admin', 'default-roles-master', 'offline_access', 'uma_authorization'} for r in direct)
                    or user.get('emailVerified') is not True or user.get('requiredActions')):
                raise RuntimeError('Existing master identity is not ready; no password or privilege change performed')
            credentials, _ = api('GET', '/users/' + identity + '/credentials')
            if not any(c.get('type') == 'otp' for c in objects(credentials)):
                raise RuntimeError('Existing master operator requires MFA reconciliation; no account change performed')
        elif (user.get('emailVerified') is not False or not isinstance(user.get('requiredActions'), list)
              or len(user['requiredActions']) != len(ACTIONS) or set(user['requiredActions']) != set(ACTIONS)):
            raise RuntimeError('New operator verification and MFA requirements were not preserved')

    if matches:
        identity = uid(matches[0].get('id'))
        existing, _ = api('GET', '/users/' + identity)
        dedicated(existing, identity, existing=True)
        print(json.dumps({'status': 'already_ready', 'changed': False, 'setup_email_sent': False}))
        return 0

    console, _ = api('GET', '/clients?clientId=security-admin-console')
    console = objects(console)
    if len(console) != 1 or console[0].get('clientId') != 'security-admin-console' or console[0].get('enabled') is not True:
        raise RuntimeError('The enabled master administration console is required')
    required, _ = api('GET', '/authentication/required-actions')
    available = {entry.get('alias') for entry in objects(required) if entry.get('enabled') is True}
    if not set(ACTIONS) <= available:
        raise RuntimeError('Email, password and MFA setup actions must be enabled before operator creation')

    def smtp_ready(settings, provided=False):
        if not isinstance(settings, dict):
            return False
        if any(not isinstance(v, (str, bool, int)) or isinstance(v, str) and any(ord(c) < 32 for c in v) for v in settings.values()):
            return False
        def flag(name):
            return str(settings.get(name, 'false')).lower() == 'true'
        if any(str(settings.get(name, 'false')).lower() not in ('true', 'false') for name in ('auth', 'ssl', 'starttls')):
            return False
        if not settings.get('host') or not settings.get('from') or '\n' in str(settings['from']):
            return False
        if ('@' not in str(settings['from']) or any(c.isspace() for c in str(settings['host']))
                or not str(settings.get('port', '25')).isdigit() or not 1 <= int(settings.get('port', '25')) <= 65535):
            return False
        if not synthetic and not (flag('ssl') or flag('starttls')):
            return False
        if flag('auth') and (not settings.get('user') or not settings.get('password')):
            return False
        if provided and isinstance(settings.get('password'), str) and set(settings['password']) <= {'*', '•'}:
            return False
        return True

    current_smtp = realm.get('smtpServer') or {}
    desired_smtp = None
    if not smtp_ready(current_smtp):
        raw_smtp = os.environ.get('KEYCLOAK_OPERATOR_SMTP_JSON')
        if not raw_smtp:
            raise RuntimeError('Working protected SMTP configuration is required before operator creation')
        desired_smtp = json.loads(raw_smtp)
        if not smtp_ready(desired_smtp, provided=True):
            raise RuntimeError('Protected SMTP configuration is incomplete or insecure')
        desired_smtp = {k: str(v).lower() if isinstance(v, bool) else str(v) for k, v in desired_smtp.items()}
    if not args.apply:
        print(json.dumps({'status': 'needs_setup', 'changed': False,
                          'changes': ['operator', 'setup_email'] + (['smtp'] if desired_smtp is not None else [])}))
        return 2
    if desired_smtp is not None:
        # Only explicit protected JSON is written. Never round-trip a masked GET
        # password or merge it into a write, including when invitation delivery fails.
        api('PUT', '', {'smtpServer': desired_smtp}, allowed=(204,))
    _, location = api('POST', '/users', {'username': email, 'email': email, 'enabled': True,
                                       'emailVerified': False, 'requiredActions': list(ACTIONS)}, allowed=(201,))
    if not isinstance(location, str):
        raise RuntimeError('New operator location is missing; manual reconciliation required')
    parsed_location = urlsplit(location)
    expected_paths = {p.path.rstrip('/') + '/admin/realms/master/users/' for p in parsed}
    origins = {(p.scheme, p.netloc) for p in parsed}
    if (parsed_location.query or parsed_location.fragment or parsed_location.username or parsed_location.password
            or (parsed_location.scheme, parsed_location.netloc) not in origins
            or not any(parsed_location.path.startswith(prefix) for prefix in expected_paths)):
        raise RuntimeError('New operator location is unexpected; manual reconciliation required')
    identity = uid(parsed_location.path.rsplit('/', 1)[-1])
    if parsed_location.path.rsplit('/', 1)[0] + '/' not in expected_paths:
        raise RuntimeError('New operator location is not canonical; manual reconciliation required')
    try:
        created, _ = api('GET', '/users/' + identity)
        dedicated(created, identity)
        api('POST', '/users/' + identity + '/role-mappings/realm', [admin_role], allowed=(204,))
        query = urlencode({'client_id': 'security-admin-console',
                           'redirect_uri': public + '/admin/master/console/', 'lifespan': 1800})
        api('PUT', '/users/' + identity + '/execute-actions-email?' + query, list(ACTIONS), allowed=(204,))
    except Exception:
        try:
            # This ID came only from this invocation's successful creation.
            api('DELETE', '/users/' + identity, allowed=(204,))
        except Exception:
            raise RuntimeError('New operator setup and cleanup failed; manual reconciliation required') from None
        raise RuntimeError('New operator setup failed; newly created account removed') from None
    print(json.dumps({'status': 'smtp_accepted', 'changed': True, 'inbox_verified': False, 'mfa_completed': False}))
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, OSError):
        print(json.dumps({'status': 'error', 'code': 'operator_setup_failed'}))
        raise SystemExit(1)
