# Field-service workflow delivery ledger

Approved design: [design](../superpowers/specs/2026-11-02-field-service-workflows-design.md).
Reusable implementation checklist: [plan](../superpowers/plans/2026-11-02-field-service-workflows-plan.md).

## Current checkpoint

- Baseline: main `b4f7b6260797b3784c0b3de295464d1373fee5fd`.
- Implementation branch: `feat/field-service-workflows`.
- Scope authorised: approved six-stream plan, delivered as **small sequential PRs**, focused Playwright tests in each, review/fix/merge, then saved handoff and context compaction before next chunk.
- First chunk: C01 — server-search company selectors in Agreements/Shifts, preserving selected context and removing the first100 cutoff.
- C01 implemented and verified locally: shared paged server-search company selector in Agreements/Shifts; selected context survives searches and page changes, stale responses are ignored, and shift branches reset when the company changes. PR review, merge and deployment remain separate checkpoints.
- Autoaccept A01 is the next separate chunk after C01. User confirmed exact admin-planned start is required. Employee-owned preference defaults Manual; hide approvals only after confirmed enabling, which is blocked while any pending offers remain. Disabling is future-only. Use atomic Offer/acceptance and the same preference/work-reservation locking; notifications are informational, not approval prompts.

## Per-PR ledger

