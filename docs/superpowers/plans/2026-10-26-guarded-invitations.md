# Guarded invitation implementation

## Primary goal

Employee setup links must stop working when their account or access changes, even
if the password form is already open, while infrastructure administrators remain
separate from employee accounts.

## Phase 0: Pinned API discovery

**How this serves the primary goal:** protect the actual email/password writes,
not only the visible form or initial email request.

Read Keycloak 26.8.1 ExecuteActionsActionTokenHandler, UpdatePassword,
VerifyEmailActionTokenHandler, RequiredActionContext, DefaultActionToken,
SingleUseObjectProvider and provider-selection code. Confirm exact note APIs,
native auto-verification, create/factory overrides and old-session markers.
No invented APIs or assumed native-token revocation by logout alone.

## Phase 1: Guarded token and required actions

**How this serves the primary goal:** enforce current eligibility at redemption
and before storage, with single-use tickets and safe retries.

Implement the approved design in the existing provider project. Factor complete
eligibility checks for reuse without requiring a runtime service token in browser
handlers. Pin and compile all new SPIs, confirm registration and native-provider
override priority, and preserve master/unrelated realm behavior.

## Phase 2: Native regressions and operating model

**How this serves the primary goal:** prove old links/open forms fail safely and
the separate administrator model preserves real employee and owner workflows.

Add native HTTP/browser regressions with synthetic identities; compare credentials
and email verification after rejection. Verify explicit master-only operator
preflight without altering tenant employees. Real development first-login setup
depends on the pending HTTPS environment choice and protected SMTP setup;
email delivery itself is approved. Production remains untouched.

## Phase 3: Review, push and CI

**How this serves the primary goal:** make the completed safeguards reviewable and
repeatable without hiding unverified operating assumptions.

Run focused and backend/setup checks as needed, independently review the final
diff, record sanitized evidence and cleanup, then commit/push only approved files.
Keep deployment and any unresolved delivery/multi-node constraints explicit.

## Verified development checkpoint (2026-10-27)

Implementation and independent source review are complete with no outstanding
P1/P2 findings in the reviewed change. Normal-image native regressions, separate
credential-rollback fault tests, setup tests and the backend suite passed.
See [the sanitized verification record](../../security/2026-10-27-guarded-invitations-verification.json)
for exact artifacts, counts, cleanup and limits. The operator tool is ready;
no real administrator was created and no setup email was sent. The HTTPS
environment choice and production deployment remain separate gates.
