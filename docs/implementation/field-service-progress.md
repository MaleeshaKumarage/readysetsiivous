# Field-service workflow delivery ledger

Approved design: [design](../superpowers/specs/2026-11-02-field-service-workflows-design.md).
Reusable implementation checklist: [plan](../superpowers/plans/2026-11-02-field-service-workflows-plan.md).

## Current checkpoint

- Baseline: C01 merged main `d849678054a5cd7c7089cfe17c08f9b1a78e1c2f`.
- Implementation branch: `feat/employee-auto-accept` (A01 implemented and locally verified; publication/CI pending; C01 is merged and deployed).
- Merge gate: merge only after every CI check for the latest PR head has completed successfully and all actionable review comments are resolved. Then save the handoff and use fresh context for the next chunk.
- Scope authorised: approved six-stream plan, delivered as **small sequential PRs**, focused Playwright tests in each, review/fix/merge, then saved handoff and context compaction before next chunk.
- First chunk: C01 — server-search company selectors in Agreements/Shifts, preserving selected context and removing the first100 cutoff.
- C01 implemented and verified locally: shared paged server-search company selector in Agreements/Shifts; selected context survives searches and page changes, stale responses are ignored, and shift branches reset when the company changes. PR #240 merged; Pages verified for the merged SHA as recorded below.
- Autoaccept A01 is implemented and locally verified after C01; PR CI, merge and deployment remain pending. User confirmed exact admin-planned start is required. Employee-owned preference defaults Manual; hide approvals only after confirmed enabling, which is blocked while any pending offers remain. Disabling is future-only. Use atomic Offer/acceptance and the same preference/work-reservation locking; notifications are informational, not approval prompts.

## Per-PR ledger

