# One-time shifts, including past dates

## Approved outcome

Admins can select **One-time** when creating a shift and save one required local date, start time and end time together. A past date is valid. Once assigned, the visit appears in the employee's past/today/future view; past and today visits can start and finish, while future visits cannot start. Scheduled times describe the plan; actual start/end record the button actions. No automatic completion or invented historical actual times.

The user approved this scope on 2026-11-03. This is a focused scheduling change, separate from roadmap C02, which remains unstarted.

## Approach and compatibility

Use an explicit new schedule type, appended without renumbering existing persisted enum values. A rename of On-call would preserve its unscheduled behavior; composing a dated exception after creation risks a partially saved definition. A first-class One-time schedule instead uses the ordinary shift create/update transaction and existing occurrence pipeline.

Existing OnCallFlexible value 4 remains supported by storage, APIs, editing, legacy dated exceptions and partner request materialization. Hide it from new admin schedule selection only; do not migrate or delete historical shifts or change partner acceptance behavior. Build from main including PR #244, retaining its legacy Schedule one-time visit support and occurrence editor race guards.

## Schedule and validation

- Append `OneTime = 6`. Add nullable `OneTimeDate` (date-only local calendar value), `OneTimeStart` and `OneTimeEnd` to the schedule contract. Keep old fields and values compatible.
- Require all three values, with end strictly after start on the same local date. Dates before creation/today are accepted. Reject missing values, invalid times and unknown schedule types with useful validation errors. Overnight work is outside this change.
- Normalize one-time shift validity to the selected date on create/update, or reject contradictory API validity dates clearly. A stale hidden form end date must not cancel the selected visit. Show a single Date input rather than independent recurring validity fields.
- Generate exactly one occurrence when the date is inside the requested inclusive local date range and shift validity; zero outside. Use the existing tenant-timezone conversion policy, including DST handling. Never derive the date from creation time or the caller device timezone.
- Keep existing cancellation, replacements, conflict detection, inactive visibility and parent/tenant permission checks.

## Past assignments, checklist and reports

Audit the complete path, not only the dropdown: creating a past shift and assigning it now must produce a visit the employee can see and act on. Ensure assignment eligibility and overlap checking inspect the one-time date rather than clamping the conflict window to today. Explicit reassignment scopes must retain their documented historical boundaries.

Existing self-work and clock paths already support past dates. Reuse them. If plan-effective or assignment-effective filters suppress the new one-time visit solely because its definition was entered later, add a narrow initial-one-time rule; do not backdate audit timestamps, reopen unrelated history, or broadly disable effective-history checks. Initial attached checklist must be available; incomplete checklist items still do not block finishing. Preserve photos, per-photo notes, completed records and report snapshots.

For edits of an already started/completed visit, retain existing historical protection and correction mechanisms. Do not silently move completed work or replace actual times/evidence. Verify new schedule fields participate in schedule-change/equality checks and plan snapshots.

## Pricing and invoicing

Keep existing Monthly/Hourly options compatible. Make per-completed-visit pricing and visit overrides eligible for the new One-time type where current guards only allow OnCallFlexible; retain legacy behavior. Use ordinary invoice selection, tax calculations, completion checks, duplicate prevention and PDFs. No migration of existing rates or new invoice mode. Reports continue to describe scheduled versus actual work normally.

## Admin UI

Use the existing light Mantine form, English labels and current design system. New schedule dropdown contains One-time and the existing recurring choices. Existing on-call edits still display their legacy choice. One-time shows Date, Start time, End time; no second visit-creation popup is needed after save. Editing restores the selected date and times. Failed saves preserve the draft and show a clear reason; prevent duplicate submits. Summary rows identify the date/time as One-time. Preserve company/branch selection, assignment and checklist controls.

## Verification and release gates

Backend coverage: valid/missing/invalid one-time values, JSON compatibility, inclusive/exclusive date ranges, exactly one occurrence across a wide range, tenant timezone/DST, initial backdated assignment and overlap conflict, own/foreign/inactive visibility, past start/end versus future denial, initial checklist/report availability, historical edit protection and invoice price/duplicate behavior. Execute persistence-dependent cases on isolated PostgreSQL CI; local skips are not database verification.

Desktop/mobile Playwright: option visibility; create a past One-time shift in one form; required/date/time errors; edit round-trip; failed save/retry and no duplicate create; no on-call option on new records but legacy edit/dated-visit still works; assigned past visit shown and start/end actions work; future start blocked. Mocked browser APIs do not establish live integration, so backend tests must cover the real service path.

One focused PR. Typecheck, appropriate build and tests, independent review and every latest-head CI check must pass before merge. Save separate merge/deployment evidence and handoff. Preserve unrelated untracked files and the named stash/patch containing the earlier local A01 documentation checkpoint. Do not start C02 in this context.
