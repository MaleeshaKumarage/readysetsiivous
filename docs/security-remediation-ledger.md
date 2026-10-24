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
| SEC-03 | Public booking phone/number credentials | PublicBookingsController and BookingCommands/Queries still authorize with phone | Strong credential negative/positive tenant tests and confirmation UI coverage; legacy policy documented | Proposed design |
| SEC-04 | Inactive cleaner retains started QC token access | QualityCycleApp skips employee checks when StartedAtUtc exists; public token GET checks no identity/lifecycle | Synthetic deactivation/reassignment/read/submit cases; persisted history unchanged | Source-grounded, fix pending |
| SEC-05 | Disconnected game rooms exhaust process admission | GameSessionManager never removes rooms; non-atomic Count/GetOrAdd admission | >1000 lifecycle fixture, concurrent acquire/eviction/cancellation/reconnect tests | Source-grounded, fix pending |
| INF-01 | Public rate limits share proxy IP and retain idle buckets | RemoteIpAddress used without forwarded-header setup; dictionary has no eviction | Trusted/untrusted proxy tests, independent customer quotas, bounded bucket retention | Source-grounded, fix pending |
| INF-02 | JWKS request amplification/blocking | KeycloakJwt fetches synchronously, caches successful responses only | Concurrent cache misses, invalid realms, bounded negative cache/time/body, rotation and cancellation tests | Source-grounded, fix pending |
| INF-03 | Runtime Keycloak bootstrap admin credential | Prod/dev API Compose injects AdminUser/AdminPassword | Scoped service-account credentials; no bootstrap credentials in runtime; real synthetic identity operations | Fix/migration pending |
| INF-04 | Runtime database superuser/schema privileges | API uses POSTGRES_USER identity and CreateOrUpdate schema mode | Separate bootstrap/migration/runtime roles and privileges; real DB rehearsal and rollback | Fix/migration pending |
| INF-05 | Shared-dev state persistence | Dev Keycloak has no external DB/persistent store; API uploads have no volume | Container recreation fixture preserves realm/users/files/key state; no prod data | Fix pending |
| HARD-01 | PDF raster resource exhaustion | AgreementPdfSigner renders default size and retains all page PNGs without aggregate budget | Page dimension/pixel/aggregate/time/concurrency bounds before native allocation, malicious synthetic PDF tests | Hardening pending |
| HARD-02 | Release-consistent deployment inputs | docker-deploy-oracle checks out main while selecting another release SHA | Exact release checkout, bound scripts/Compose and immutable image digest, mocked deployment and rollback tests | Fix pending |
| HARD-03 | Mutable Actions/container versions | Workflow Actions and deployment base/service images use tags | Verified immutable SHA/digest inventory and update instructions; offline workflow checks | Hardening pending |
| HARD-04 | Docker build context / restore reproducibility | api/docker/Dockerfile COPY . .; no api .dockerignore or packages.lock.json found | Narrow build context, reviewed locked restore and repeatable clean build | Hardening pending |
| HARD-05 | Comment text shell interpolation | Former unsafe agent comment path replaced with manual immutable snapshot pipeline | Recheck current workflow event interpolation and PR191 tests | Superseded by SEC-02; source rechecked |
| VERIFY-01 | AI retry quota reservation | PR192 merged as94f47c0; 6 new cases, 847 Linux backend tests passed (2 skipped), 484 desktop/mobile Playwright tests passed; independent and automated reviews clear | Main release jobs still running; no paid provider smoke test performed | Code fixed, merged; release pending |
| VERIFY-02 | Dependency/advisory hints | Original offline feed matches were not proven reachable | Current primary advisory/version/applicability evidence; fix confirmed reachable issues, document dismissed hints | Validation pending |
| VERIFY-03 | Original audit tooling gaps | Windows strict validators could not run; old audit explicitly incomplete | Supported isolated Linux validators and bounded synthetic demonstrations for remaining leads | Validation pending |

## Closure rules

No item closes based only on a spec, green unrelated CI, a source-only claim, or an intended deployment. Each fix must have scoped tests and review evidence plus merged state; deployment/migration requirements remain open until observed. Existing booking issue79 and game issue126 overlap this work but must not be closed unless their full acceptance criteria are met. Preserve signed agreements, attachments and historical reports. Do not probe real customers or mutate production to demonstrate a defect.

## Design review

A separate read-only reviewer found no concrete blockers in the consolidated and booking designs. This does not approve implementation on behalf of the user. The reviewer highlighted that authenticated QC started-work policies must be reconciled before centralizing checks; keep historical evidence and intentional completion rules intact.

## Latest environment evidence

Local Docker Linux daemon is unavailable: dockerDesktopLinuxEngine named pipe missing on 2026-10-24. Do not claim the old Windows validator gap is fixed. PR192 backend job120911559956 completed successfully (847 passed,2 skipped); frontend job120911560240 still installing Chromium at the latest observation. The full goal remains active and the consolidated approval remains pending.

## QC lifecycle clarification

The proposed consolidated design now explicitly preserves original-owner completion of started work after roster changes, while checking current employee activity and shift availability. Unstarted reassigned work is denied. Source: EndQualityCycleFormCommandHandler checks ownership/start; GetMyShifts preserves recorded history only while a shift is active; RecordActivity blocks deleted forms. This avoids accidentally revoking valid started-work completion when fixing inactive employee token access. User approval is still pending.

## Dependency audit follow-up

The NuGet transitive advisory query exited0 without vulnerable package entries across five restored projects. The npm package-lock-only audit exited1 with36 package entries (4critical,16high,10moderate,6low), including dev/build tooling and propagated dependencies. These are registry findings requiring applicability review, not36 confirmed production exploits. Normalized evidence is in security-nuget-advisory-evidence.json and security-npm-advisory-evidence.json; VERIFY-02 remains open. No dependencies were changed.

## Consolidated approval

The user explicitly approved the combined design on 2026-10-24: "Approve the combined design". The booking legacy-access policy and the revised QC lifecycle matrix are approved. Implementation, synthetic verification, review and PR work may proceed. Production role/credential migrations and paid/provider or live-customer tests retain the separate boundaries stated in the approved design.
