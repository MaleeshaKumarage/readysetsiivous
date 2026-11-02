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
