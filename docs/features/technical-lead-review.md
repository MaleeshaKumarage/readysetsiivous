# Technical lead review: workspaces, auth and roles

Reviewed on 2026-10-13 against `ceff5bd` plus the documented local inventory.
Recommendations below are for discussion. No role, API policy or screen has been
changed by this review. The assessment is source-based; no privileged production
login or cross-tenant exploit was performed. Use the
[operation matrix](feature-capability-matrix.md) and
[test catalogue](../testing/edge-case-catalog.md) to drive implementation.

## Is authentication doing its job?

**The authentication foundation exists; the complete authorization model is not
ready to certify.** Authentication answers who signed in. Authorization must also
answer what they may do, in which tenant/branch, and to whose record.

What the code does correctly at a structural level:

- Browser uses Keycloak public-client login with PKCE S256; token stays in memory.
- JWT configuration validates issuer shape/server, signature, signing key and
  lifetime. Keys are fetched per realm. Tenant-scoped repositories ordinarily
  resolve the partition through authenticated issuer realm.
- API realm-role policies restrict writes; known tenant endpoints require active
  registration. UI sign-in/out and common token refresh paths exist.
- Public signing and QC use resource tokens rather than requiring tenant-staff
  accounts. These are capability links and should remain resource-limited.

The review found concrete gaps:

| Finding | Evidence / impact | Required action |
| --- | --- | --- |
| JWT audience disabled | `KeycloakJwt.cs`: ValidateAudience=false despite configured audiences | Issue API-specific audience in valid clients; enforce validation with an explicit separate provisioner flow. Test wrong/missing audiences. |
| System gate is realm-only | `SystemController.IsProvisioner` checks configured realm equality only | Dedicated platform/provisioner permission and audience, not every identity with a valid master-realm token. |
| Overbroad employee read access | staff policy=admin OR employee; employee list returns full Employee (pay/certifications/notes); bookings/invoices/agreement details accessible tenant-wide | Define self/branch/tenant scopes, safe DTOs and explicit billing/HR/document permissions; signer tokens are sensitive. |
| No subject→employee enforcement | Employee has KeycloakUserId and lookup helper, but controllers/handlers do not use it to scope requests | Bind realm+sub to employee; evaluate active flag and assignment/branch scope on each protected resource operation. |
| Invitation does not establish access | Invite handler sends registration link; provisioner has a user/role API but handler does not call it | Fix issue49 destination and safe account onboarding; persist identity binding, control roles, make reinvite explicit. |
| Employment flag and identity drift | Role changes/deactivation mutate Employee only; no corresponding Keycloak role/session change | Define authoritative permission source, synchronize role/lifecycle changes, test old token and refresh behavior. |
| Dev roles differ from provisioning | Dev import creates admin/staff; provisioner creates admin/employee; staff policy does not accept literal staff | Align dev fixtures/realm import/client claims with actual policies and migration aliases. |
| UI checks authentication only | Admin layout does not use isAdmin/permissions to filter routes/actions | Explicit role/capability presentation plus forbidden states; API remains final authority. |
| QC partition bypass | QC repository takes bare IDocumentSession instead of request tenant session | Wire tenant-bound persistence and prove A/B isolation before broader rollout. |
| Public URL-vs-JWT mismatch | Middleware favors authenticated realm over public URL slug | Define a consistent URL tenant policy, reject mismatch or resolve public slug consistently. |
| Refresh/cache gaps | Direct multipart/PDF fetches skip common refresh; admin cache key lacks principal/tenant | Centralize authenticated adapters, clear/scoped cache on logout/context change, preserve error recovery. |

The existing mocked Playwright suite cannot validate these permissions: its mock
auth turns authenticated identities into admin and uses a non-real JWT. Add real
tokens and negative API tests before calling auth correct. JWT logout/revocation
is also not synonymous with an immediately invalidated already-issued access token;
the application needs an explicit disabled-user/role-change enforcement policy.

