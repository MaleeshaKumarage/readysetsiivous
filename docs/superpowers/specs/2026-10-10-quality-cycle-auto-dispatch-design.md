# Quality Cycle Auto-Dispatch at Shift End — Design

Date: 2026-10-10
Status: Approved for implementation planning

## Goal

Automate the Quality Cycle (QC) form dispatch so that, when a shift with an
assigned QC template ends, the assigned employee automatically receives an
email with a link to fill the checklist. No manual "Dispatch Forms Now" button.
Forms aggregate into a monthly summary PDF per employee / branch / shift.

## Requirements

1. **Template assignment** — a shift links to a QC template via
   `Shift.QualityCycleTemplateId` (already exists; the shift page already has
   the selector).

2. **Backdating** — when assigning a template to a shift that has already
   started, prompt the admin:
   - "From today" — dispatch only for future occurrences.
   - "From beginning" — then choose "from start date" or a **custom date**
     (calendar input). Backfill past occurrences from that date.
   Backfilled past occurrences get a form + email **immediately**.

3. **Auto-dispatch at shift end** — an in-app `BackgroundService` runs every
   5 minutes, finds occurrences whose end time just passed, and for each:
   create the form + email the assigned employee. (Replaces the manual button
   and the on-assignment dispatch.)

4. **Fill** — employee opens the emailed link, fills the checklist (+ photos +
   notes), submits via the existing public form.

5. **Report** — monthly summary **PDF only**, generated per **employee**,
   **branch (workplace)**, or **shift**, filtered by month. Contents:
   - Number of visits.
   - Total hours (sum of occurrence durations).
   - Per-day "done" summary.
   - Checklist item aggregates (each item `× N` occurrences ticked).
   - Photos attached with their descriptions.

## Architecture

### Components

- **`IQualityCycleDispatcher`** (Application service) — core dispatch logic:
  given a shift + occurrence + employee, create the `QualityCycleForm` (if not
  already present) and send the fill-in email. Used by both the background
  service and the backfill endpoint. Extracted from the current
  `DispatchQualityCycleFormsCommandHandler`.

- **`QualityCycleBackgroundService`** (`BackgroundService`, Infrastructure) —
  runs every 5 min. Loads shifts that have an active template, computes each
  shift's occurrences, finds occurrences whose `EndUtc` falls in the last
  dispatch window (`now - interval` … `now`), and dispatches a form to each
  assigned employee. Idempotent via
  `IQualityCycleRepository.GetFormByShiftOccurrenceAsync` (skips existing).

- **Backfill** — when a template is assigned to a shift with a start date in
  the past, the admin chooses a backfill start date; the API backfills forms +
  emails for past occurrences in `[startDate, now]`.

- **Report generator** — extends the existing `QualityCyclePdfGenerator` to
  produce the monthly summary (visits, hours, per-day done, checklist item
  counts, photos).

### Data model (unchanged, already present)

- `Shift.QualityCycleTemplateId` (`Guid?`).
- `QualityCycleTemplate` — items, title, isActive.
- `QualityCycleForm` — shiftId, employeeId, `ShiftOccurrenceUtc`, token, items
  (checked/unchecked), `PhotoUrls`, `CleanerNotes`, `IsSubmitted`,
  `SubmittedUtc`.
- `ShiftAssignment` — shiftId, employeeId, isActive.

### Data flow

```
shift end time passes
   → QualityCycleBackgroundService (5 min tick)
   → find ended occurrence(s) in window
   → for each assigned employee: create QualityCycleForm + email fill link
   → employee fills + submits (photos/notes)
   → monthly report aggregates submitted forms
```

Backfill flow:

```
admin assigns template to in-progress shift
   → prompt "from today" | "from beginning"
   → if "from beginning": "from start date" | custom date
   → backfill: for each occurrence in [startDate, now]: create form + email
```

## Decisions

1. **Scheduler** — in-app `BackgroundService`, 5-min interval. (User chose
   in-app hosted service over external cron.)
2. **Dispatch timing** — at occurrence **end time** (not end of day, not ahead
   of time).
3. **Backfill email** — immediate (user chose A), not silent.
4. **Backfill window** — bounded; user picks "start date" or a custom date
   (user chose B with the calendar option).
5. **Workplace** = **Branch** (not Company).
6. **Report format** — PDF only.

## Removals (from the current in-progress state)

- Remove the **on-assignment dispatch** added to
  `AssignEmployeeToShiftCommandHandler` (the `DispatchQualityCycleFormAsync`
  call) — dispatch is now only at shift end.
- The manual "Dispatch Forms Now" button is already removed from the
  quality-cycle admin page; keep it removed.

## Error handling

- Email failures must not roll back form creation or the shift assignment;
  log and continue.
- Background service tick failures are caught and logged; the next tick
  retries (idempotent).
- Form dedup: `GetFormByShiftOccurrenceAsync` prevents duplicate forms/emails
  on overlapping ticks or re-runs.

## Testing

- **Unit**:
  - Dispatcher creates a form only once per (shift, employee, occurrence).
  - Background service dispatches only occurrences whose end time just passed.
  - Backfill respects the chosen start/custom date window.
  - Report aggregates: visits count, hours sum, per-item tick counts.
- **Integration / E2E**:
  - Assign template to shift → simulate shift end → employee receives email
    link → fills → form appears in the monthly report.
  - Backfill "from beginning" with a custom date → past occurrences get forms
    + emails.
  - Report generated per employee / branch / shift, PDF with photos.
