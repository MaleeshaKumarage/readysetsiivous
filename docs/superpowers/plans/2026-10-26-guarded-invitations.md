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
preflight without altering tenant employees. The user subsequently selected
existing production HTTPS for dedicated master-operator account and SMTP setup
only. That scoped setup and first-login completion were verified separately;
production application/provider deployment remains held.

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
for exact artifacts, counts, cleanup and limits. That test record predates real
operator setup and correctly records that no real administrator or setup email
existed at that checkpoint. It is retained unchanged as historical evidence.

## Completed account-only follow-up (2026-10-27)

After explicit production-HTTPS target approval, the reviewed operator helper
configured master SMTP with the existing provider and created the separate master
operator. Native setup-email delivery was accepted; the user confirmed completion.
Read-only verification then found enabled/master-admin status, verified email,
configured password and OTP, and no remaining required actions. No application or
provider image was deployed. See the [sanitized account-only outcome](../../security/2026-10-27-master-operator-setup-outcome.md).
Production application/provider adoption, multi-node behavior and the release hold
remain separate gates.

## PostgreSQL persistence follow-up (2026-10-27)

The exact reviewed normal and separate fault images passed 40 API canaries,
139 invitation/MFA checks and 10 native credential-rollback checks on PostgreSQL
16.15. All owned containers/network were removed; six existing development
services remained running. [Sanitized evidence](../../security/2026-10-27-postgresql-identity-verification.json)
records the database proof and limits. The regular security workflow now uses a
separate PostgreSQL database for Keycloak; no provider source was changed.
