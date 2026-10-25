# Security remediation ledger

Goal: fix all issues found in the ReadySetSiivous audit and infrastructure follow-up. This is not a claim that all historical product backlog issues are part of this audit.
Baseline checked: main a57c6bdc2c1e0361d8bf7dcb2430e50f303382c5, 2026-10-24.
Source audit: local security-audits/readysetsiivous/run-1/REPORT.md, stored outside this repository.
Status words distinguish verified fixes, source-grounded leads, hardening and deployment prerequisites. A source pattern alone is not a confirmed exploitable vulnerability.

| ID | Finding / requirement | Current evidence | Required completion evidence | State |
|---|---|---|---|---|
| SEC-01 | Review failure must not claim a clean review | PR190 merged; strict provider/review contracts | Existing 6 regressions and merged code | Fixed, merged |
| SEC-02 | Approved agent input and isolated generation/verification/publication | PR191 merged; issue116 closed | 50 regressions, 2 real Linux tests, real artifact roundtrip | Code fixed, merged |
| SEC-02A | Activate replacement agent safely | Immutable images/offline caches are required but not configured | Approved images, exact-base cache, complete offline app verification, synthetic end-to-end dispatch before paid use | Pending activation |
| SEC-03 | Public booking phone/number credentials | PR196 current commit 74267cf; 82 targeted backend tests passed (1 DB skip), 12 browser tests and typecheck passed; fresh independent review passed; both review claims were false positives against the existing DTO/UI optional-field behavior | Current-head CI, final review and merged state; legacy policy documented | Implemented, PR open; current CI pending |
| SEC-04 | Inactive cleaner retains started QC token access | PR195 merged as 25dd; 301 local tests passed; 878 real PostgreSQL CI tests passed with 2 unrelated identity-environment skips; corrective independent review passed | Lifecycle race and ownership/history coverage retained in merged implementation | Fixed, merged |
| SEC-05 | Disconnected game rooms exhaust process admission | PR194 merged as d3bf5cbe6fcd659ef235ec3c3e4a318824d29b66; all CI and independent review passed; 10 focused tests passed with no skips | Lifecycle, concurrent admission, waiter/cancellation/reconnect coverage retained in merged implementation | Fixed, merged |
| INF-01 | Public rate limits share proxy IP and retain idle buckets | PR197 commit 100308d; independent review, 27 focused tests and all original-head CI passed; deployment proxy topology remains unconfigured | Sequential rebase and current-head CI/merge; explicit verified deployment proxy topology and trusted/untrusted quota evidence | Implemented, PR open; rebase/merge and topology gate pending |
| INF-02 | JWKS request amplification/blocking | PR198 commit 556d1bc includes negative-cache flood correction; 128 Auth tests passed (1 environment skip); fresh independent review passed | New-head CI and merge; bounded cache/time/body/concurrency, realm rejection, rotation and cancellation coverage retained | Implemented, PR open; new CI pending |
| INF-03 | Runtime Keycloak bootstrap admin credential | Draft PR202 commit 11d5a1e; 12 Python tests passed (2 POSIX skips), 78 backend tests passed (3 owned-service skips); real Keycloak24/26 CI pending, with first public 26 image pull EOF retried once | Real synthetic identity/permission rehearsal, final review/merge and separately reviewed production credential rollout | Implemented, draft PR; real-service CI and production gate pending |
| INF-04 | Runtime database superuser/schema privileges | Draft PR203 commit 42e9722 stacked on PR199; 2 startup tests passed, 3 DB tests skipped; migration-wrapper correction received fresh review | Real PostgreSQL privilege/migration CI, stack rebase, final review/merge and separately reviewed production role rollout | Implemented, draft PR; real DB CI and production gate pending |
| INF-05 | Shared-dev state persistence | PR199 commit 23f0428; unsafe initialization guards corrected; real negative-initialization and container-recreation CI passed | Sequential rebase/merge; reviewed existing-dev backup/import/rollback before switching populated services; no prod data | Implemented, real CI passed; sequential merge pending |
| INF-06 | Dev HTTP / all-interface ingress candidate | Original infrastructure source concern: dev HTTP settings and public interface bindings; no live exposure or firewall state confirmed | Verify ingress/firewall/TLS topology; fail-closed dev binds/tunnel configuration and synthetic checks before completion; no fabricated production probe | Source concern, exposure unconfirmed; verification/fix pending |
| HARD-01 | PDF raster resource exhaustion | AgreementPdfSigner renders default size and retains all page PNGs without aggregate budget | Page dimension/pixel/aggregate/time/concurrency bounds before native allocation, malicious synthetic PDF tests | Hardening pending |
| HARD-02 | Release-consistent deployment inputs | Source is in final review; 18 tests passed with 1 Linux-only skip; rollout remains disabled by the approved false gate | Draft commit/PR, exact release scripts/Compose/digest and Linux deployment/rollback verification, review and merge; concrete activation gate | Implemented source; final review and draft commit pending |
| HARD-03 | Mutable Actions/container versions | Workflow Actions and deployment base/service images use tags | Verified immutable SHA/digest inventory and update instructions; offline workflow checks | Hardening pending |
| HARD-04 | Docker build context / restore reproducibility | api/docker/Dockerfile COPY . .; no api .dockerignore or packages.lock.json found | Narrow build context, reviewed locked restore and repeatable clean build | Hardening pending |
| HARD-05 | Comment text shell interpolation | Former unsafe agent comment path replaced with manual immutable snapshot pipeline | Recheck current workflow event interpolation and PR191 tests | Superseded by SEC-02; source rechecked |
| VERIFY-01 | AI retry quota reservation | PR192 merged as94f47c0; 6 new cases, 847 Linux backend tests passed (2 skipped), 484 desktop/mobile Playwright tests passed; independent and automated reviews clear | Main docker-build-push run40108641286 succeeded at94f47c0; deployment and health not observed; no paid provider smoke test performed | Code fixed, merged; deployment observation pending |
| VERIFY-02 | Dependency/advisory hints | Original offline feed matches were not proven reachable | Current primary advisory/version/applicability evidence; fix confirmed reachable issues, document dismissed hints | Validation pending |
| VERIFY-03 | Original audit tooling gaps | Windows strict validators could not run; old audit explicitly incomplete | Supported isolated Linux validators and bounded synthetic demonstrations for remaining leads | Validation pending |

