# Main integration release: activity reporting and identity fixes

Release PR: [#89](https://github.com/MaleeshaKumarage/readysetsiivous/pull/89), merged
into main at `8ae2d9cda388bea0cd1df6c305b096e43e7dcbc6` on 2026-10-13.
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

- Integrated backend: all 289 passed, zero skipped, with disposable PostgreSQL,
  Keycloak 26.1 and captured SMTP enabled. Before these services were enabled,
  the ordinary local run passed 285 and explicitly skipped four integration tests.
- Complete mocked Playwright desktop/mobile suite: 110 passed. The two initial
  CI failures were an obsolete resend label and an incomplete invitation mock;
  the test now uses the actual setup-email label and response contract.
- TypeScript compilation passed.
- The clock creation regression runs with UTC, local and unspecified inputs and
  verifies UTC lookup, UTC evidence and the dispatcher-compatible task ID.

Historical live Keycloak 24/26 and database results are in the feature documents.
The integrated release's four conditional tests were rerun against separate QC,
identity and audience databases. All owned disposable services were removed
after verification; no test message was sent to a real recipient.

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

## Live migration and merge

After successful final CI and automated review, the API was stopped briefly to
pause writes, a fresh private database backup was taken, and both migrations
were applied explicitly. Two templates moved to `readysetsiivous` and all six
employee identity keys were backfilled. The checks compared template IDs, JSON,
versions and timestamps, plus employee business fields, before and after the
transactions; they were unchanged. No default-partition templates remain.
Repeated dry runs passed and the API restarted. Private backups were retained.

The deployed compose configuration now includes the separate API audiences and
the persistent entertainment key volume. The old container's existing encryption
key was privately backed up and copied to that volume before replacement, so
stored game credentials do not lose their encryption key during deployment.
Unrelated deployment settings and upload volumes were preserved.
The underlying deployment workflow does not synchronize reviewed compose changes
automatically. That newly discovered configuration-drift problem is tracked
separately in [#90](https://github.com/MaleeshaKumarage/readysetsiivous/issues/90).

PR #89 merged after all checks passed and review reported no critical issues.
Issues #49, #62, #63 and #64 closed through that main merge. The GitHub Pages
deployment succeeded; a public fetch confirmed that the published employee UI
bundle contains the released setup-email flow.

## Remaining release verification

Verify the API image against the actual merge SHA, health and genuine platform
versus tenant authorization boundaries after its build/deployment pipeline
completes. Broader employee read scope (#65) and other audit findings stay open
and start from latest main after this release.

Rollback requires restoring the old application and pre-migration database
together: the old QC repository cannot read the moved tenant partitions. Preserve
private backups; never publish tokens, credentials or database dumps in GitHub.
