# Employee read scope and explicit response contracts — issue65

Status: proposed, awaiting user approval of cleaner self access/admin management
and manager denial pending issue82. No behavior changes are included yet.
Baseline inspected: `612264c`, after invitation and API audience fixes.
Independent spec review: approved for completeness/security/verification readiness
on 2026-10-13. Product approval remains pending; advisory details are fixed below.

## Verified exposure

The `staff` policy means tenant `admin` OR `employee`. EmployeesAdminController,
BookingsAdminController, InvoicesAdminController and AgreementsAdminController
use it on their controllers. Tenant settings and administrative service reads
use it too. Employee list returns persistence objects containing payroll, HR notes,
certifications and identity/delivery internals. Booking and invoice queries return
full models; agreement detail explicitly includes signer capability tokens.
An employee can therefore reach data beyond their assigned work. AdminLayout
checks authentication alone and renders administrative children/navigation.

The issue49 guard already resolves realm+subject to an active linked employee,
checks verified email/current role and rejects pending identity synchronization.
It stores the employee in request context. Use that verified binding for self
operations; do not accept a caller-selected employee/tenant ID as authorization.

## Options and recommendation

1. **Recommended:** make administrative routes tenant-admin-only, add explicit
   self/profile/assigned-work APIs and DTOs, and block nonadmins in the admin shell.
   This preserves legitimate cleaner work while closing the current data leaks.
2. Only deny employee administrative reads. Smaller change, but leaves no safe
   authenticated work API and does not satisfy the operational/self DTO contract.
3. Add branch-scoped managers and delegated operational permissions simultaneously.
   Requires a product decision about branch assignment, customer/HR/billing fields,
   role grants and management actions; that decision belongs to issue82.

## Scope matrix

| Principal | Self profile / assigned work | Tenant administrative APIs | Platform APIs |
| --- | --- | --- | --- |
| Active bound employee, verified, current role | Own only | Denied | Denied |
| Active bound tenant admin | Own employee profile/work plus administration | Own tenant | Denied |
| Legacy unbound tenant owner/admin | No fabricated employee identity | Own tenant | Denied |
| No role, literal legacy staff or unapproved manager | Denied | Denied | Denied |
| Inactive/unverified/pending-sync/role-drift bound identity | Denied | Denied | No tenant-role grant |
| Platform service identity | Denied | Denied | Existing dedicated policy |

Own scope means the active verified binding in the current tenant plus current
resource assignment. Tenant-wide scope remains the current tenant admin role.
Assigned-branch manager scope is explicitly **not granted** until its permission
matrix and persisted branch membership are approved in issue82. A role name alone
is insufficient proof of branch ownership. A user with both employee and admin
claims must still satisfy the persisted role/lifecycle check from issue49.

Protect every `/api/v1/admin/...` controller consistently, including settings,
services, list/detail/document/PDF and schedule variants, before handlers read
data. Anonymous protected requests return401; authenticated insufficient roles
return403. Valid admin cross-tenant IDs remain404/403 with unchanged foreign data.
Public catalogue and existing signer/checklist capability routes retain their
separate resource-token contracts; administrative signer-token lookup is not a
way for an employee to obtain those capabilities.

## API contracts and data minimization

Administrative employee, booking and invoice JSON must use explicit admin DTOs,
with nested typed snapshots rather than returning persistence models. Preserve
the current admin wire names needed by existing editors/details: this is an
authorization/serialization fix, not a silent field-removal migration.
Agreement already uses explicit DTOs; retain signer tokens only in its admin
contract because admins manage signer links. Invoice PDF generation may use the
internal invoice model after authorization; JSON serialization must use its DTO.

The admin DTO field inventories are frozen to the current contracts:

- Employee: id, created/updated UTC, email, first/last name, phone, role, active,
  color, skills, service areas, pay rate, certifications (name/expiry), notes,
  default hours (day→start/end), Keycloak user id (current invite/resend UI),
  invite last attempt/sent, invite error, identity synchronization pending.
  Exclude computed identity key and Marten version. None of these admin-only HR
  or identity fields may escape through the self DTO beyond its explicit allowlist.
- Booking: id, created/updated UTC, booking number, customer (name/phone/email),
  cleaning address (street/postal code/city/country), service id and snapshot
  (fi/en/sv names, duration, net price, VAT percent), employee id/name, UTC times,
  local date/time, total (net/VAT/gross), status, source, customer reference,
  notes and history (status/time/actor/note). Exclude persistence version.
- Invoice: id, created/updated UTC, invoice/booking numbers and booking id,
  customer (name/phone/email), issuer (company/business id/address/email/phone/
  bank IBAN/BIC), lines (description/quantity/unit net price/VAT percent/VAT amount/
  line total), total (net/VAT/gross), currency, issue/due dates, status and paid UTC.
  Exclude persistence version. Its financial/bank fields remain admin-only.

Nested values also use explicit records; referencing mutable persistence classes
would let future domain fields silently broaden these contracts. Self default
hours contain only start/end, and operational addresses contain only the listed
address fields. Preserve nullable values and existing numeric precision.

New self routes under `/api/v1/me` reuse the lifecycle guard and staff role policy:

- `GET /api/v1/me/profile`: employee id, first/last name, own email/phone, current
  role, active status, display color, skills/service areas and default working
  hours. Exclude pay rate, HR notes, certifications, delivery failures/status
  internals, Keycloak subject/derived identity key and persistence metadata.
