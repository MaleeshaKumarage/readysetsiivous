#!/usr/bin/env python3
"""Native post-consumption rollback regression; requires the TEST-ONLY image."""
import json
import re

from guarded_invitations import (Browser, Fixture, RESULTS, OLD_PASSWORD,
                                NEW_PASSWORD, OTHER_PASSWORD, check,
                                completed_password, has_password_form)

ARM = 'synthetic_test_rollback_password_user'


def main():
    fixture = Fixture()
    try:
        fixture.setup()
        user = fixture.user('post-consumption-rollback')
        attrs = dict(fixture.admin('GET', '').get('attributes', {}))
        attrs[ARM] = user['id']
        fixture.admin('PUT', '', {'attributes': attrs})
        link = fixture.invite(user)
        browser = Browser()
        form = browser.open(link)
        check('rollback-test-real-guarded-password-form', has_password_form(form))
        before = fixture.state(user)
        pointer = fixture.admin('GET', '/users/' + user['id'])['attributes'].get('cleaning_invitation_ticket')
        check('rollback-test-ticket-present-before-submit', bool(pointer))
        response = browser.submit_password(form, NEW_PASSWORD)
        check('post-consumption-credential-commit-rejected', response.status >= 400 and not completed_password(response))
        check('credential-transaction-actually-rolled-back', fixture.state(user) == before)
        restored_pointer = fixture.admin('GET', '/users/' + user['id'])['attributes'].get('cleaning_invitation_ticket')
        check('database-ticket-pointer-rolled-back', restored_pointer == pointer)
        # These checks happen before any administrative cleanup could revoke a
        # ticket itself: its immediate atomic cache removal must survive DB rollback.
        check('consumed-cache-ticket-not-revived-by-database-rollback', not has_password_form(Browser().open(link)))
        browser.submit_password(form, OTHER_PASSWORD)
        check('failed-open-form-cannot-retry-consumed-ticket', fixture.state(user) == before)
        fixture.assert_password(user, OLD_PASSWORD)
        fixture.assert_password(user, NEW_PASSWORD, False)
        fixture.assert_password(user, OTHER_PASSWORD, False)
        check('native-old-password-preserved-and-both-new-passwords-rejected', True)
        attrs = dict(fixture.admin('GET', '').get('attributes', {}))
        attrs.pop(ARM, None)
        fixture.admin('PUT', '', {'attributes': attrs})
        fixture.update(user, {'requiredActions': ['VERIFY_EMAIL', 'UPDATE_PASSWORD'], 'emailVerified': False})
        browser = Browser()
        form = browser.open(fixture.invite(user))
        check('fresh-invitation-required-and-available-after-fault', has_password_form(form))
        browser.submit_password(form, NEW_PASSWORD)
        fixture.assert_password(user, NEW_PASSWORD)
        check('fresh-invitation-succeeds-after-test-fault-disarmed', True)
    finally:
        fixture.close()
    print(json.dumps({'result': 'passed', 'checks': len(RESULTS), 'cleanup': 'complete'}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        detail = str(error)
        print(json.dumps({'result': 'failed', 'error_type': type(error).__name__,
                          'safe_detail': detail if re.fullmatch(r'[A-Za-z0-9 -]{1,160}', detail) else None,
                          'last_passed_check': RESULTS[-1] if RESULTS else None}), flush=True)
        raise SystemExit(1)
