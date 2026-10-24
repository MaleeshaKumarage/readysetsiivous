# Approved security remediation implementation

## Primary goal

Customers, cleaners and administrators can use existing workflows without the audit's access, resource-exhaustion and deployment-integrity gaps. Every item in docs/security-remediation-ledger.md must be verified resolved or disproved with evidence; merged source alone does not prove a production migration or agent activation.

## Phase 0: documentation discovery

**How this serves the primary goal:** validates real interfaces and existing behavior before changing security boundaries.

Completed read-only discovery by remediation_phase0_discovery. Evidence: PublicBookingsController lookup/cancel, BookingCommands/Queries and tenant-partitioned BookingRepository; BookingFlow confirmation state and i18n JSON; QualityCycleApp lifecycle handlers, MeController verified identity, SchedulingWriteBehavior and cross-replica ShiftWorkReservation; EmployeeCommands identity-only locking; GameSessionManager and EntertainmentHub call sites. Copy existing EntertainmentPlayerToken crypto and AgreementEmailCopyButton clipboard patterns, tightening encoding validation. The SDK is pinned to10.0.402 and available at C:/Users/malee/source/NewIdeas2/cleaning-site/tmp/dotnet10/dotnet.exe; its reference pack is the API authority during builds.

Allowed APIs: RandomNumberGenerator.GetBytes(32), Convert.ToHexString, SHA256.HashData, CryptographicOperations.FixedTimeEquals; body-bound MVC requests/no-store headers; IShiftWorkReservation.AcquireAsync(CancellationToken) returning Task<IAsyncDisposable>; SemaphoreSlim.WaitAsync(TimeSpan,CancellationToken) and TimeProvider. No invented framework APIs or deprecated network trust-all options. Later groups repeat focused discovery against installed package/framework signatures before implementation.

Discovery clarified that current public cancellation guards terminal states but defines no time cutoff. Preserve that actual behavior: do not add an unapproved cancellation window. Employee Update as well as Deactivate can disable an employee; both must coordinate with QC writes. Do not nest the non-reentrant advisory reservation.

## Phase 1: access and game lifecycle (three isolated lanes)

**How this serves the primary goal:** removes weak customer credentials, stale cleaner-token access and cumulative game admission exhaustion without replacing existing product flows.

### Lane A — booking management credentials

Checkout cleaning-site-booking-security, branch fix/booking-management-keys. Follow the approved booking-access spec. Own booking domain/helper, public controller/commands/queries/contracts, matching booking tests, confirmation UI/i18n/browser tests and booking feature documentation. Copy strong random/hash/compare patterns; return raw key only once, persist hash only; reject phone/short-reference and legacy anonymous access. Preserve staff operations and terminal-state behavior.

Verification: negative/positive tenant/booking/encoding/legacy cases; HTTP no-store/body-only/no enumeration; no raw key persisted or exposed through staff/self DTOs/logs; real creation/reservation regressions; confirmation copy success/failure and fi/en/sv labels/reset. Avoid key URL/localStorage and opportunistic backfill/customer emails.

### Lane B — QC current identity/lifecycle

Checkout cleaning-site-qc-security, branch fix/qc-token-lifecycle. Own QC lifecycle guards/handlers, employee update/deactivate race coordination, matching tests and QC feature docs. Follow the approved matrix: current inactive employee denied even for started/submitted forms; inactive/deleted shift blocked in work paths; unstarted removed roster denied; original active owner may finish started work after roster change. Preserve historical administrative evidence, optional checklist selections, actual-clock and past/today/future rules. Keep owner identity derived by MeController.

Verification: synthetic token read/submit/start/end matrix and preserved attachments; ownership/tenant/terminal replay; deactivation/update race against QC write under a coherent database reservation/reload/version protocol. Use real disposable PostgreSQL evidence in CI. Required security dependencies in production; update unit fixtures rather than retaining optional bypasses. Avoid double acquisition or lock-order inversion with the identity lock.

### Lane C — lease-safe game room retention

