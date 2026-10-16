# Quality Cycle activity recording and monthly reports

Requirements confirmed and implementation updated on 2026-10-13. This describes
source behavior; deployment is a separate step.

## Business rules

- Tenant admins create templates and assign/change them on existing shifts.
- Each assigned cleaner records activities for each occurrence. They select only
  what they did. Any number of items, including zero, may be selected. Notes and
  photos are optional. Unchecked activities are not failures or exceptions.
- Submission records that occurrence once; it does not end the recurring shift.
- Monthly reports keep company, branch and shift separate. AAA1 weekday 2-hour
  cleaning and AAA1 biweekly 3-hour cleaning have independent rows/PDFs; AAA2 is
  separate. Scheduled hours count each occurrence once, not once per cleaner.
- Every activity has a days-performed count and an occurrence count. Two cleaners
  selecting corridor on the same occurrence count once. Two occurrences on the
  same tenant-local date count as one day and two occurrences. Corridor on 12
  dates and gym on four dates produce 12 and four days respectively.
- Missing records mean nobody submitted the expected record, not that cleaning
  was not performed. Unchecked means not recorded as performed; do not infer a
  mandatory cleaning frequency or noncompliance from it.
- Templates and reporting/sharing are tenant-admin-only. The current `admin`
  Keycloak realm role is the tenant administrator role. `manager`, `employee`,
  and anonymous users cannot manage templates, view private reports, or share.

## Admin and cleaner flow

Use **Shifts → Checklist template → Save template assignment** on an existing
shift. Only active templates matching that shift's company/branch are offered;
API ownership/scope validation also applies to create/update/assignment paths.
Attaching a template creates forthcoming records for already-assigned cleaners.
Repeated attachment/generation is idempotent. No remove/reassign is required.

The shared dispatcher creates records for a rolling 32-day window. A worker scans
active tenants every five minutes after a 15-second startup delay. Assignment,
template changes and manual dispatch use the same generator. Generation creates
tasks only: **no QC/checklist emails are sent**, regardless of Resend configuration
or historical failed delivery state. Account-setup invitation emails are separate.

Employees sign in, open **My shifts** from the account landing, choose **Start**,
then **Record activities & end** on that same shift. They select only the activities
performed and choose **End shift**. None, some or all activities are valid; notes
are optional. A shift without an active checklist can still start/end with no
activity items. Authenticated end requires a prior start.

New records contain company/branch/schedule timing and checklist snapshots.
Future pending records follow template changes; started and submitted records
keep their original activities. Submission before the occurrence starts is 409;
a second submission is 409. Missing/modified/reordered checklist structures are
400. Duplicate item labels still have separate checkbox positions.

Historical checklist links retain their existing resource-token access contract
for compatibility. They are not emailed, issued as the employee workflow, or
advertised on the account landing. The current workflow is authenticated My
Shifts. Public monthly-report sharing remains separate and admin-controlled.

## Reports and sharing

Use **Quality Cycle → Submissions & PDF Reports**. Select year/month and optionally
a shift, then **Load monthly report**. Each row shows company/branch/shift,
scheduled occurrences/hours, recorded/missing/pending records and activity counts.
**Shift PDF** exports one shift; **Branch PDF** contains separate shift sections.
PDFs contain activity counts and occurrence/recording totals.

**Share shift report** or **Share branch report** creates a frozen report link
that anyone possessing it can download as PDF. The confirmed expiry is 30 days.
**Manage shared report links** lists existing links; **Copy link** and **Revoke
link** are admin-only. Revoked/expired/unknown links return 404. Public responses
use no-store/noindex headers. Sharing does not send email or messages automatically.

New shared snapshots include submitted photos, individual photo notes and optional
shift notes in the appendix, while removing full cleaner forms, names and tokens.
Previously created summary-only snapshots remain summary-only.
New submissions affect fresh reports, but never rewrite an existing shared
snapshot. Generate another link to share updated totals. Suspension of the tenant
also denies its public report links through tenant resolution.

Month boundaries and recurring schedule wall-clock times use the tenant profile
timezone, falling back to Europe/Helsinki. Shift forms convert the tenant-local
schedule time to a UTC instant on the server. My Shifts displays date/time in the
tenant timezone even when the cleaner's device uses a different zone. Admin and
manager forms label the timezone used for entered shift times. On a spring-forward
gap, the affected occurrence is omitted; an ambiguous fall-back time uses the
first clock appearance (the earlier UTC instant). Start/end authorization,
overlap checks, dispatch and monthly reports use these same UTC instants and local
calendar dates. Tenant timezone changes affect future generation; existing forms
keep their saved UTC occurrence instants.

