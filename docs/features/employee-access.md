# Employee access and response contracts — issue65

Implementation merged through PR99 as `b5bbae3`, built on main `82d2cb7`.
Website deployment succeeded; API production verification is pending. The
preceding invitation and audience release is already in production; this issue
adds narrower read scopes and tenant-wide manager operations.

| Identity | Administration | Self profile / assigned work |
| --- | --- | --- |
| Active verified bound employee with current employee role | Denied | Own only |
| Active verified bound tenant admin with current admin role | Own tenant | Own only |
| Legacy unbound tenant owner/admin | Own tenant | 404; no invented employee |
| Employee without binding | Denied | 403 |
| Inactive, pending synchronization, unverified or role-drift binding | Denied | 403 |
| Active verified bound tenant manager | Denied; dedicated tenant operational APIs only | Own only |
| Literal staff or no-role identity | Denied | Denied |
| Platform service identity | Denied | Denied |

Managers operate across their tenant, without branch assignments. The dedicated
manager contracts/workspace are documented in [tenant-manager access](tenant-manager-access.md).

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
issue71 and wider authenticated cache recovery issue78. Tenant manager permissions
from issue82 are included in this fix.
These features are not claimed complete by the self APIs.

Local verification covers serialization/marker-secret allowlists, independent
DST/day-boundary tables, assignment/parent/ownership, signed-JWT administrative
negatives and desktop/mobile entry controls. Genuine PostgreSQL/Keycloak browser
checks also cover cleaner self work, tenant-wide manager operations, two-tenant
denials, clock/report/PDF, shift restoration and stale-role lifecycle. Merge and
production deployment remain required before closing #65.

Current local evidence on 2026-10-13: full backend 325 passed / four explicitly
skipped external-service tests (329 total); complete mocked desktop/mobile suite
128 passed; after adding the account-to-shifts link, all 14 focused invitation
and account checks passed again. TypeScript passed. These counts do not claim
that the remaining real-service or cleaner-clock browser cases are complete.

Updated local evidence on 2026-10-14: all368 backend tests passed with owned
PostgreSQL/Keycloak/mail capture, zero skips; all168 desktop/mobile cases passed,
including tenant manager operations, deactivated-shift restore, precise clock
errors and future-start eligibility. The genuine two-tenant browser test passed,
as did TypeScript and the final production build. A discovered Keycloak existing-
role409 was corrected with idempotent manager-role setup and regression coverage.
No issue65 production deployment is claimed yet.

Traceability: AUTH-003/004/008, EMP-004 and OPS-005. The implementation plan and
spec are in `docs/superpowers/plans/2026-10-13-employee-read-scope.md` and
`docs/superpowers/specs/2026-10-13-employee-read-scope-design.md`.
