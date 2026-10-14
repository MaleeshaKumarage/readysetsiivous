# Tenant manager access — issues65/82

Implementation merged through PR99 as `b5bbae3`. The user explicitly
specified managers for the whole tenant, not individual branches. Manager
permissions are deployed: the API image, website deployment and production health are verified in the [release evidence](../releases/2026-10-14-employee-manager-release.md).

| Capability | Tenant admin | Tenant manager | Cleaner |
| --- | --- | --- | --- |
| Own profile and currently assigned work/clock | Own if bound | Own if bound | Own if bound |
| Operational roster and company/branch references | Own tenant | Own tenant | Denied |
| Shift create/edit/deactivate/reactivate, employee assign/remove | Own tenant | Own tenant | Denied |
| Shift company/branch relocation | Own tenant | Denied | Denied |
| QC template creation/edit/assignment | Own tenant | Denied | Denied |
| Monthly reports/PDF/share links | Own tenant | Denied | Denied |
| Employee creation/edit/invite, role grants, payroll/HR | Own tenant | Denied | Denied |
| Invoices/agreements/signer tokens/settings/services | Own tenant | Denied | Denied |
| Platform tenant provisioning/suspension | Dedicated platform identity only | Denied | Denied |

Managers enter `/manager/` from their authenticated account. The workspace verifies
current realm, claims and persisted active role before fetching operational data.
It covers every branch in that tenant and has no branch membership configuration.
Direct admin navigation remains denied. API enforcement applies independently of
hidden UI controls and rejects stale tokens after role or lifecycle changes.

## API and field boundaries

All routes below use `/api/v1/manager` and the tenant manager/admin operations
policy, plus the current bound-identity guard. Existing unbound tenant owners keep
admin operations; an unbound manager is denied. No caller-selected tenant is accepted.

| Method | Path | Response/action |
| --- | --- | --- |
| GET | `/employees` | Active operational roster; no email/payroll/HR/invite/identity internals |
| GET | `/companies?skip=0&take=100` | Paged id/name/active; UI loads subsequent pages |
| GET | `/companies/{id}/branches` | Active own-tenant branches, operational address/contact |
| GET | `/shifts`, `/shifts/{id}` | Explicit schedule and validity contract; no QC plan/template/token fields |
| POST | `/shifts` | Create validated shift in an active company/branch, without template/notes |
| PUT | `/shifts/{id}` | Edit name/schedule/activity/validity; preserve parent ids, notes, template and history |
| POST | `/shifts/{id}/deactivate` | Deactivate while retaining history |
| POST | `/shifts/{id}/reactivate` | Restore retained fields and assignments; require active parents; repeated requests are idempotent |
| GET | `/shifts/{id}/assignments` | Own-tenant assignment identity/time/status, without notes |
| POST | `/shifts/{id}/assignments` | Assign active employee; existing overlap checks apply |
| DELETE | `/shifts/{id}/assignments/{employeeId}` | Remove assignment, retaining its historical periods |

Roster fields are id, first/last name, phone, active, color, skills, service areas
and explicit default hours. Shift responses contain id, company/branch ids, name,
explicit typed schedule, active and validity dates. Unknown mutation fields such
as role, notes, template or parent relocation are rejected400. Another tenant's
IDs return404. Invalid/inactive/mismatched parent references cannot be used.

Manager edits load and save the same versioned shift. A concurrent admin template
change yields409 instead of overwriting its template or notes. The UI retains the
manager's form after failure; retry starts a new request using current stored
admin-owned fields. Shift timezone/recurrence follows the existing UTC policy;
broader calendar policy remains issues75/84.

The **Show deactivated shifts** filter is available in manager and admin shift
lists. Reactivation records a new active plan without rewriting the inactive gap,
extends no validity dates and sends no QC email. It preserves the template, notes,
schedule and assignment history. Reactivation concurrent with an admin update
fails409 rather than overwriting it. Expired shifts remain outside future work
until their validity dates are explicitly edited.

## Realm preparation and lifecycle

Supported persisted roles remain `admin`, `employee` and now `manager`. Legacy
admin means tenant admin, without platform privileges. Admin employee controls
can choose manager; manager controls cannot change roles or invite people.
New realm provisioner and dev realm import include manager. Existing realms gain
the empty manager role idempotently through the authorized admin invitation/role
sync path when manager is selected. No existing user automatically gains it.
Role synchronization removes other managed roles, preserves unrelated Keycloak
roles and attributes, and preserves passwords. Pending sync, inactive account,
unverified email and mismatched token/document role deny manager and self access.

## Test cases and remaining features

- Two branches in one tenant are both visible without branch grants; foreign
  tenant shift IDs are denied on GET/PUT/assignment routes.
- Exact serialized allowlists exclude payroll, certifications, email, invitation
  errors, subjects, notes, persistence versions, QC/signature/report tokens.
- Signed-token negatives cover all manager operations for employee/staff/no-role,
  and all administrative read/write/PDF/share families for manager identities.
- Manager role creation/promotion/demotion preserves unrelated roles/passwords;
  unbound, stale, inactive, unverified and pending identities are denied.
- Real PostgreSQL concurrent manager edit/admin template change preserves the
  winning admin state; missing/inactive/foreign parents cause no writes.
- Desktop/mobile controls cover all schedule types, create/edit/cancel, assignment
  removal, deactivation, load/save retry, login/logout and account navigation.

Still missing: broader manager booking/customer operational screens and dedicated
delegated capabilities such as billing; these are not implicitly granted through
this role. Cleaner My Work UI remains issue70, advanced employee fields71 and
general authenticated transport/cache work78. Manager cannot relocate a templated
shift or manage templates/reports; a tenant admin handles those actions.

Local verification on 2026-10-14: all368 backend tests passed with owned disposable
PostgreSQL/Keycloak/mail capture and zero skips; all168 desktop/mobile tests passed.
The genuine browser test passed cleaner self-work privacy, optional activities,
admin report/PDF, native invitation/password preservation, tenant manager login
across two branches, foreign-tenant denials, shift creation/assignment/restoration,
template preservation and stale-token denial after role/lifecycle changes.
TypeScript and the production build passed. Merge/deployment verification remains
pending; this document does not claim manager permissions are already live.
