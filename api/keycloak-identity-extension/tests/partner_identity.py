#!/usr/bin/env python3
"""Native partner fixed-group/invitation canaries, owned loopback fixture only.

Uses real Keycloak credentials/forms and captured SMTP. Application company
binding and mixed-role request denial are verified separately by API tests;
this test establishes the native role and ticket behavior they depend on.
"""
import json
import uuid

from guarded_invitations import (Fixture, Browser, NEW_PASSWORD, OTHER_PASSWORD,
    OWNER, api, check, has_password_form, completed_password, RESULTS)


def main():
    fixture = Fixture()
    try:
        fixture.setup()
        check('partner-fixed-group-present', bool(fixture.groups['app-partner']))
        email = 'partner-' + uuid.uuid4().hex + '@example.invalid'
        _, location = fixture.runtime('POST', '/users', {
            'username': email, 'email': email, 'firstName': 'Synthetic', 'lastName': 'Partner',
            'enabled': True, 'emailVerified': False,
            'groups': ['/managed-users'], 'requiredActions': ['VERIFY_EMAIL', 'UPDATE_PASSWORD'],
            'attributes': {OWNER: [str(uuid.uuid4())]}})
        user = {'id': location.rsplit('/', 1)[1], 'email': email}
        path = '/users/' + user['id']
        fixture.runtime('PUT', path + '/groups/' + fixture.groups['app-partner'])
        roles, _ = fixture.runtime('GET', path + '/role-mappings/realm/composite')
        check('partner-native-role-without-workforce-roles',
              'partner' in {r['name'] for r in roles}
              and not {'employee', 'manager', 'admin'} & {r['name'] for r in roles})
        browser = Browser()
        invitation = fixture.invite(user)
        page = browser.open(invitation)
        check('partner-invitation-real-password-form', has_password_form(page))
        result = browser.submit_password(page, NEW_PASSWORD)
        check('partner-invitation-completes-native-password', completed_password(result))
        fixture.assert_password(user, NEW_PASSWORD)
        check('partner-native-password-authentication', True)
        check('partner-invitation-replay-denied', not has_password_form(Browser().open(invitation)))

        # Application role switches must remove the old group; a mutation also
        # revokes an outstanding invitation, including an already-open form.
        pending = fixture.invite(user)
        pending_browser = Browser()
        pending_form = pending_browser.open(pending)
        check('partner-reinvitation-form-opened', has_password_form(pending_form))
        fixture.runtime('DELETE', path + '/groups/' + fixture.groups['app-partner'])
        fixture.runtime('PUT', path + '/groups/' + fixture.groups['app-employee'])
        pending_browser.submit_password(pending_form, OTHER_PASSWORD)
        fixture.assert_password(user, NEW_PASSWORD)
        fixture.assert_password(user, OTHER_PASSWORD, False)
        check('partner-to-worker-role-change-revokes-open-setup', True)
        roles, _ = fixture.runtime('GET', path + '/role-mappings/realm/composite')
        check('partner-to-worker-native-group-transition',
              'employee' in {r['name'] for r in roles} and 'partner' not in {r['name'] for r in roles})
        fixture.runtime('DELETE', path + '/groups/' + fixture.groups['app-employee'])
        fixture.runtime('PUT', path + '/groups/' + fixture.groups['app-partner'])
        fixture.runtime('PUT', path, {'enabled': False})
        fixture.runtime('PUT', path, {'enabled': True})
        fixture.assert_password(user, NEW_PASSWORD)
        check('partner-disable-reactivate-preserves-native-password', True)
        status, _, _ = api('PUT', fixture.extension + path,
            {'attributes': {OWNER: [str(uuid.uuid4())]}}, fixture.runtime_token, allowed=(403,))
        check('partner-identity-ownership-rebinding-denied', status == 403)
        status, _, _ = api('GET', fixture.root + '/users', token=fixture.runtime_token, allowed=(403,))
        check('partner-runtime-still-denied-direct-admin-rest', status == 403)
    finally:
        fixture.close()
    print(json.dumps({'result': 'passed', 'checks': len(RESULTS), 'cleanup': 'complete'}), flush=True)


if __name__ == '__main__':
    main()
