# Admin daily shift calendar

The admin navigation includes **Shift calendar** for enabled admin employees. It shows one tenant-local day with shift cards, company/branch, effective visit assignees, work status and actual hours. Previous/next day, a date picker, Today, Refresh, and empty/error states are available on desktop and mobile. Overnight cards belong to their scheduled start date.

## Access and initial rollout

Admins manage the permission from **Employees**, using **Enable shift calendar** or **Disable shift calendar** beside an active, synchronized admin. Managers and employees cannot receive access. Calendar read and write endpoints independently enforce permission using the authenticated subject's linked, verified employee identity.

`Employee.ShiftCalendarEnabled` is nullable for existing documents. Its initial effective default is enabled only for the active admin employee whose email is `maleesha@hiava.fi` (case insensitive). All others default to disabled. Saving an explicit false revokes even the initial account; saving true enables another admin. Inactive, demoted, or synchronization-pending employees cannot use it. An unlinked tenant-owner login must first be linked to an employee profile; email claims alone do not grant access. No live account or deployment is modified by adding this default.

## API and work recording

- `GET /api/v1/admin/shifts/calendar?date=YYYY-MM-DD` defaults to today and returns `date`, `today`, `timeZoneId`, and visit cards. Reads bypass browser caching.
- `PUT /api/v1/admin/employees/{id}/shift-calendar-access` accepts `{ enabled: boolean }` for active admin employees and requires the existing admin authorization policy.
- `PUT /api/v1/admin/shifts/calendar/work-time` accepts shift/employee identity, occurrence start in UTC, exception ID, actual start/end as tenant-local `YYYY-MM-DDTHH:mm`, reason, and the current work-record version (null for a missing record).

Work recording is available only when the scheduled start date precedes today's tenant-local date. Enter start/end together; existing records can be corrected. Both times must be in the past and end must follow start. Invalid and ambiguous DST wall times are rejected with an explicit message. The dialog displays the calculated duration before saving.

Writes resolve the visit and assignees again, reject cancelled/deactivated/deleted visits, validate record versions, and use the same tenant scheduling reservation as employee start/end operations. Existing records remain correctable after roster changes. Unassigned visits require assignment through existing shift management first.

Work uses the existing Quality Cycle record and deterministic task identity, so personal calendars and work-hour projections read the same timestamps. New records become recorded with checklist items unchecked; corrections preserve answers, photos and notes. Audit entries on the form retain actor subject, real change time, reason and previous/new work times. Issued invoices are not regenerated.

The existing work-record identity cannot distinguish two visits on the same shift with the same start timestamp. Such cards remain visible but recording is disabled until the overlapping schedule is corrected.

## Verification

`AdminShiftCalendarTests` covers initial permission defaults/revocation, role and lifecycle restrictions, DST validation, recording/corrections, stale versions, evidence preservation, effective rosters, cancellation and inaccessible shifts. `lifecycle-admin-calendar.spec.ts` exercises desktop/mobile daily cards, time entry, past-only actions, direct URL denial, retries and stale response handling.