- `GET /api/v1/me/work?date=YYYY-MM-DD`: a bounded single-day view containing only
  that bound employee's currently assigned bookings and active assigned shift
  occurrences. Return the effective tenant timezone and explicit operational
  records, not full booking/service/company/branch/assignment models.
- `GET /api/v1/me/work/bookings/{id}`: same operational booking contract; return404
  for missing/unassigned/another employee's/another tenant's booking. Never return
  a distinguishable body revealing which unauthorized object exists.

Operational booking fields: id/booking number, status, start/end UTC and display
local date/time, service names/duration, assigned customer name/contact phone and
cleaning address. Exclude customer email/reference lookup code, totals/prices/VAT,
status history/actors and unrestricted notes. Those fields cannot appear as nested
models or extensions added later.

Operational shift fields: id/name, company/branch id and display name, cleaning
address and actual occurrence start/end UTC. Exclude agreement/billing/HR fields,
internal assignment notes, complete rosters, other employee IDs and report/share
tokens. This change does not issue new checklist capabilities: the existing
cleaner's email links remain the submission mechanism, and templates/reports
remain tenant-admin-only under the user's approved requirement.

For an unbound legacy admin, self profile and self-work are404 and do not invent an
employee or return all work. For employees, missing binding is403. Empty work is
200 with empty arrays. Invalid/nonexistent dates are400, with no broad data query.
Caller-supplied extra employee IDs cannot change the binding used for a request.

## Scheduling boundaries

Bookings already have actual UTC timestamps and stored local display strings.
The day view groups actual starts in the tenant-local day, using the profile
timezone and exclusive next-midnight UTC boundary. Shift generation retains the
existing schedule's UTC recurrence interpretation; do not silently move shifts
to another clock/DST policy while issue84 is undecided. Filter generated actual
starts to the same exclusive day window, including validity and assignment start.
Document this legacy interpretation until issues75/84 resolve wider recurrence
policy. Tests must independently enumerate the expected UTC/local boundary table.

Only current active assignments authorize work reads, including historical-day
queries; unassignment immediately removes self access. Deactivated/missing
shifts and invalid foreign parent references expose no foreign data. Do not
reuse broad administrative day DTOs or trust repository mocks without checking
employee ownership in the application projection. Cancellation/completion are
shown as stored statuses for still-assigned bookings, not cleaner mutation rights.
No cleaner completion/cancellation/edit action is added here.

A shift occurrence is eligible when a period of the current active assignment
overlaps `[occurrenceStart, occurrenceEnd)`, matching `ShiftAssignment.Overlaps`.
An assignment beginning during a shift can expose that still-assigned work;
one beginning exactly at/after its end cannot. Reassignment permits only periods
that actually overlap the occurrence, never an earlier unassigned gap. Legacy
assignments without periods use the existing AssignedAt/UnassignedAt fallback.

## Frontend boundary

AdminLayout waits for authentication, tenant admin role and a successful current
identity/lifecycle verification before mounting administrative children. A
nonadmin/denied identity sees a clear access-denied state and account/sign-out
actions, with no administrative data calls or financial/HR navigation. A transient
identity verification failure needs visible retry rather than mounting children.
Existing account landing remains available to cleaners. The broader My Work
screen is tracked by issue70 and must consume these self APIs rather than admin
schedule/list APIs; no missing screen is claimed complete by this backend change.
Use existing authenticated `GET /api/v1/me` for shell verification. Its necessary
identity lookup is permitted in negative tests; denied handler calls must still
perform no payroll, booking, invoice, signer or settings data reads.

## Tests and evidence required

- Unit projection tests put marker secrets in every sensitive field and assert
  exact self/operational allowlists after JSON serialization, including nested
  snapshots. Admin DTOs preserve editor fields but exclude computed identity keys.
- Unit work tests: own/other/unassigned bookings; active/inactive shifts and
  assignment periods; assignment begins after occurrence; missing parent records;
  strict ISO dates, leap day, empty day, day/year rollover, DST, exclusive end;
  no client-selected employee binding and safe404 detail behavior.
- Genuine signed-JWT HTTP tests exercise all admin read/write endpoint families
  with employee/no-role/manager/admin identities. Use sentinel repositories to
  prove denied calls do not read sensitive data, not merely return an empty body.
  Exercise document/PDF and administrative schedule paths as well as lists.
- Real PostgreSQL/Keycloak: two tenants, multiple employees, current assignments,
  sentinel payroll/bank/signer/reference values; authentic role negatives, own
  self-work, cross-employee/tenant IDs, unassignment and stale lifecycle claims.
- Playwright: nonadmin direct navigation on desktop/mobile mounts no admin data
  requests; stale-role/identity denial, transient retry and admin regression.
  Real cleaner browser login/account plus APIRequestContext verifies self DTOs,
  assigned work and forbidden employee/manager/no-role requests with real tokens.
- Full backend/browser regression, type checking and production build; record
  exact counts and limitations. Clean up only owned disposable fixtures.

Traceability: AUTH-003/004/008, EMP-004, OPS-005. Issue70 owns the remaining My Work
UI; issue82 owns branch-manager approval; issue78 retains wider transport/cache
recovery outside the admin entry boundary. Issue65 is not closed merely because
some controllers have a stricter attribute: DTO, self-scope, UI and genuine
negative evidence must all be present.