| Chunk | Branch / PR | Implemented | Verified | Merged | Deployed | Handoff |
| --- | --- | --- | --- | --- | --- | --- |
| C01 | feat/field-service-workflows / [PR #240](https://github.com/MaleeshaKumarage/readysetsiivous/pull/240) | yes | yes (local checks below) | yes (`d849678`) | yes (Pages run `41435068885`) | checkpoint below |
| A01 | feat/employee-auto-accept / [PR #242](https://github.com/MaleeshaKumarage/readysetsiivous/pull/242) | yes | all 32 A01/PWA database cases passed in CI; backend failed stale roster-contract expectation; focused fix verification below | no | no | active checkpoint below |

## Handoff template

Append one checkpoint after every reviewed chunk, before compacting context. Earlier pending statuses below are historical; the current checkpoint and latest chunk checkpoint take precedence:

- Date; chunk IDs and exact shipped behaviour.
- Branch, commit SHA, PR URL and reviewed head.
- Files/interfaces changed; compatibility/permission notes.
- Commands and test evidence; failures or limitations.
- Review findings and disposition.
- Merge and deployment status (separate; verify live SHA before claiming deployed).
- Outstanding decisions and next dependency-ready chunk.
- Unrelated working-tree files preserved; any active local processes or special test prerequisites.

Never store credentials, customer personal data, production exports or private host configuration here. Machine-local operations references stay in the ignored runbook.

### C01 implementation checkpoint — 2026-11-02

- Implemented the shared bounded CompanyPicker for Agreements and Shifts, including server search, paging beyond the former first-100 cutoff, selected-company hydration, inactive company labels, failure/retry handling and stale-response protection. Shift branch loading is guarded against stale company responses and resets dependent branch selection.
- Added desktop/mobile picker E2E coverage and updated existing admin tests for the searchable selector. Persisted the approved six-stream design and implementation plan; later chunks remain planned.
- Verification passed: 12 new picker desktop/mobile E2E cases (53.2 seconds); 34 existing lifecycle-agreement-email, lifecycle-agreements-employees, lifecycle-shifts, shift-dates and company-integrity cases; 10 admin Agreement/Shift cases; `npx tsc --noEmit --incremental false`; `npm run build` (47 pages).
- E2E commands (from `site`, both default desktop/mobile projects): `npx playwright test lifecycle-company-picker.spec.ts --workers=1`; `npx playwright test lifecycle-agreement-email.spec.ts lifecycle-agreements-employees.spec.ts lifecycle-shifts.spec.ts shift-dates.spec.ts company-integrity.spec.ts --workers=1`; `npx playwright test admin.spec.ts --grep 'Admin Panel - (Agreements|Shifts) Page' --workers=1`.
- Independent code review reported no blockers; specification/documentation review passed. No backend behavior changed in C01.
- Published initial implementation commit `8186e49633af56454a01d4677426532ccaeecc84` on [PR #240](https://github.com/MaleeshaKumarage/readysetsiivous/pull/240). Follow-up review adds explicit `aria-disabled` matching the existing inactive-option policy and covers inactive search results for both shift and agreement creation. Independent final-delta review passed.
- Follow-up verification passed: 16 picker desktop/mobile E2E cases (51.7 seconds), TypeScript check (exit 0), and production build (47 pages). Together with the 44 existing workflow/admin regression cases above, 60 E2E cases passed; those 44 regression cases ran before the final aria-only component delta. PR merge and deployment remain pending. Unrelated untracked workspace files are preserved.
- Next dependency-ready chunk: A01, with exact admin-planned start required and enabling autoaccept blocked while any pending offers remain; retain the atomic Offer/acceptance and shared work-reservation locking boundaries below.
### C01 retained inactive-company follow-up — 2026-11-02

- Follow-up to reviewed head `749113eba37bbb4251af6cdc905e0b22559f908a` on [PR #240](https://github.com/MaleeshaKumarage/readysetsiivous/pull/240): Shifts passes the edited shift's original company ID as a persistent inactive-company exception. One shared predicate controls pointer submission, disabled options and `aria-disabled`, so switching to an active company still allows returning to the original inactive company.
- Added desktop/mobile coverage for inactive company A → active company B → original inactive company A, branch reset/restoration and the saved shift payload. Shift-context coverage also forces a failed load-more request, retries the same offset and verifies paging beyond the first 50 companies.
- Verification passed: 18 current picker desktop/mobile E2E cases (about one minute), TypeScript check, production build (47 pages) and `git diff --check`. Together with the 44 prior workflow/admin regression cases, 62 E2E cases passed; those 44 cases were run before this follow-up delta. Independent fresh review of the retained delta passed.
- The original C01 review/merge gates remain pending; this follow-up does not mark PR #240 merged or deployed. Unrelated untracked files are preserved. A01 remains the next separate chunk after C01 completes its gates.
### Next planned chunk

A01 (employee-owned work autoaccept) follows C01. Start-time question resolved: user confirmed an admin must set the exact planned start. Enabling is blocked while any existing pending offers remain, with an explanation to resolve them first; no silent hiding. Disabling affects future offers only. Use self-owned work-preference endpoints, one atomic server Offer/acceptance commit and the same work-reservation locking for Offer and preference changes. Only informational notifications are sent, not approval-prompt email. These boundaries are authorised for implementation. This documentation update does not implement app behaviour.

### C01 merge / A01 start checkpoint - 2026-11-02

- C01 PR #240 merged to main as `d849678054a5cd7c7089cfe17c08f9b1a78e1c2f`; CI all green and three review threads resolved. Local evidence is 62 UI cases with the 44 prior regression-case caveat above, TypeScript and production build. GitHub Pages run `41435068885` succeeded for this exact merged SHA; public `/admin/shifts/` and `/admin/agreements/` routes returned 200. This proves availability only; authenticated live workflow was not tested.
- A01 implementation in progress on `feat/employee-auto-accept`, based on that merged main. Scope is employee-owned consent, exact admin-planned start, shared work reservation and atomic acceptance, and informational notifications. Existing unrelated untracked files are preserved.

### A01 backend local verification - 2026-11-02 (in progress)

- Implemented default-false consent, strict own GET/PUT work preferences, explicit admin/manager operational metadata, reservation-serialized preference/Offer decisions, exact-time automatic acceptance through one lock-held materialization routine and informational employee notifications guarded by current acceptance/revision. Generic employee fields cannot modify consent.
- Focused .NET command: `<DOTNET_10> test api/CleaningSuite.Tests --no-restore --filter 'FullyQualifiedName~PartnerWorkflowTests|FullyQualifiedName~SelfWorkTests|FullyQualifiedName~AdminResponseContractTests|FullyQualifiedName~PartnerNotificationTests|FullyQualifiedName~ManagerOperationsTests|FullyQualifiedName~EmployeeWorkPreferenceTests' --verbosity minimal`. `<DOTNET_10>` denotes a portable .NET SDK executable matching `global.json`. The final completed run passed 64, failed 0, skipped 12 (76 total); build succeeded with existing warnings and the final scoped diff check passed.
- `QC_TEST_CONNECTION_STRING` is absent locally. Documentation (`docs/environments.md`, `docs/quality-cycle-rollout.md`) requires an isolated synthetic PostgreSQL database; no production connection was used. The 12 skipped persistence tests include five new A01 atomicity/preference-race scenarios and seven existing workflow regressions. CI's disposable PostgreSQL16 must execute these before merge; local skips are not database verification.
- Backend verification completed locally; final review and browser evidence are recorded in the checkpoint below. This entry does not establish real-database verification or merge/deployment.

### A01 implementation / publication checkpoint — 2026-11-02

- A01 is implemented and locally verified: Manual remains the historical/new default; only the authenticated active linked work identity owns GET/PUT work preferences. Staff operational metadata exposes consent through explicit allowlists without expanding HR access. Account saves confirmed preference state; My shifts retains pending approvals until successful enabling, and staff Offer UI explains exact timing and automatic outcomes.
- Offer and preference writes share the work-reservation lock; enabling is blocked by pending offers. Automatic acceptance requires an exact admin-planned or staff-confirmed fixed appointment and uses the same lock-held atomic materialization as manual acceptance. Informational notifications replace approval prompts only for automatic acceptance. Disabling affects future offers only.
- Final local backend result: 64 passed, 12 skipped, 0 failed. Five new and seven existing persistence cases skipped because `QC_TEST_CONNECTION_STRING` is absent. CI must execute these against its isolated disposable PostgreSQL 16 before merge; no local database verification is claimed.
- Final browser result: 102 passed across desktop/mobile, including focused draft/focus and in-flight preference-save regression coverage. Browser APIs are mocked; these checks do not prove live backend integration. TypeScript passed (exit 0); production build passed (47 pages).
- Independent backend/security review passed; final independent UI review passed after scoped findings were fixed, including retained draft/focus and in-flight save handling. No Keycloak, deployment configuration or rate changes are included.
- Branch: `feat/employee-auto-accept`, based on merged C01 `d849678`. Publication and latest-head CI are pending; A01 is not merged or deployed. Merge only after every latest-head CI check succeeds and all actionable comments are resolved. Preserve unrelated untracked workspace files.
- C01 remains merged/deployed as separately recorded above; its public-route checks establish availability only. All other roadmap chunks remain planned. After A01 completes its gates, save its handoff and use fresh context for the next dependency-ready chunk.

### A01 main integration / local checkpoint — 2026-11-02

- PR: [#242](https://github.com/MaleeshaKumarage/readysetsiivous/pull/242), branch `feat/employee-auto-accept`. Local integration head `6e3a4bab54bd71ff2b5075f3d51c0daed63fc0e6` merges main `3e527` (including #241 employee PWA and #239 helper changes); the previously published PR head remains `37c6db8` at this checkpoint. The follow-up changes recorded here are local and have not been pushed.
- Retained the employee PWA notifications panel and push permission controls independently of automatic-acceptance consent. An automatic recipient receives one current-revision informational `WorkAutoAccepted` push intent; suppress the generic schedule-change intent for that recipient while retaining manual and other-employee notifications. Database coverage checks the actual outbox contains exactly one expected manual or automatic offer intent and retains delivery eligibility coverage.
- Verified the integrated working tree: backend 109 passed, 32 skipped, 0 failed (141 total); 162 unique combined A01/PWA desktop/mobile browser cases passed. The strengthened notifications-panel case passed a further two desktop/mobile reruns, not two additional unique cases. Browser APIs are mocked and do not prove live backend integration.
- TypeScript passed (exit 0), production build passed (47 pages), scoped diff checks passed and UTF-8 inspection found no replacement characters. Fresh independent integration review passed after the duplicate-push fix.
- The 32 database cases skipped locally because the isolated database connection is unavailable. Latest-head CI must execute these against its disposable synthetic PostgreSQL database before merge; no local database verification is claimed. Publication of this integration/follow-up, latest-head CI, review-comment resolution, merge and deployment remain pending. Merge only after every latest-head CI check succeeds and all actionable comments are resolved.
- Unrelated untracked workspace files are preserved. This checkpoint authorizes no merge or deployment and does not change the remaining roadmap scope.

### A01 roster-contract CI correction - 2026-11-02

- Published PR #242 head `0a42b907` executed and passed all 32 A01/PWA Quality Cycle database cases against CI's isolated synthetic database. The overall backend job still failed: 1 failed, 1272 passed, 2 configured native skips. The failure was `EmployeeReadScopeTests.SignedManagerGetsOnlyOperationalRosterFields`, whose exact key allowlist omitted the intentional `autoAcceptWork` operational metadata required by the approved A01 spec and existing explicit DTO.
- Corrected only the test's exact roster allowlist and added an assertion that its JSON metadata defaults to false. Existing HR, pay and identity exclusion assertions remain intact; existing response-contract coverage also verifies enabled metadata. No application code changed.
- This correction is prepared locally for publication. PR #242 remains unmerged and undeployed; the corrected head must pass the overall backend job and remaining latest-head gates before merge. Earlier local database skips remain local skips; the CI execution above is the database evidence.
- Focused local verification using portable .NET SDK 10.0.402 and explicit `api/CleaningSuite.Tests/CleaningSuite.Tests.csproj`: EmployeeReadScopeTests, AdminResponseContractTests and ManagerOperationsTests passed 51/51, with 0 failures and 0 skips. No local database connection was used. Existing compiler warnings remain; no frontend or production build was run for this test-only correction.

### A01 review coverage follow-up - 2026-11-03

- Added regression coverage for PR #242 review threads `PRRT_kwDOTuUlNc6y4e1P` and `PRRT_kwDOTuUlNc6y4e2y`. An active linked employee with `identitySyncPending` has no automatic-acceptance checkbox and makes zero work-preference requests. A multiday request with an exact clock time still requires an explicit planned start; submitting without it makes no offer request, while an explicit start on the second day succeeds.
- Fresh focused verification passed all 14 A01 desktop/mobile cases in `employee-invite.spec.ts` and `partner-operations.spec.ts`; TypeScript, scoped diff and UTF-8 checks passed. Independent review of this test-only delta passed. These cases overlap prior browser evidence and are not additional unique cases. APIs are mocked; no live backend integration is claimed. Unchanged tests were not rerun for this documentation/commit step.
- No application behavior changed. This follow-up is local and has not been pushed; PR #242 remains unmerged and undeployed. Every latest-head CI check must pass and all actionable review comments must be resolved before merge. Preserve unrelated untracked files.

### A01 failed-run fixture corrections and publication - 2026-11-03

- Run `41532311363` failed on two outdated test assumptions: the manager roster allowlist omitted intentional `autoAcceptWork` metadata (corrected in `f678ad2`), and the cleaner offer-recovery browser fixture omitted the work-preferences GET response now required to render manual offers. Added an explicit `{ autoAcceptWork: false }` fixture response; no production behavior changed.
- Fresh focused verification passed EmployeeReadScopeTests 40/40 with no skips, the exact cleaner failed-read/stale-acceptance case on desktop and mobile 2/2 with retries disabled, TypeScript `--noEmit`, and `git diff --check`. Browser APIs are mocked. These overlap earlier coverage and are not additional unique cases.
- Publishing these corrections together with existing review-coverage commit `cbb958f` starts fresh PR #242 CI; latest-head CI is not yet verified. No failed workflow is manually retried, and no merge or deployment is authorized by this checkpoint. Unrelated untracked files remain preserved.

### A01 critical review corrections - 2026-11-03

- Addressed review threads `PRRT_kwDOTuUlNc6y-O82` and `PRRT_kwDOTuUlNc6y-O9c`: only fixed single-day appointments infer an automatic start or enforce a fixed clock. Flexible/legacy multiday requests require an explicit planned date for automatic acceptance; full-window and duration validation remains in place. Manual offers remain visible and actionable while work preferences are loading or unavailable, with warning/retry retained; confirmed automatic acceptance still hides them.
- Added actual-service database regressions for rejected implicit multiday acceptance with no state/materialization/notification changes or push calls, explicit second-day acceptance, and strict fixed single-day clock/fallback behavior. Native focused checks passed 17 tests; 14 database cases skipped locally and require fresh CI PostgreSQL execution. TypeScript and diff checks passed. All six focused desktop/mobile cases passed with retries disabled, including persistent-preference-outage acceptance, confirmed-consent hiding, and draft/in-flight preservation.
- Root reviewed the production and regression delta before publication. Latest-head CI and fresh review remain pending; no manual workflow retry was requested. Merge is authorized only after every latest-head required check passes and no critical review issue remains. Unrelated untracked files are preserved.

### A01 cleaner-offer CI fixture correction - 2026-11-03

- Published-head frontend CI passed 794 cases and failed the same cleaner-offer recovery case on desktop and mobile. Its fixture omitted the work-preference response required before My shifts mounts manual offers. Added an explicit `autoAcceptWork: false` response to that scenario; Manual remains the default and no application behavior changed. Existing failed-read, stale-version and exactly two explicit acceptance-write assertions remain intact. Independent review passed.
- Focused verification of the exact CI-failing scenario passed 2/2 desktop/mobile cases. Broader local `partner-owner.spec.ts` runs were not clean: 29 passed/1 failed, then 25 passed/5 failed, with manager navigation timeouts and later Next development-server route/export errors. These local limits are retained as evidence; no full-suite success is claimed. APIs are mocked. Scoped diff checks passed; no production build was rerun.
- This correction is local pending publication. PR #242 remains unmerged and undeployed; every latest-head CI check and actionable review-comment gate must pass before merge.

### Focused Partner Operations / on-call repairs — 2026-11-03

- Prepared `fix/partner-properties-oncall` from main `da21a57` after the user confirmed the saved branch's property/calendar fixes were still absent from production. This isolates the two reported issues and editor visibility from preserved `feat/partner-branch-routing` (`62ec6a6`); it does not bring preferred-cleaner routing, generated titles, size metadata or separate acceptance preferences into this repair. The remaining approved work stays tracked in the [delivery inventory](request-delivery-status.md).
- Staff partner property and quote queries now exclude ordinary/former-partner companies and orphan branches. Enabled but inactive partner property history remains readable server-side; new quote queues require active partner companies. Manager company metadata exposes the partner flag and the UI restricts property/quote results and creation choices accordingly. Edit property and Set cleaning price focus/reveal their inline editors, using instant scrolling to override global smooth scrolling and make repeated mobile actions reliable.
- On-call creation now opens the existing dated Extra-visit editor; existing on-call definitions expose Schedule one-time visit. Date/time and cleaner selection reuse the current occurrence API and tenant time zone. Saving outside the visible range brings the saved date into view. Cancelling leaves an unscheduled definition; existing definitions are not assigned invented appointments. Merged A01 acceptance guards and company pickers are retained.
- Native focused verification: 104 passed, 15 database tests skipped, zero failures (119 selected). This includes manager metadata and ordinary/partner on-call calendar cases; the new actual-service partner-list isolation test compiled but requires CI's isolated PostgreSQL execution. No production database was used.
- Browser verification covered 140 desktop/mobile cases across partner workflows, company pickers, shift dates and occurrences. Initial broad run: 138 passed, two failures in the new long-list case (one locator adaptation and one mobile smooth-scroll race). After correcting the selector and instant-scroll behavior, the complete affected Partner Operations/cancellation suites passed 18/18, including repeated edit/pricing actions and saves. All other 122 cases passed in the broad run, including the new dated on-call flow on both projects. TypeScript and diff checks passed. Independent root review found no blocking issue in the scoped source changes; instant scrolling follows the existing partner portal pattern.
- Production push configuration has separately been prepared in protected deployment storage for the next approved release; the running API was disabled at its last check. This source change keeps repository defaults disabled. Runtime activation/availability and real-device receipt still require verification after deployment. No secret values or private deployment paths are recorded here.
- Publication, latest-head CI/database checks, merge and production verification remain distinct next steps. No workflow has been manually retried and no production records were changed by this repair.
- Final production build passed on the scoped repair: Next.js 14.2.35 compiled, lint/type validation passed and all 47 static pages exported. This is local artifact verification, not deployment.

### W01 work queue checkpoint - 2026-11-03

- Branch `feat/work-queue-w01` from main f23ac0d. Adds admin-only `GET /api/v1/admin/work-queue` (view, source, q, skip, take capped at 100) with per-view counts, and an admin Work queue page with view tabs, source and search filters, URL state and a booking drawer with existing confirm, cancel and complete actions.
- View rules: bookings New or unassigned Confirmed = Needs action; assigned Confirmed = Scheduled; Completed and Cancelled = Completed. Partner requests Requested (Unassigned) and Offered (Awaiting acceptance) = Needs action; Accepted = Scheduled; Cancelled = Completed. In progress is empty: no stored state yet. Partner in-progress/completed derivation from shift and form state is deferred.
- Deferred from W01: manager access (bookings endpoints not verified for manager policy), date and assignee filters, assign and unassign controls, shift occurrences in unassigned work, and partner request actions (remain on Partner requests page).
- Verification: Rules unit tests 17 passed; tsc exit 0; Playwright lifecycle-work-queue 6 passed (desktop and mobile); admin.spec and lifecycle-companies-services 50 passed. Backend read paging is covered by reasoning and rules tests only; no database test yet, so the merged query is unverified against PostgreSQL. Latest-head CI pending.

### W02 shift workspace checkpoint - 2026-11-03

- Branch `feat/shift-workspace-w02` from main `a9b6fb2`. Scope from #245 W02 only: compact admin shift rows, one primary Manage action, a More actions menu with the existing actions under unchanged labels, and a per-shift workspace.
- Workspace sections: Schedule, Assignees, Visits, Checklist and Pricing. Each section opens the existing editor, panel or dialog. Workspace actions close the workspace first, so each existing dialog opens alone.
- Next step text is derived only from fields the shift list already returns: active or inactive, completed one-time visit, on-call type, and assignment count.
- Status column shows Active, Completed or Inactive from the existing lifecycle classifier.
- Visit scopes (This visit, Recurring, From date) are unchanged and remain in VisitAssigneesDialog.

#### Deviations from #245 W02

- Request, awaiting acceptance, accepted, in progress and completed states are not shown. The shift and occurrence DTOs carry no offer, acceptance or progress state. Adding them needs backend work tracked separately.
- Next actor is limited to admin or cleaner wording from the fields above. It does not name a specific person.

#### Verification

- Baseline before edits on the unchanged worktree: 114 of 114 desktop and mobile cases passed across the shift specs.
- Specs routed through the new `e2e/shift-actions.ts` helpers: lifecycle-shifts, lifecycle-one-time-shifts, lifecycle-shift-assignment-count, lifecycle-shift-completed, shift-assignment, shift-dates, shift-occurrences, shift-reactivation, admin-edit-inactive, lifecycle-invoice-billing, quality-cycle, admin.spec.
- New `e2e/shift-workspace.spec.ts` covers row actions, workspace sections, workspace-to-dialog handoffs, on-call and inactive next steps, and phone overflow.

### C03 customer workspace checkpoint (#137 part 1) - 2026-11-04

- Branch `feat/customer-lifecycle-c03` from main `93753c4f` (PR #274). Design: `docs/superpowers/specs/2026-11-04-c03-design.md`; edge-case matrix: `docs/implementation/edge-cases/C03.md`.
- Shipped: static route `/admin/companies/customer/?id=&section=&back=` with Overview, Properties, Agreements, Work and Invoices tabs (lazy, per-tab errors and retry, stale-response guard). Companies list rows gain an Open link. Agreements and Shifts pages preselect `?company=`. Agreement list rows show the customer name.
- Backend: `ListAgreementSummariesQuery` (company filter, customer name), `ListInvoicesQuery` company filter, optional `companyId` on `GET /api/v1/admin/agreements` and `GET /api/v1/admin/invoices`.
- Deferred to PR 2 of #137: agreement service metadata, renewal/amendment lineage, expiry filters and alerts. Also deferred: Expenses (E01), reports/public links and incidents (no company key or model), current/upcoming shift classification (tenant timezone rule), manager access, and the "Customers" nav label rename.
- Verification: backend full suite 1346 passed, 0 failed, 4 skipped (with `QC_TEST_CONNECTION_STRING` against the local PostgreSQL 16 container; includes the new DB tenant-isolation test). `npx tsc --noEmit` exit 0. `npm run build` compiled, 49 static pages. Playwright `lifecycle-customer-workspace.spec.ts`: 55 passed, 1 skipped (desktop phone-width case by design). Regression run of companies/admin/agreements/shifts specs: 245 passed, plus one admin assertion made exact after the new Open control, rerun 2/2.
### W03 completion review checkpoint - 2026-11-04

- Branch feat/completion-review-w03 from main 1fed3af (W02 PR #256). Scope from #245 W03 only: server-computed review flags on the existing admin shift calendar for past visits.
- Missing completion: a past, eligible visit whose assignee has no submitted record (includes started but not ended). Unusual duration: a submitted record whose duration is below 0.5x or above 1.5x the scheduled duration (CompletionReview constants; a product threshold, not a settled rule). A reasoned correction whose times equal the current actual times clears the flag. Flags never change times automatically.
- Eligibility uses the same boundary as correction (past day, not cancelled, not ambiguous, not partner, active work-role employee), so every flag has an action and today/future never flag. Inactive employees and deleted-source records are not flagged or changed.
- The calendar shows Scheduled and Actual labels apart, a per-day count of visits needing review, a Needs-review filter, and the reason in the correction dialog. A lost save response now says the save may have reached the server and asks for a reload instead of showing a raw network error.
- Backend: 13 new AdminShiftCalendarTests cases (classification theory, past/future flags, unusual duration cleared by correction, inactive employee, lost-response stale-version retry). Full backend suite: 1257 passed, 0 failed, 89 skipped (database tests skipped locally; CI executes them).
- Frontend: lifecycle-admin-calendar.spec.ts 32 cases (16 tests on chromium-desktop and mobile-touch, including the new review, unusual-duration and lost-response cases). Regression: my-shifts, my-shifts-one-time, my-shifts-calendar, quality-cycle, shift-workspace, lifecycle-shifts and shift-occurrences, 174 passed. TypeScript passed.
- Open product decisions: the duration thresholds above; review is per day, not a cross-date queue, so D01 still needs a bounded server count for missing completions. The checklist is unchanged: unchecked items still finish a visit (existing quality-cycle.spec coverage).
- Merge and deployment status are recorded separately after PR CI completes.
- Review threads on PR 259 (2, both on the per-day summary): added E2E cases for the two-flagged plural summary and the zero-flag day with the filter-empty message, both projects (34 calendar cases passed, 17 tests x 2 projects). Both threads were addressed by the fix push; resolving them was refused by the permission gate, so they remain open until resolved. A third thread (Marten eager-load of WorkTimeCorrections) is open: Marten stores the form as one JSON document, so the list loads with the document; a database-backed test is still to be added. No behaviour changed in this commit.

### Inventory partner acknowledgment — local implementation, 2026-11-09

- User-requested feature, separate from the unfinished W/C/P/E/B programme chunks. Branch `feat/inventory-partner-acknowledgment`, initially based on main `4909a55`, then rebased onto current main `4a3c20f` for local live verification and PR publication. Product design, API, access rules and first-increment boundaries: [inventory acknowledgment](../features/inventory-partner-acknowledgment.md).
- Admins select 1–50 inventory entries, choose an active partner company, review the shared notes/photos and explicitly confirm disclosure. Partner FI/EN portal has an in-portal notification, pending inbox, photo review, per-entry acknowledgment/issue response and response history. Admins inspect company/response/actor/timestamps from log statuses. Employee inventory records are unchanged.
- Tenant-scoped immutable snapshots use the original usage ID to prevent duplicate requests. Shared tenant reservation and one transaction protect all-or-nothing batches and concurrent submissions. Request/response retries preserve audit; partner list, response and photo access revalidate company/profile scope. Existing photo retention remains three months. New schema registration is included; no production schema change has run.
- Backend verification: all 32 inventory tests passed, zero skipped, including eight new PostgreSQL tests for transactions/concurrency, retries/stale versions, company/tenant/photo boundaries, private employee identity, bounded case-insensitive partner search, pagination/counts and retained history after photo purge. Tests used an isolated synthetic local database, removed afterward. Final API build passed with zero warnings/errors; TypeScript and diff checks passed.
- Browser verification: new acknowledgment suite passed 12/12 desktop/mobile cases. Broader regression run passed 96/106; all ten failures were existing `inventory-admin.spec.ts` substring cell locators also matching the new accessible selection-checkbox cell. Assertions now use exact cell names. The complete corrected admin/new-feature rerun passed 30/30 on desktop/mobile; all 106 selected scenarios have passed across these runs. Browser tests use API fixtures; the backend tests exercise real PostgreSQL. Admin/partner screenshots were captured and visually reviewed at both widths. Production frontend build passed, including lint/type validation and all 53 static pages.
- Live verification after rebase: real local API + PostgreSQL 16 + private disk uploads + Next frontend passed the complete employee/admin/partner workflow on desktop and Pixel 5. Actual photo uploads/downloads, bulk and single-entry requests, acknowledgment, issue validation, history, admin audit, original-record preservation, role/company negatives, atomic invalid batches, duplicate conflicts, stale versions and idempotent retries passed. Browser runtime errors: zero. The fixture supplied test-only RSA-signed identities validated by normal API authentication; API replies were not mocked. Genuine Keycloak login/PKCE was not exercised. All 32 backend inventory tests passed again, zero skipped. See the linked feature document for the verification scope.
- Implementation received a local source review. Published [PR #358](https://github.com/MaleeshaKumarage/readysetsiivous/pull/358) against main after live verification; the production frontend build passed again on the rebased branch. GitHub CI/review are pending; the PR is open, unmerged and undeployed. No shared-development/production data, notification settings or release configuration changed. First-increment limits: in-portal notifications on load/refresh; one company/request per inventory entry; no cancellation/reassignment/resend, email/push reminders or automatic issue resolution.
