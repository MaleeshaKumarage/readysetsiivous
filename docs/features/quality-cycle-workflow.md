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
template changes and manual dispatch use the same generator. Current-day links
are emailed when Resend is configured; ten-minute delivery retry leases and
stored delivery state distinguish generated records from sent notifications.
Email can be delivered more than once if the provider succeeds before a process
crash; exactly-once external delivery is not promised.

New records contain company/branch/schedule timing and checklist snapshots.
Future pending records follow template changes; started and submitted records
keep their original activities. Submission before the occurrence starts is 409;
a second submission is 409. Missing/modified/reordered checklist structures are
400. Duplicate item labels still have separate checkbox positions.

Cleaner links include tenant and an opaque record token. This is the existing
resource-token access model: possession of a link grants access to that one
record, without administrator capabilities. It is not authenticated My Work.

## Reports and sharing

Use **Quality Cycle → Submissions & PDF Reports**. Select year/month and optionally
a shift, then **Load monthly report**. Each row shows company/branch/shift,
scheduled occurrences/hours, recorded/missing/pending records and activity counts.
**Shift PDF** exports one shift; **Branch PDF** contains separate shift sections.
PDFs contain activity counts and occurrence/recording totals.

**Share shift report** or **Share branch report** creates a frozen summary link
that anyone possessing it can download as PDF. The confirmed expiry is 30 days.
**Manage shared report links** lists existing links; **Copy link** and **Revoke
link** are admin-only. Revoked/expired/unknown links return 404. Public responses
use no-store/noindex headers. Sharing does not send email or messages automatically.

Shared snapshots remove all cleaner forms, names, tokens, notes and photos.
New submissions affect fresh reports, but never rewrite an existing shared
snapshot. Generate another link to share updated totals. Suspension of the tenant
also denies its public report links through tenant resolution.

Month boundaries use the tenant profile timezone, falling back to Europe/Helsinki,
with an exclusive next-month boundary. Existing shift schedule time fields retain
the repository's UTC convention. Month grouping does not convert recurrence entry
times into local-time schedules. Activity identity is exact snapshot text within
one shift: renaming an activity produces separate historical labels. No stable
activity catalog, holiday exclusions or actual worked-hours tracking exists.

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
| Share snapshot | No cleaner identity, notes, photos or record tokens; backend regression |
| Link expires exactly now / revoked | 404, no PDF generated; backend regression |
| Generate/copy/revoke link | Admin controls; desktop/mobile regression |
| Remove/reassign cleaner with a gap | New assignment periods exclude the gap; add dedicated lifecycle test |
| DST/overnight/month transition | Tenant-local date counts and exclusive boundary; broaden calendar fixtures |
| Template rename / duplicate labels | Preserve snapshot labels; position-based checks; add detailed report grouping fixtures |

Still missing: authenticated own-work screen and token revocation/expiry policy;
editable submissions/correction audit; scoped template creation controls in FE
(global creation and scope-preserving edits exist); durable private photo upload
and enforced payload limits; holiday calendars; genuine Keycloak browser login
coverage and notification-provider failure/crash recovery tests. These are not
silently covered by request mocks. Existing issues #49, #62, #75–77, #82–83 track
remaining identity, lifecycle, evidence and workspace work.

## Verification on 2026-10-13

- .NET build and150tests passed, including isolated PostgreSQL tenant/concurrency
  checks, activity-count/PDF/share snapshot checks and HTTP role negatives.
- Full Playwright suite:50desktop/mobile runs passed. After the final checkbox
  accessibility and existing-shift-action edits, all18QC desktop/mobile runs passed.
- TypeScript checks and the Next.js production build passed.
- No real cleaner emails sent. Production was not deployed or migrated.
