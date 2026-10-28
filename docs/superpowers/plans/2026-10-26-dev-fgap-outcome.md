# Development identity implementation outcome

This is the original FGAP checkpoint, not the current completion report. Its
results and limitations below describe that earlier implementation and are
retained as historical evidence. Later work introduced the server extension and
[guarded invitations](../specs/2026-10-26-guarded-invitations-design.md); see the
[later development verification](../../security/2026-10-27-guarded-invitations-verification.json)
for the replacement boundary, supported candidate and tests. The separately
approved [production master-operator account-only setup](../../security/2026-10-27-master-operator-setup-outcome.md)
occurred afterward and does not approve application/provider production rollout.

Implemented on `feat/dev-fgap-identity`, based on6d080a9. No production rollout,
main merge, or release-policy approval has occurred. Existing shared development
services were preserved; verification used separately named synthetic services on
the development host and loopback-only SSH forwarding.

## Implemented and verified

- Fixed setup-owned staff/role groups; no broad user/client administration grant.
- Exact employee ID required at every identity mutation, including setup emails.
  No email-only adoption of existing logins or direct-role fallback.
- Effective group-role checks and explicit legacy-role reconciliation; safe
  membership replacement and session revocation after partial failures and after
  access/activation changes. Passwords are preserved.
- Clear provider failures without exposing upstream response bodies. Real SMTP
  failure/retry and four parallel invitation requests were exercised.
- Group-derived tenant-admin recipients, including the ordinary tenant owner;
  inactive, pending, service and out-of-contract group members are excluded.
- Explicit development-only, read-only-by-default setup; delivered-secret proof,
  unsafe drift rejection and native idempotency. Permission bindings use actual
  sparse GET/relationship representations. Authorization resource `_id` differs
  from the underlying group UUID in `name`; tests model both identities.
- Service-token refresh now uses a bounded proportional margin, so60/300second
  tokens are reused while still valid. Expired tokens are not reused.

Recorded verification in `2026-10-26-dev-fgap-verification.json`:

| Verification | Result |
|---|---|
| Full .NET suite |1003 passed,34 environment-gated skips,0 failed|
| Native PostgreSQL/Keycloak identity tests |2 executed,2 passed,0 skipped|
| Real browser workflow |1 passed,0 skipped,0 flaky|
| Python setup/contract checks |24 passed,2 POSIX-only skips|
| Native setup apply/check/reapply |All3 returned0; check/reapply emitted no mutations|

The browser workflow completed native account setup and login, shift start/end,
manager operations, cross-tenant isolation, reinvitation, role changes, stale-token
rejection and deactivation. No mock authentication or production records were used.
Native cross-network verification used an explicit180second PostgreSQL command
timeout to allow serialized requests over the SSH tunnel; production lock settings
were unchanged. Initial timeout and contract failures were corrected/retested,
not counted as successful runs. NuGet advisory refresh was unavailable; these
test results do not constitute a refreshed dependency vulnerability audit.

Cleanup verified removal of the three uniquely labelled synthetic containers,
their internal network, the owned SSH tunnel and local test web processes. All
existing shared development and audit services remained running. Test context
and diagnostic files remain local under ignored/untracked temporary directories;
native action tokens and raw test transcripts are not part of this checkpoint.

## Adoption gates at this original FGAP checkpoint (historical)

1. Keycloak's hidden privileged-group creation returns500 instead of403. The
   disposable probe verified rollback/no partial account. The application uses
   fixed paths and reports failures, but this is not an upstream fix.
2. Runtime role lists filter roles that the caller cannot view. An experimental
   check for protected administration-role drift therefore failed native testing
   and was removed. Roles do not support a separate FGAP `view` scope in26.8.1;
   its disposable test returned400 and the entire test realm was removed. No
   broader client visibility was granted. Privileged setup detects unsafe managed
   membership, but an operator must remove all staff groups before granting
   Keycloak administration rights. This is a documented trust assumption, not
   independent runtime proof against later operator misconfiguration.
3. The existing coarse-role24/26.1 security gate remains unchanged and failing.
   A supported candidate CI baseline, final policy review, and any rollout require
   separate approval. PRs202/203/206 and the production release hold remain held.

If protection must survive privileged operators leaving administrators in the
managed group, an isolated administration boundary or a different proven policy
model needs design review. This development checkpoint is not approved for
production adoption and does not close that architecture question.
