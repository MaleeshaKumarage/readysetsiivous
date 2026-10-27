# Native guarded invitation regressions

Run only against the explicitly owned disposable Keycloak/Mailpit fixture.
The harness requires Python 3.10+ and uses real server-rendered Keycloak forms,
cookies, SMTP capture and native password-grant authentication. It does not mock
credential storage or action-token handlers.

Configure these environment variables without printing their values:

```text
SYNTHETIC_KEYCLOAK_URL=http://127.0.0.1:18480
SYNTHETIC_MAILBOX_URL=http://127.0.0.1:12525
SYNTHETIC_KEYCLOAK_ADMIN_USER=<disposable-bootstrap-user>
SYNTHETIC_KEYCLOAK_ADMIN_PASSWORD=<disposable-bootstrap-password>
```

Then run from the repository root:

```bash
python3 -B api/keycloak-identity-extension/tests/guarded_invitations.py
```

The Keycloak container must resolve `invite-review-mail:1025` to the disposable
Mailpit container. Only explicitly configured loopback HTTP origins are accepted.
No real recipients or operator identity are used. An independent synthetic realm
is created and deleted in `finally`. The master-scope regression temporarily
configures captured SMTP, creates one synthetic operator/client, and deletes
those exact identities and restores SMTP in `finally`; no existing master user
or password is modified. Destroy the disposable fixture if interrupted.

The owned HTTP loopback fixture emits Secure authentication cookies. The harness
allows those cookies over HTTP only inside its private cookie jar, with every
request and redirect confined to the exact validated loopback origin. This
models the fixture's browser behavior; it is not a public-HTTP cookie policy or
evidence about production HTTPS/SameSite behavior.

Coverage includes guarded setup/login, invalid-password retry, signed-token type,
ticket secrecy, metadata forgery denial, replay, successful/failed reinvitation,
superseded links and already-open forms, disable/reactivate, ownership/email
changes, direct/composite/group administration privileges, logout and actual role
changes, native legacy links, already-open legacy sessions, email-verification
handler enforcement, ordinary unbound-owner and owned-employee password recovery,
native administrative disable/group/role cycles, rejected direct/composite/group
administration grants and master-realm operator MFA enrollment. The master test
requires password plus TOTP enrollment, rejects login without OTP, and verifies a
fresh OTP login. Two concurrent password submissions must produce exactly
one success page, and only that submission's password may authenticate afterward.

Results print sanitized check names and booleans only. Action links, signed JWTs,
ticket values, cookies, credentials, recipients and HTML bodies stay in memory.
A nonzero exit is a failure; there is no skip or insecure-status acceptance mode.

## Separate credential-rollback fault test

The normal extension Dockerfile copies only `src/` and includes no test fault
provider. Build a separate image, based on the normal reviewed image, explicitly:

```bash
docker build --build-arg IDENTITY_BASE=cleaning-identity-rehearsal:ci \
  -f api/keycloak-identity-extension/tests/fault-provider/Dockerfile \
  -t cleaning-identity-rehearsal-fault:ci api/keycloak-identity-extension
```

Run that image in the same disposable fixture in place of its Keycloak container,
then use the same environment variables and run:

```bash
python3 -B api/keycloak-identity-extension/tests/credential_rollback.py
```

Only this test image contains a global event listener that can arm one synthetic
`guarded-invite-*` realm/user. The listener observes the real native
`UPDATE_PASSWORD` success event after `updateCredential`, then forces a prepare
transaction failure. The test checks actual credential/database rollback while
the already-consumed cache ticket and open form remain unusable. It also verifies
the old password still authenticates, proposed new passwords do not, and a fresh
invitation works after the test fault is disarmed. No fault flag, endpoint or
listener is included in the normal provider JAR/image.

These are single-node deployment tests. They do not prove multi-node cache
atomicity or global serialization against unrestricted infrastructure operators
who can disable or replace the security boundary itself.

## PostgreSQL persistence coverage

The security CI job connects its existing synthetic PostgreSQL service to the
owned identity network and creates a separate `keycloak_identity` database.
Both the normal image and the later fault image explicitly select the PostgreSQL
driver and that database; startup checks the persisted master-realm row. The
application tests retain their independent `identity_tests` database. Cleanup
disconnects the PostgreSQL service before removing the test network.

The [dated PostgreSQL evidence](../../../docs/security/2026-10-27-postgresql-identity-verification.json)
records 40 API canaries, 139 guarded-invitation/MFA checks and 10 credential-rollback
checks against PostgreSQL 16.15 using the reviewed normal and separate fault
images. This extends the earlier H2 fixture evidence without claiming clustered
cache correctness or production-mode deployment verification.
