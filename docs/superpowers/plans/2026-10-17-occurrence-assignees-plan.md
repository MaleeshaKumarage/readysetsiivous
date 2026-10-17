# Occurrence assignment implementation plan

Approved design: ../specs/2026-10-17-occurrence-assignees-design.md. Preserve main's deletion fix and the separate start/end-date PR. Implement in the following order, verifying each boundary before connecting the UI.

## 1. Domain and persistence

- Add ShiftOccurrenceAssignment with stable scheduled-original-start or extra-exception identity, Inherit/Replace/Add mode, deduplicated employee IDs, reason and version.
- Extend ShiftAssignment with explicit dated-period membership and helpers to truncate future periods/start a dated period without rewriting earlier periods. Preserve legacy undated fallback.
- Register the override document in Marten; add tenant-scoped single/window/employee-discovery reads, atomic override/period writes and archive reads. Extend DeletedShift to retain assignment overrides and remove live override links with deletion.
- Test deterministic occurrence identities, team deduplication, boundary membership (including overnight), legacy history and real PostgreSQL tenant/contains predicates.

## 2. Shared work reservation

- Add an application scheduling-reservation interface and infrastructure implementation. Acquire tenant-scoped advisory locks before authoritative reads; initially one tenant scheduling key provides deterministic ordering across all involved employee/shift resources. Hold the lock until the request's transaction has committed/disposed. Use a bounded lock timeout with a retryable conflict.
- Start a fresh Marten reservation session after acquiring the lock. Wire start, assignment, restore, handover, recurring assignment/unassignment, deactivation/deletion and schedule-exception changes to the same protocol.
- Add an atomic started-form insert/save path in QualityCycleRepository: the current TryCreateFormAsync uses a separate transaction and remains only for background draft generation. Start authorisation and its started form must commit together. Map optimistic/serialization races to HTTP 409; never continue on a poisoned transaction.
- Prove both concurrent outcomes with real database tests: start wins and reassignment fails; reassignment wins and the outgoing employee cannot start. Include deletion/move/cancellation invalidation.

## 3. Effective roster service

- Introduce a shared application service resolving stable visit identity, schedule exceptions, legacy EmployeeId fallback, explicit assignment override and recurring periods. Inherit suppresses legacy overrides; Add performs a set union; Replace uses only explicit IDs.
- Return effective employee IDs plus source/mode metadata for occurrence DTOs. Discover shifts through override-only memberships as well as recurring records.
- Connect personal calendars, actual start authorisation and dispatcher to the same result. Ignore orphan unstarted draft forms as authority after replacement; keep completed past work and current inactive/deleted visibility policy.
- Regression cases: A replaced by B for one visit, A+B for Add, several extras deduplicated, next visit unchanged, moved/extra keys, restore with legacy fallback and staff-role access to own work.

## 4. Mutation commands and availability

- Add SaveOccurrenceAssignment/RestoreOccurrenceAssignment and recurring-handover commands. Validate active tenant employees, stable occurrence identity, cancellation state, non-past local date, started/submitted forms at original/resolved starts and optimistic version.
- Resolve the final roster before any write and check all selected employees against effective recurring and one-off work. Reject collisions, including identical-time scheduled/extra visits; allow adjacent windows. Validate a team atomically.
- Handover uses local midnight: Replace truncates current/future recurring periods and cancels later handovers after the boundary; Add appends periods. Preserve one-off overrides. Reject boundaries affecting started visits. Update IsActive and period-aware reads consistently.
- Expose under existing admin/manager operational authorisation; employee writes must fail. Return clear conflict messages and refreshed effective roster.

## 5. Reporting

- Replace monthly base-schedule-only expected roster calculation with the shared resolver while preserving original-plan company/branch/template attribution for moved visits.
- Count moved work only at the destination; exclude cancellation expected work; include extras once. Retain submitted historical evidence and deduplicate expected/submitted employees.
- Verify two-cleaner team, cross-month move, cancellation, extra, handover and archive report counts. Existing report shares remain immutable and redacted.

## 6. Occurrences popup

- Add Assignee column showing effective names, Unassigned and inheritance/override status. Add assignment dialog with scope (This occurrence/From this date), action (Replace/Add extra), active employee multi-select, required reason and preview.
- Preserve existing move/cancel controls. Restore assignment is separate from restoring schedule exceptions. Display API failures and retain selections for retry; disable unsafe controls on locked/cancelled records.
- Test every action on desktop/mobile: team selection, preview, cancel, save, failure/retry, restore, handover, unchanged surrounding visit, inactive staff and completed-work locks. Test different browser/tenant zones.

## 7. Release

- Run real-PostgreSQL regressions and full backend suite, TypeScript and production build, then browser suite. Keep tests in CI's testMatch.
- Update feature matrix/edge cases and API behaviour docs. Push PR, attach it, address review findings, rebase on latest main, merge after checks and verify actual production image/health and website bundle.

The referenced writing-plans skill is not installed in this workspace; this plan is recorded directly from the approved, independently reviewed spec.