## Closure rules

No item closes based only on a spec, green unrelated CI, a source-only claim, or an intended deployment. Each fix must have scoped tests and review evidence plus merged state; deployment/migration requirements remain open until observed. Existing booking issue79 and game issue126 overlap this work but must not be closed unless their full acceptance criteria are met. Preserve signed agreements, attachments and historical reports. Do not probe real customers or mutate production to demonstrate a defect.

## Design review

A separate read-only reviewer found no concrete blockers in the consolidated and booking designs. The user subsequently approved the combined design. The reviewer highlighted that authenticated QC started-work policies must be reconciled before centralizing checks; keep historical evidence and intentional completion rules intact.

## Latest environment evidence

Local Docker Linux daemon is unavailable: dockerDesktopLinuxEngine named pipe missing on 2026-10-24. Do not claim the old Windows validator gap is fixed. PR192 main docker-build-push run40108641286 completed successfully at94f47c0. Deployment and production health have not been observed. The full goal is not complete. The combined design is approved, and implementation and verification are in progress.

## QC lifecycle clarification

The approved consolidated design explicitly preserves original-owner completion of started work after roster changes, while checking current employee activity and shift availability. Unstarted reassigned work is denied. The reviewed implementation centralizes current owner/shift checks for token reads and submissions, verifies authenticated end ownership, and coordinates employee Update/Deactivate with QC through the tenant work reservation. GetMyShifts retains recorded history only while its shift is active; administrative evidence remains available. This avoids accidentally revoking valid started-work completion when fixing inactive employee token access. The user approved this lifecycle matrix as part of the combined design; corrective independent source review and original-head real PostgreSQL race verification passed. PR195 subsequently merged as 25dd after passing review and real PostgreSQL CI (878 passed, 2 unrelated identity-environment skips).

## Dependency audit follow-up

The NuGet transitive advisory query exited0 without vulnerable package entries across five restored projects. The npm package-lock-only audit exited1 with36 package entries (4critical,16high,10moderate,6low), including dev/build tooling and propagated dependencies. These are registry findings requiring applicability review, not36 confirmed production exploits. Normalized evidence is in security-nuget-advisory-evidence.json and security-npm-advisory-evidence.json; VERIFY-02 remains open. No dependencies were changed.

## Consolidated approval

The user explicitly approved the combined design on 2026-10-24: "Approve the combined design". The booking legacy-access policy and the revised QC lifecycle matrix are approved. Implementation, synthetic verification, review and PR work may proceed. Production role/credential migrations and paid/provider or live-customer tests retain the separate boundaries stated in the approved design.

## Phase 3 progress

Trusted proxy/bounded limiter (PR197) passed original-head CI and awaits sequential rebase/merge; its deployment topology gate remains open. JWKS PR198 has a reviewed negative-flood correction with new-head CI pending. Shared-dev persistence PR199 passed real negative and recreation CI and awaits sequential merge. Draft PR202 and stacked draft PR203 cover scoped identity and database privilege changes; real-service verification and production rollout gates remain open. The original dev HTTP/all-interface candidate is now explicitly tracked as INF-06 without asserting live exposure. No production role or credential changes have been made. The overall remediation goal is not complete.
