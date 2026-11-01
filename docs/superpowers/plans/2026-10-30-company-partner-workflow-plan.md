# Partner workflow implementation

## Primary goal

An invited property owner can request cleaning only for their company's priced branches, see when a cleaner accepts and finishes, and have completed visits included in a staff-generated monthly invoice.

Approved design: `../specs/2026-10-30-company-partner-workflow-design.md`.

## Phase 0 — Documentation discovery

**How this serves the primary goal:** Reuse real application contracts and preserve established identity/work/billing guarantees.

- [x] Read identity/profile commands, fixed Keycloak groups, guarded extension, setup/native canaries and current API role policies.
- [x] Read CompanyApp/repositories, Marten tenancy/session behavior, scheduling reservation, one-time occurrence and work-form patterns.
- [x] Read employee/company UI, manager/account access verification, My Shifts and invoice preview/issue paths.
- Exact shared wire contract is maintained in `docs/features/partner-api-contract.md` by the core workflow implementation.
- Guards: no invented APIs, no sequential partial acceptance commits, no role escalation, no public-site layout changes.

## Phase 1 — Identity and company boundary

**How this serves the primary goal:** A partner account can enter only its enabled company's portal and cannot become staff through another route.

- [x] Extend existing profile, validation, invitation/reactivation, response projection, guarded Keycloak role/group setup and native canaries.
- [x] Add only the individual partner policy; protect current database identity/company state and deny mixed application roles.
- [x] Exclude partners from every booking/shift/occurrence work assignment path; preserve general admin people listing.
- Verification: focused profile/access/invitation tests; existing fixed-group drift/security regressions; native extension canaries before production.

## Phase 2 — Branch pricing and atomic requests/offers

**How this serves the primary goal:** Properties become ready for reusable-priced requests, and cleaner acceptance reliably becomes one scheduled visit.

- [x] Add company enablement, optional room/bed fields, staff pricing/duration readiness and quote/change queues.
- [x] Implement company-scoped partner APIs and narrowly scoped admin/manager operations.
- [x] Implement flexible/fixed request validation, price snapshots, offer revisions, first-acceptance editing lock and atomic one-time materialization.
- [x] Protect linked-shift rescheduling/deletion/assignment through existing entry points and expose real work status without requiring a checklist.
- [x] Write durable notification intents with business changes; no synchronous email before commit.
- Verification: domain/application tests plus real Marten tenant and race tests; repeat accept/cancel/edit and stale-price/binding scenarios.

## Phase 3 — Portal and staff/cleaner interfaces

**How this serves the primary goal:** Owners, staff and cleaners can complete the workflow using the existing authenticated interface patterns.

- [x] Extend reused employee invitation/company/branch forms and role-aware account navigation.
- [x] Add verified partner properties, requests and read-only company work views; staff quotes/offers/change queues; pending cleaner offers in My Shifts.
- [x] Implement clear loading, empty, error and uncertain-write states without blind mutation retries.
- [x] Preserve existing actual-work controls, public routes and ordinary staff workflows.
- Verification: TypeScript, production export, desktop/mobile functional and negative-access Playwright flows.

## Phase 4 — Delivery and monthly invoicing

**How this serves the primary goal:** Participants receive committed-event notifications, and completed fixed-price work can be billed without omissions or duplicate charges.

- [x] Dispatch transactional partner outbox intents through idempotent Resend delivery with bounded retries and current recipient/lifecycle checks.
- [x] Add manual company/month partner selection to existing preview/issue services and UI, enumerating all completed unbilled visits beyond 50 shifts.
- [x] Reuse frozen per-occurrence pricing, existing completion predicate, issue locking, invoice numbering, PDF and barcode output.
- Verification: transport failure/retry/idempotency, pricing history, cancelled/incomplete exclusion, stale preview and concurrent issue tests.

## Phase 5 — Independent verification and review

**How this serves the primary goal:** The new role works end to end without weakening staff boundaries or existing customer/work history.

- [x] Independent authorization/concurrency review with source-backed findings and fixes.
- [x] Run focused suites and the broader backend/frontend checks; run native development identity probes before any production rollout. GitHub CI follows PR creation.
- [ ] Commit/push only verified changes, create and attach a reviewable PR with exact test evidence and known native/deployment gaps.
- [x] Update documentation/contract and release compatibility; do not deploy or claim production readiness before the development identity gate passes.

## Verification checkpoint — 1 November 2026

Implementation phases 1–4 are complete. Independent backend and UI reviews identified
and corrected reservation races, staff-to-partner work retention, per-recipient email
failure handling, cross-record form state and owner visibility of staff resolutions.
The latest frontend production export passed all 46 pages. The broader backend run
passed 1,096 tests with four environment-specific skips; separately configured native and
manager checks are recorded with exact scope. GitHub delivery remains pending at this
checkpoint; completion is recorded in the
[verification and rollout record](../../features/partner-workflow-verification.md).

Production has not changed. Native development evidence is recorded separately in
[the exact-source identity verification](../../security/2026-11-01-partner-native-verification.json).
