# Development identity extension outcome

The application-facing identity boundary now uses a narrow Keycloak extension,
not visibility-filtered Admin REST permissions. Runtime clients have no
realm-management roles or FGAP grants. Tenant transport has no fallback to Admin
REST; platform provisioning remains separate. New provisioning and explicit setup
must verify the authenticated extension before reporting readiness.

The final image passed 38 direct security canaries. Forbidden groups return 403
before user creation, privileged targets are denied after later promotion,
effective composite privileges are detected, searches exclude privileged targets,
and direct Admin REST/client-secret access is denied. Unsupported operations have
no endpoint (404/405); those results are not substituted for failed authorization
on implemented operations.

Two real-service .NET regressions executed and passed on that same final image.
They cover invitations, email failure/retry, parallel requests, action-token
binding, password preservation, role transitions, disable/reactivation, ordinary
owner directory access, dedicated audience and protected-account exclusion.
The full backend run passed 1,009 tests with 34 environmental skips. Python setup
checks passed 37 tests with two POSIX-only skips on Windows. No skipped check is
counted as native verification.

An explicit native migration refused changes without opt-in, removed only the
validated legacy grants/policies with opt-in, disabled FGAP, and proved extension
HTTP200 plus direct Admin REST HTTP403. Both helper and CLI check/apply were
idempotent. One earlier tunnel-based attempt exceeded the administrator token's
lifetime; its isolated container was cleared before the successful host-local
rerun, whose exact realm was deleted with fresh cleanup credentials.

Independent static review found no verified P1/P2 in this bounded checkpoint.
Sanitized counts and exact tested image/source identities are in
[the verification record](2026-10-26-dev-identity-extension-verification.json).
Raw TRX, synthetic tokens, build contexts and probe logs remain untracked.

All three owned test containers, their empty network and the SSH tunnel were
removed. The six pre-existing development/audit services were verified running.
Shared development configuration and production were not changed.

## Remaining production gates

This is not production-ready. Complete per-request model reads solve the proven
steady-state drift defect, but do not establish serialization against concurrent
operator role/group/composite changes. An invitation issued before promotion
also needs an explicit redemption/revocation policy. Keep the production rollout
hold and draft status until these adoption decisions and their native checks are
resolved; do not present ordinary transactions or a local mutex as proof.
