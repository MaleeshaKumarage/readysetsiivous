# Main integration release: activity reporting and identity fixes

Release PR: [#89](https://github.com/MaleeshaKumarage/readysetsiivous/pull/89).
This integrates the activity/reporting work and issues #49, #62, #63 and #64
with the latest main. Earlier PRs #87 and #88 targeted feature branches, so
their merges did not deploy production. Production deployment follows main.

## Behavior and integration

Tenant admins can create templates and attach them to existing shifts. Cleaners
record whichever activities they performed; unchecked items do not block ending
a task and require no explanation. Monthly reports keep each company, branch
and shift separate and count activity days. Tenant admins can download PDFs and
create anonymous report links that expire after 30 days and can be revoked.

Employee invitations use native Keycloak account setup rather than emailing or
resetting passwords. Login bindings, retries, inactive employees and stale roles
are checked. Tenant and platform API audiences are separate; platform operations
require `platform.tenants.manage`. Existing tenant admins gain no platform access.

The integration retains main's multilingual service editor, registration status,
employee shift clock and entertainment game. Clock starts share deterministic
task IDs with dispatch, reject fabricated/future occurrences and normalize all
lookup and creation timestamps to UTC. No feature branch replaces main wholesale.

## Verification before merge

- Integrated backend: 285 passed, 4 conditional external-service tests skipped,
  289 total. Skipped integration checks require disposable PostgreSQL/Keycloak.
- Complete mocked Playwright desktop/mobile suite: 110 passed. The two initial
  CI failures were an obsolete resend label and an incomplete invitation mock;
  the test now uses the actual setup-email label and response contract.
- TypeScript compilation passed.
- The clock creation regression runs with UTC, local and unspecified inputs and
  verifies UTC lookup, UTC evidence and the dispatcher-compatible task ID.

Historical live Keycloak 24/26 and database results are in the feature documents;
they do not establish that this integrated revision has been deployed.

## Production preflight on 2026-10-13

The deployed API before this release was `80e281c`. A private PostgreSQL backup
was successfully restored into an unexposed disposable PostgreSQL container on
the production host. Business data stayed on that host.

Production had no QC forms, two templates in `*DEFAULT*`, and six employees,
including four bound identities missing derived identity keys. There were no
duplicate bound subjects. One template had a unique shift reference proving its
owner. The other was unscoped and unreferenced. The explicit operator decision
is to preserve it under `readysetsiivous`, the sole registered application tenant;
this is an owner decision, not an inferred reference.

The [QC migration](../../api/deploy/quality-cycle-tenant-migration.sql) defaults to
rollback. Rehearsal proved missing-owner rejection, same-ID target collision
rejection, duplicate occurrence rejection, rollback, successful ownership moves,
unchanged IDs/JSON/versions/timestamps and repeat-run idempotence. Existing global
token uniqueness also rejected an attempted duplicate-token rehearsal fixture.
The [identity backfill](../../api/deploy/employee-identity-backfill.sql) preserved
business fields and made no further writes when applied a second time.

Before the application merge, production's existing SPA received the tenant API
audience mapper. A separate confidential, service-only platform operator client
received the platform audience and explicit permission. A subsequent check found
zero configuration differences. Realm/client configurations were backed up
privately and the new client credential remains on the server with restricted
permissions. No employee received a platform role.

Native realm SMTP was configured using the existing Resend credential after
verifying the sender domain and TLS/authentication. No test email was sent.
The public realm frontend URL is `https://auth.readysetsiivous.fi`.

## Remaining release steps

Wait for the final revision's CI/review, stop application writes briefly, take a
fresh backup and apply both migrations explicitly. Verify live counts and
evidence before restarting. Ensure deployed compose/config includes the distinct
audiences and preserves entertainment key/upload volumes. Merge #89 into main,
then verify Pages and API deployment against the actual merge SHA and health.
Record resulting evidence here; close completed issues only after main contains
their fixes. Broader employee read scope (#65) and other audit findings stay open
and start from latest main after this release.

Rollback requires restoring the old application and pre-migration database
together: the old QC repository cannot read the moved tenant partitions. Preserve
private backups; never publish tokens, credentials or database dumps in GitHub.
