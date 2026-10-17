# Occurrence assignees and recurring handovers

## Approved requirements

Keep recurring shift assignments, support individual-visit changes through an Assignee column in the existing admin occurrences popup, and allow recurring handover from a chosen date. The user explicitly selected both Replace and Add extra for individual visits. Required shift start/optional end dates are implemented separately in PR #162. Deletion/current-work visibility is handled in PR #161.

## User behaviour

- Each occurrence row lists the effective employees, or Unassigned, and an Assign action. Show whether its roster is inherited, replaced or has extra employees. Use tenant-local dates/times.
- Assign opens a dialog with scope This occurrence or From this date onward, action Replace or Add extra, employee selection, required reason, and a preview of the resulting employee names and affected date scope.
- This occurrence + Replace replaces the regular roster only for this visit. Add extra unions the selected employees with the regular roster; duplicate IDs are removed. Selecting several employees supports teams. Other visits are unchanged.
- Restore recurring assignment removes only the visit's assignment override, preserving any move/cancellation and reason. A cancelled visit cannot be assigned until restored. Assignment changes never silently move a visit or label it as Moved.
- From this date onward changes recurring assignment periods at the start of the selected tenant-local calendar date. Replace closes existing periods at that boundary and starts the selected employees; Add extra keeps existing employees and starts additional periods. Previously worked dates remain unchanged. One-off overrides remain authoritative unless explicitly restored; preview warns that they remain.
- Keep the existing whole-shift recurring assignment control, backed by the same dated-period rules. Existing assignments without explicit periods use their existing AssignedAtUtc/history; never backdate them implicitly.
- Reject modifying an occurrence with any started/submitted work. Reject a handover boundary in the past or affecting already-started visits; choose a later boundary instead. No cleaner inputs, ratings, invoice data or public access changes.

## Architecture and compatibility

- Centralise effective employee resolution in one application helper: recurring periods plus optional occurrence override. Use it for occurrence DTOs, employee calendars, start authorisation, dispatcher, overlap checks and monthly expected-task counts. This prevents replacement employees appearing in a list but failing to start, and prevents former assignees receiving duplicate work.
- Preserve existing move/cancel/extra exceptions and legacy EmployeeId overrides. Introduce explicit assignment override data with a list of employee IDs and Replace/Add mode, keyed by the stable occurrence identity (original start for scheduled/moved visits; exception ID for extra visits). Use a separate assignment document so restoring an assignment never removes a schedule exception. Old exception EmployeeId remains the fallback when no new override exists.
- Repository reads/writes stay tenant-scoped. Register the new document in Marten; include it in DeletedShift archives and remove live override links on shift deletion. Archived reporting reads retained overrides, while management lists continue to use live records only.
- Administrative assignment commands validate active shift and active tenant employees, occurrence existence and current version, then check actual occurrence overlaps against both recurring and individually assigned work. Treat adjacent end/start as non-overlapping. Validate all selected employees before saving; no partial team updates.
- Use serializable reservation reads and optimistic versions for assignment/period changes. Concurrent attempts fail with a clear retryable conflict; do not overwrite a newer roster. Recheck started/submitted forms within the same transaction. Keep each handover atomic across affected periods.
- Generate QC only for effective assignees. Untouched draft forms may remain stored for recovery but must not grant visibility/start permission after reassignment. Started/submitted forms are protected by the mutation guard. No checklist emails are introduced.
- Personal history keeps completed past work after roster changes. Employee calendars and start endpoints resolve the same effective roster. Managers/admins who also work shifts follow the same employee rules.
- Monthly reporting resolves the roster for each actual visit, including moved/extra/cancelled visits, and deduplicates expected employees. Retain submitted evidence and historical names. Public snapshots retain their existing redaction, expiry and revocation; no live recomputation of previously shared snapshots.
- The Assignee column/dialog is in the existing admin popup; existing tenant-manager operational API may use the same commands under its current operational permission. Adding a new manager occurrences screen is outside this change.

## Validation and acceptance

- Backend: inherited roster; replace A with B for one visit; extra B keeps A; team deduplication; next visit unchanged; recurring handover at tenant-local midnight; past periods and one-off overrides preserved; inactive/wrong-tenant staff rejected; moved and extra identities; restore without losing schedule exception; cancelled/started/submitted guards; overlaps from recurring and one-off work; adjacent visits; concurrency conflicts; deletion archives; expected-task report counts and immutable shares.
- Use real PostgreSQL tests for tenant isolation, version/transaction behaviour, period boundaries and override query predicates. Test employee-list visibility and actual start authorisation together, plus dispatcher employee IDs.
- Desktop/mobile Playwright: column names/inheritance, Replace/Add extra, team selection, preview, cancel/save, error/retry, restore, handover date, next-visit stability and completed-record lock. Assert actual API errors are displayed. Include Helsinki DST and browser/tenant timezone differences.
- Run backend suite, TypeScript, production build and appropriate browser coverage; review PR comments and verify deployed release before reporting completion.