Checkout cleaning-site-game-rooms, branch fix/game-room-retention. Own GameSessionManager, related configuration/DI only if required, registry/hub tests and game-room feature docs. Copy existing hub identity/reconnect behavior. Atomic capacity admission covers Acquire and RegisterConnection; reference-count holders/waiters/connections; identity-safe expiry after configurable default10min grace; never await under registry lock. Durable game state remains unchanged.

Verification: deterministic fake-clock turnover beyond1000 distinct idle rooms, actual active cap, concurrent admission, acquisition cancellation/timeout, duplicate/disposed lease handling, disconnect/reconnect and no two gates for one active group. Never evict a gate still held or awaited; never bypass cap in RegisterConnection. No scoring/provider changes.

## Phase 2: independent verification and publication of each lane

**How this serves the primary goal:** makes each access/lifecycle fix reviewable and prevents green unrelated tests from standing in for security evidence.

For each lane: fresh verification, anti-pattern and code-quality review; source-backed fixes; targeted tests then relevant broader tests; diff/secret checks. Commit only after checks. Push separate PR, attach it, inspect current-head CI/comments and merge after verified readiness. Respect the approved boundary on external AI code sharing; use local reviewers or separately approved public-data review, never hide provider failure. Rebase sequentially if main changes and rerun affected checks. Do not close whole issues79/126 merely because one security subrequirement is fixed.

## Phase 3: request identity and authentication resilience

**How this serves the primary goal:** prevents proxy-wide denial and untrusted-token traffic from amplifying synchronous identity lookups.

Focused discovery then independent lanes for trusted proxy options/bounded limiter and asynchronous bounded JWKS resolution. Follow approved design exactly; preserve strict audience/issuer policy. Tests must prove spoofed proxy headers do not change identity, independent client buckets, bounded idle storage/429 details; malformed realms, body/time/concurrency/cache bounds, rotation/cancellation/unknown-kid refresh and unchanged tenant/platform separation. No shared JWT options mutation and no trust-all private network shortcut.

## Phase 4: runtime privileges and persistence

**How this serves the primary goal:** limits the consequences of runtime compromise and preserves existing dev identities/files across restarts.

Focused discovery then scoped Keycloak service accounts, separate PostgreSQL bootstrap/migration/runtime roles, and persistent dev identity/uploads/keys. Own setup scripts/Compose/config changes in separate related PRs; copy pinned Keycloak/DB APIs after verifying them. Synthetic real-service rehearsal must prove identity operations, migration, privileges and recreate persistence plus rollback. Preserve production backups/history; no live credential/role changes until a concrete rollout is reviewed. Never silently fall back to bootstrap admin or enable runtime schema creation to bypass migration failure.

## Phase 5: documents, supply chain and release integrity

**How this serves the primary goal:** bounds hostile media work and ensures shipped code/config/dependencies match reviewed inputs.

Separate focused lanes for PDF worker/budgets; release-bound script/Compose/image digest; immutable Actions/images and Docker context/NuGet locked restore; dependency remediation based on the full npm inventory and exact primary advisory evidence. Prefer supported compatible fixes with tested builds/Storybook/static export; do not force downgrade or claim all advisory matches are public runtime exploits. Keep all36 npm package entries tracked to final dispositions; NuGet advisory scan is evidence, not native-library coverage.

Verification: native geometry/expansion/time/concurrency fixtures before dangerous allocation; signed evidence retained; mocked release/rollback/provenance tests; immutable inventory/ref checks; clean locked restores/builds; dependency lockfile and reachable dev/build/runtime attack paths. Do not delete signed agreements or customer attachments as cleanup.

## Phase 6: rollout, original audit validation and completion audit

**How this serves the primary goal:** proves the actual requested final state rather than equating source changes with deployed protection.

Finish compatible Linux strict audit validators and bounded original lead demonstrations. Prepare reviewed agent images/exact-base offline caches and prove synthetic execution before activation. Track release commit health without paid AI smoke calls. Review production role/credential rollout separately with backups and rollback. For every ledger item inspect the actual source/tests/review/merge/deployment evidence; leave unresolved/unsupported requirements open. Goal complete only when all original and follow-up requirements have sufficient evidence; no smaller substitute goal.