Keycloak supports token audience mappers and realm/client/composite roles; these
can implement permission bundles. The recommendation is based on our code's
domain boundaries, not on adding role names for their own sake. References:
[Keycloak server administration](https://www.keycloak.org/docs/26.2.5/server_admin/index.html)
and [JavaScript adapter](https://www.keycloak.org/securing-apps/javascript-adapter).

## Recommended minimal roles

| Role | Purpose and scope | Allowed by default | Excluded by default |
| --- | --- | --- | --- |
| Platform administrator (`platform_admin`) | Operator of the SaaS, separate platform/provisioner identity domain | Provision/list/suspend/activate tenants; platform diagnostics | Automatic access to all tenant payroll/customer documents; tenant impersonation without explicit audited grant |
| Tenant administrator (`tenant_admin`) | Owner/admin of one tenant | Tenant settings, user roles, service catalogue, all tenant operations, billing and document workflows | Cross-tenant/platform lifecycle access |
| Operations manager (`manager`) | Delegated operational control | Bookings/shifts/assignments/customer locations/QC within assigned branches | Role grants, platform/tenant security configuration, private pay/bank/invoice details unless separately granted |
| Employee (`employee`) | Cleaner/worker | Own assigned work, permitted own profile fields and own QC submissions | Tenant-wide employee/customer/booking/invoice/signer-token lists and role changes |
| Billing capability or later `billing_user` | Optional delegation, only if a distinct person needs it | Invoice read/create/paid/void/PDF and minimum linked customer/booking fields | Operational assignment, staff roles, unrestricted HR notes |

Recommend assigned-branch manager scope as the starting proposal. A manager role
alone is not enough: server-side branch assignments must scope reads and writes.
Tenant admin can have tenant-wide operational scope. Whether managers can be
configured tenant-wide is a product decision pending discussion.

Do not introduce a second `super_admin` synonym unless its permissions differ.
"Super admin" can be the display name for platform_admin. Existing `admin`
means a tenant realm's admin; it must not silently become platform_admin.
Public customer lookup, external agreement signer and QC token holder do not need
staff roles simply to use their existing resource-limited links. A customer portal
would be a separate identity/product decision.

## Permission model proposal

Keep a small role set, but authorize business capabilities with resource scope.
Suggested capabilities, not currently implemented policy names:

- `tenant.manage`, `users.manage`, `catalog.manage` (tenant admin).
- `customers.read/manage`, `bookings.read/manage`, `shifts.read/manage`,
  `assignments.manage`, `quality.read/manage` (tenant admin or scoped manager).
- `work.read.own`, `quality.submit.own`, `profile.read.own` (employee).
- `employees.read.operational` distinct from `employees.read.sensitive`;
  `invoices.read/manage` and `agreements.read/manage` are explicit grants.
- `platform.tenants.manage` is platform-only and not inherited from tenant admin.

Every decision evaluates token issuer/audience/claims, active tenant, active bound
user, capability, and resource ownership/scope. A role hierarchy does not replace
tenant_id/branch_id/employee_id checks. Do not use client-supplied employee or
branch IDs as proof of ownership. Sensitive fields need explicit DTOs even when
the enclosing endpoint is allowed. Return normalized effective permissions from
an identity endpoint so FE menus and BE authorization share a contract.

### Migration and testing

1. First close audience, platform gate, QC partition and broad-data-exposure gaps.
   Do not rename roles before these boundaries work.
2. Introduce explicit permissions and safe DTOs with an agreed endpoint matrix;
   review sensitive reads before delegating manager actions.
3. Map legacy admin to tenant_admin only. If compatibility aliases are needed,
   make them temporary, documented and tested; never grant platform privileges.
   Align dev import, provisioning, claims and Employee.Role validation together.
4. Bind genuine realm+sub to employee identity; add managed-branch assignments
   and self-work endpoints; define invitation/reinvite/disable/demotion handling.
5. Add FE permission-aware navigation and dedicated My work. Keep API negative
   tests for employee, manager, tenant admin, platform admin and no-role users.
6. Exercise same tenant/different branch, different tenant, disabled user, stale
   token, expired token, wrong audience and removed manager assignment on every
   sensitive operation. Audit permission and financial/document lifecycle changes.

## Recommended feature grouping

Current top-level modules mirror tables more than user work. Retain distinct BE
models but group UI workflows around jobs, customers, staff and quality:

| Workspace | Suggested contents | Why / what moves |
| --- | --- | --- |
| Operations | Bookings, recurring shifts, calendar and employee assignments | One place to see work/capacity; keep booking and shift entities distinct and explain which is assigned |
| Customers | Companies, branches/locations and links to associated jobs/documents | Complete customer lifecycle in one detail view; replace disconnected company/branch actions |
| People | Employee profile, availability/qualifications, invite/access status | Employment data and identity lifecycle visibly connected; sensitive fields gated |
| Quality | Templates, dispatched forms, submissions/photos, monthly reports | Full checklist lifecycle together; link from shift detail and employee My work |
| Agreements and Billing | Agreements and invoices as distinct tabs/sections, linked from customer/job detail | Related commercial records but different states/permissions; avoid one generic document CRUD model |
| Settings | Catalogue/services, tenant issuer/contact/timezone/booking policy and content | Catalogue changes are configuration, not scheduled work; allow operational shortcuts to service editor |
| My work | Employee's assigned jobs/shifts, own checklists and limited profile | Employee should not need the all-tenant admin dashboard |
| Platform | Tenant registry/provisioning/lifecycle | Separate trusted platform workspace, never a tab automatically available to tenant admins |

Alternative: keep Services at top level for frequent catalogue changes while adding
Operations/Customers/My work. This is lower navigation churn but retains more
top-level modules. Recommend workflow grouping with shortcuts; discuss Services
placement and manager scope before moving screens.

Do not merge bookings with recurring shifts into one storage entity, or agreements
with invoices into generic files. They have different lifecycle/financial/snapshot
rules. Reuse editors/components and link detail pages instead of duplicating CRUD.
Do not rewrite realm-per-tenant architecture merely to add manager roles; harden
current partition/identity boundaries first.

## Discussion decisions

- Managers assigned branches only, tenant-wide, or configurable? What can they see
  about employee pay and customer invoices?
- Is billing a delegated job today, or can tenant_admin own it until demand exists?
- Should service catalogue stay top-level, move under Settings, or have both entry
  points through one editor?
- Which missing screens unblock daily operations first: bookings/calendar,
  company/employee editing, billing, or QC review/automation?
- Should signer/QC links expire and how should disabled users or revoked assignment
  affect previously issued links? Keep external signing and staff sessions distinct.

Concrete defects and missing operations are GitHub implementation issues.
Role scope, workspace moves and business lifecycle policies are discussion issues
until these decisions are approved. The issue register links both kinds to the
feature gaps and Playwright case IDs.
