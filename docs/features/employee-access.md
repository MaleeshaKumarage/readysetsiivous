# Employee access and response contracts — issue65

Implementation branch: `fix/employee-read-scope-65-main`, created from latest main
`68dedb7`. This work is not yet merged or deployed. The preceding invitation and
audience release is already in production; this issue adds narrower read scopes.

| Identity | Administration | Self profile / assigned work |
| --- | --- | --- |
| Active verified bound employee with current employee role | Denied | Own only |
| Active verified bound tenant admin with current admin role | Own tenant | Own only |
| Legacy unbound tenant owner/admin | Own tenant | 404; no invented employee |
| Employee without binding | Denied | 403 |
| Inactive, pending synchronization, unverified or role-drift binding | Denied | 403 |
| Manager, literal staff or no-role identity | Denied | Denied |
| Platform service identity | Denied | Denied |

Branch-manager permissions remain ungranted until issue82 defines assignments
and delegated capabilities. A role name alone never establishes branch ownership.

Every administrative controller requires tenant admin before its handler runs,
including employee/booking/invoice/agreement lists, details, PDF/document paths,
service/settings reads and schedule variants. Employee, booking and invoice JSON
uses explicit nested admin response contracts. The admin wire fields needed by
existing editors remain available, including invitation/registration timestamps,
translations, payroll, billing and signer management where applicable. Persistence
versions and computed identity keys are not administrative response fields.

Self routes require the existing database-backed lifecycle guard. They use its
verified realm+subject binding, not an employee ID supplied by the caller:

- `GET /api/v1/me`: minimal identity handshake for account/admin entry.
- `GET /api/v1/me/profile`: own contact/name, role, active status, color, skills,
  service areas and working hours. No payroll, HR notes, certifications, identity
  internals or invitation delivery errors.
- `GET /api/v1/me/work?date=YYYY-MM-DD`: own currently assigned bookings and shift
  occurrences whose actual UTC starts fall in that tenant-local day.
- `GET /api/v1/me/work/bookings/{id}`: own assigned operational booking, otherwise
  indistinguishable 404 for missing, unassigned, other employee or foreign tenant.
- Existing `/me/shifts` and clock start/end remain bound to the same identity.
  Own task snapshots retain their checklist capability and optional activity
  recording; other employees' forms and admin report/share capabilities do not.

Operational booking data includes service names/duration, times/status, customer
name/phone and cleaning address. It excludes email, prices/totals, lookup codes,
unrestricted notes and status-history actors. Shift data includes its company and
branch names/address/times, with no roster, assignment notes or billing fields.
Missing, inactive or foreign parent references do not yield operational records.

Days use strict ISO date parsing and exclusive next-midnight UTC boundaries in
the tenant timezone. Blank/missing timezone uses Europe/Helsinki. Invalid stored
timezone returns a validation error before operational queries. Midnight clock
gaps start at the first existing instant; repeated midnight uses the earlier UTC
instant. A skipped local day is rejected. Shift recurrence still follows its
existing UTC schedule interpretation; issue84 owns broader timezone policy.
Current active assignments authorize reads only when a stored assignment period
overlaps the actual occurrence. Unassignment revokes historical self reads;
reassignment gaps and assignments beginning exactly at shift end are excluded.
Existing shift interval reads reject non-increasing, overflowing or >32-day ranges.

The admin shell verifies current identity without caching before mounting any
children. Denied users see account/sign-out actions and make no administrative
data requests. Transient verification failures show a retry action. Active bound
accounts can reach the existing My Shifts page from the account landing.

The wider My Work booking UI remains issue70. Advanced employee editors remain
issue71, branch managers issue82, and wider authenticated cache recovery issue78.
These features are not claimed complete by the self APIs.

Verification is in progress: serialization/marker-secret allowlists, independent
DST/day-boundary tables, assignment/parent/ownership tests, genuine signed-JWT
administrative rejection matrices and desktop/mobile admin-entry tests have run.
Final real PostgreSQL/Keycloak and cleaner-browser coverage, full regression,
review, merge and deployment remain required before closing #65.

Current local evidence on 2026-10-13: full backend 325 passed / four explicitly
skipped external-service tests (329 total); complete mocked desktop/mobile suite
128 passed; after adding the account-to-shifts link, all 14 focused invitation
and account checks passed again. TypeScript passed. These counts do not claim
that the remaining real-service or cleaner-clock browser cases are complete.

Traceability: AUTH-003/004/008, EMP-004 and OPS-005. The implementation plan and
spec are in `docs/superpowers/plans/2026-10-13-employee-read-scope.md` and
`docs/superpowers/specs/2026-10-13-employee-read-scope-design.md`.