Activity identity is exact snapshot text within one shift: renaming an activity
produces separate historical labels. No stable activity catalog, holiday
exclusions or actual worked-hours tracking exists.

## Persistence and rollout

QC templates, records and shares use `ITenantSession`, including public token
lookups. Concurrent generators use a deterministic shift/cleaner/start ID and an
insert transaction in the same tenant; optimistic concurrency protects accepted
submissions. Schedule/template changes and assignment removal/reassignment retain
new history. Earlier changes cannot be reconstructed if the old system never
stored them; legacy snapshots remain available but historical expected totals
may be incomplete. Never invent past activity selections.

See [QC rollout](../quality-cycle-rollout.md) before deployment. Development QC
form/template tables were empty during the read-only partition check on 2026-10-13.
Production partition contents were not inspected or migrated in this change.

## Test contract and remaining gaps

Shift-time cases: same tenant-local time renders identically on admin, manager,
and cleaner devices; offset conversion is applied once; month/day boundaries use
the tenant date; shift starts are enabled by the UTC occurrence instant; spring
gap occurrences are skipped and fall-back ambiguity is deterministic.

| Case | Expected result / coverage |
| --- | --- |
| Existing shift, cleaners already assigned, then attach template | Generate upcoming records without reassigning; application regression |
| Repeat worker / parallel insert | No duplicates; dispatcher regression and real PostgreSQL race |
| Template change after record starts | Preserve started record; update future records; dispatcher regression |
| Weekday 2h and biweekly 3h under AAA1 | Separate occurrence/hour counts; application and desktop/mobile tests |
| Corridor selected by two cleaners on 12 dates, gym on four | 12 corridor days, four gym days; backend count and PDF generation regression |
| Partial selections and empty notes | Accepted, Recorded status; domain and desktop/mobile regression |
| Empty selections | Accepted daily record; zero performed activities, not an exception |
| Record not submitted | Missing after occurrence ends; expected denominator includes schedule even without generated form |
| Tenant B uses A template/form/share IDs or token | No document access; PostgreSQL isolation regression |
| Employee/manager/anonymous template/report/share access | 403/401; real authorization middleware tests with controlled identities |
| Concurrent submission | Losing save fails optimistic concurrency; PostgreSQL regression |
| Share snapshot | Submitted evidence included, no cleaner identity or record tokens; frozen old/new snapshot regression |
| Link expires exactly now / revoked | 404, no PDF generated; backend regression |
| Generate/copy/revoke link | Admin controls; desktop/mobile regression |
| Remove/reassign cleaner with a gap | New assignment periods exclude the gap; add dedicated lifecycle test |
| DST/overnight/month transition | Tenant-local date counts and exclusive boundary; broaden calendar fixtures |
| Template rename / duplicate labels | Preserve snapshot labels; position-based checks; add detailed report grouping fixtures |

Still missing: broader own-work screen and checklist token revocation/expiry policy;
editable submissions/correction audit; scoped template creation controls in FE
(global creation and scope-preserving edits exist); durable private photo upload
and enforced payload limits; holiday calendars; broader lifecycle/calendar
coverage. These are not
silently covered by request mocks. Existing issues #49, #62, #75–77, #82–83 track
remaining identity, lifecycle, evidence and workspace work.

## Verification on 2026-10-13

- .NET build and150tests passed, including isolated PostgreSQL tenant/concurrency
  checks, activity-count/PDF/share snapshot checks and HTTP role negatives.
- Full Playwright suite:50desktop/mobile runs passed. After the final checkbox
  accessibility and existing-shift-action edits, all18QC desktop/mobile runs passed.
- TypeScript checks and the Next.js production build passed.
- No real cleaner emails sent. Production was not deployed or migrated.

## Issue93 verification on 2026-10-14

- All 294 backend tests passed with owned disposable PostgreSQL, Keycloak and
  captured SMTP services; zero skipped tests. Task generation has no email sender
  dependency and preserves historical delivery metadata.
- The final desktop/mobile suite passed all 126 cases. All 16 shift cases cover
  none/subset/all selections, no-template
  clocking, retained input on retry, saved-response recovery and authentication.
- A genuine Keycloak browser test completed account setup, opened My shifts,
  started a task, selected Corridor only and ended it. Real API readback confirmed
  both clock timestamps and the selection. Admin monthly report counted Corridor
  once and Gym zero times; PDF returned a valid PDF header; cleaner report access
  was403. The captured inbox contained only the native setup email before reinvite.
- Merge/deployment verification remains pending; this section does not claim the
  issue93 correction is already live.
- TypeScript checks and the final production frontend build passed.