| Chunk | Branch / PR | Implemented | Verified | Merged | Deployed | Handoff |
| --- | --- | --- | --- | --- | --- | --- |
| C01 | feat/field-service-workflows / [PR #240](https://github.com/MaleeshaKumarage/readysetsiivous/pull/240) | yes | yes (local checks below) | yes (`d849678`, origin/main) | not verified here | checkpoint below |

## Handoff template

Append one checkpoint after every reviewed chunk, before compacting context:

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

### Partner routing and reported operations issues — 2026-11-02

- Current work is uncommitted on `feat/partner-branch-routing`. Partner-only generated property names, optional preferred-cleaner routing and employee-owned partner-request autoaccept are being verified separately from the general A01 mode. Flexible requests still require cleaner approval; exact-start requests may autoaccept when opted in. None of this checkpoint claims deployment.
- C01 is merged: the locally available `origin/main` log records merge `d849678` for PR #240. Earlier pending-merge statements above describe their historical checkpoints. Its production deployment has not been verified in this checkpoint.
- Notification [PR #241](https://github.com/MaleeshaKumarage/readysetsiivous/pull/241) exists separately; merge and deployment are still pending. Actual device receipt and production VAPID activation are not verified here.
- User reports: Partner Operations Edit property / Set cleaning price do not work; non-partner properties must never appear there; a newly created one-time shift is missing from the calendar. Partner property filtering and button-flow implementation/regression verification remain in progress. Do not label these fixes shipped until review, merge and verified deployment.
- Calendar investigation has not reproduced a defect. `ShiftCalendarAdminController.Get` uses the shared roster resolver, including persisted `Extra` visits for ordinary and partner `OnCallFlexible` shifts. Added two controller regression cases in `AdminShiftCalendarTests.One_time_extra_visit_is_visible_without_a_recurring_schedule`, checking the visit and cleaner without a recurring assignment. No calendar production behavior was changed.
- Verification: native .NET `AdminShiftCalendarTests` — **22 passed, 0 failed, 0 skipped**. The subsequent no-build filter `PartnerWorkflowTests|ShiftCalendarTests` — **51 passed, 0 failed, 13 PostgreSQL tests skipped** because the isolated test database is unavailable locally. Existing PostgreSQL coverage includes accepted partner materialization and local-midnight roster visibility; that database execution remains for CI. Commands use `dotnet test`, `-r win-x64`, `-p:SelfContained=false`, `-p:AppendRuntimeIdentifierToOutputPath=false`, `-m:1`, and `-p:UseSharedCompilation=false`.
- Pending clarification: whether the missing visit was created through Admin Shifts or is a partner request, and whether the latter is Requested, Offered or Accepted. Partner requests only materialize a calendar shift on acceptance; do not change this boundary based solely on the report. An affected date/status or reproducible case is still needed. Existing unrelated changes and the recovery stash are preserved; no commit was made for this checkpoint.

### Admin on-call scheduling clarification and fix — 2026-11-02

- User clarified Admin → Shifts: an on-call shift appears on the partner portal but not My shifts or Shift calendar. Confirmed cause: the admin creation form saves a reusable `OnCallFlexible` shift definition, hides time inputs and creates no concrete visit. The partner portal lists definitions; calendars correctly list dated occurrences. There is no appointment time to infer from the saved definition.
- Implemented in the uncommitted routing branch: an explicit **Schedule one-time visit** action for existing active on-call shifts; new on-call creation immediately opens the existing Extra-visit editor. Explanation distinguishes a saved shift from a scheduled visit, cancellation explicitly keeps the unscheduled shift, and success is shown after saving the actual date/time. Existing tenant-zone conversion, visit cleaner selection/inherited shift assignees and occurrence endpoint remain in use. A saved date outside the current occurrence-list range becomes visible immediately. No production records were edited and no date/time was invented.
- Added `shift-dates.spec.ts` desktop/mobile coverage for create on-call → blank date/time editor → cancel without a visit → reopen from existing shift → save one concrete visit with cleaner and UTC conversion → visible visit outside the old date range. Focused run: **2 passed**. Existing shift-dates/shift-occurrences cases: **32 passed**; the initial two new cases failed only due to a required-field test locator, corrected before their successful rerun. TypeScript check and diff whitespace check passed. Backend controller regression evidence from the preceding checkpoint remains applicable; no backend calendar behavior changed.
- Implementation only: review, merge and verified deployment remain pending. Existing on-call definitions still need an administrator to choose a real visit date/time using the new action; this change does not silently schedule historic definitions.

### Partner Operations property scope and editor visibility — 2026-11-02

- Implemented in the uncommitted `feat/partner-branch-routing` work: staff property lists now resolve the current tenant's partner-enabled companies and exclude ordinary customers, companies with partner access removed and orphan properties. Quote queues also require an active partner-enabled company. Enabled but inactive partner companies retain readable property history; pricing still follows the existing active-company validation. Property creation offers active partner companies only. UI property/quote filtering provides the same partner-only scope.
- Edit property and Set cleaning price open inline editors below the property list. Both actions now move keyboard focus and scroll to their editor, including repeated clicks on an already-open editor. The long-list regression opens the first of 25 properties, checks editor focus/viewport visibility, saves an edit and pricing, excludes ordinary/former-partner properties, and checks the partner-only company selector on desktop and mobile. Existing server mutation validation is retained; no production session was available to assert that this was the sole cause of the reported live button failure.
- Browser verification: all-partner run covered 98 cases (93 passed, five failures from stale partner flags/manual-name fixtures). Corrected those fixtures to match the real partner-company contract and address-generated names, preserving their cancellation, manager-only API and editor-reset assertions. Focused rerun of all three affected cases on both desktop and mobile passed **6/6**. All 98 cases therefore have passing evidence for the final relevant code/fixtures; this is not a claim that the original broad run was clean. New filtering/editor regression and all routing cases passed both projects in the broad run.
- Backend verification: new actual-service PostgreSQL regression covers ordinary/former-partner/orphan exclusions, active partner quotes, retained inactive-partner property history and current-state disabling. It compiled successfully in the shared backend verification, but was skipped locally because the isolated PostgreSQL test database is unavailable. The related combined backend run was **51 passed, 13 database tests skipped, zero failures**. Database execution remains a CI requirement. TypeScript and diff whitespace checks passed.
- Implementation and local verification only. No commit, merge, deployment or production data changes were made for this checkpoint. Existing unrelated working changes and the recovery stash remain preserved.
- Final combined production build passed on the current routing/on-call source after the notification follow-up mirror: Next.js 14.2.35 compiled, lint/type checks passed and all 47 static pages exported. This verifies the build artifact locally; it does not deploy it.

### Committed partner routing handoff — 2026-11-02

- Implementation is committed as `f225bc7`; the conflict-free integration of notification parent `ba2b797` is `284c99f0328e42c2e5d26f60be0f03c191e906ce` on `feat/partner-branch-routing`. The integration commit has an identical source tree to the implementation commit, so the successful final production build and focused verification apply to both. Earlier statements that work was uncommitted describe their dated checkpoints above.
- The comparison against parent `ba2b797` contains only partner branch/preferences/routing, Partner Operations filtering/editor visibility, explicit on-call visit scheduling, and their related tests/documentation. Mirrored notification scheduler, delivery/subscription, notification panel/helper, push-test and logout-test changes no longer appear in the child diff. The recovery stash is preserved and untracked `.local/` evidence is excluded from commits.
- Local verification remains: production build with all 47 static pages, TypeScript and diff checks passed; 98 partner cases have passing relevant evidence through the broad run and corrected-fixture rechecks described above; on-call browser coverage passed 2 new plus 32 existing cases; calendar controller tests passed 22 cases; the additional partner/calendar filter passed 51 with 13 PostgreSQL cases skipped locally. Child database integration verification remains a CI gate, not inferred from parent CI.
- Notification PR #241 is the preceding delivery dependency and is not merged at this checkpoint. Push this child branch for durable storage but do not open its PR until #241 merges, then sync with main and run the child review/CI cycle. This checkpoint records committed/local verification state only: no child PR, main merge, deployment, production VAPID activation or real-device notification receipt is claimed.
