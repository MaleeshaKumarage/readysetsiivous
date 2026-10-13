# Tenant manager permissions — issues65/82

Status: user-authorized implementation. The user's latest clarification is that
managers operate across their tenant, not assigned branches. This supersedes the
branch-manager deferral in the earlier issue65 spec. Baseline main82d2cb7 includes
the authenticated shift activity correction. The existing issue65 self-scope and
explicit admin DTO work is retained.

## Decision and alternatives

Use a dedicated tenant `manager` role, safe operational APIs under
`/api/v1/manager` and a separate `/manager/` workspace. Reusing the full admin
workspace would expose financial/HR contracts and make individual field/action
restrictions fragile. Branch membership was considered and explicitly rejected
by the user. No new branch-assignment model or platform privilege is added.

## Permission matrix

- Tenant admin: existing own-tenant administration, including manager role grants,
  employee lifecycle/invitations, HR/payroll, billing, agreements, settings,
  company/branch edits, QC templates/assignment and monthly PDF/share management.
- Active verified bound manager: read operational companies/branches, safe roster
  and shifts across their tenant; create/edit/deactivate shifts; assign/unassign
  employees. Managers can also use their own profile and assigned shift clock.
- Cleaner: own profile and current assigned work/clock only. No manager/admin APIs.
- Platform identity: existing dedicated platform audience/permission; no implied
  tenant operational or sensitive access. Anonymous/no-role identities denied.

Manager scope derives solely from the current tenant session and current persisted
Employee.Role, matched against verified token claims. Unbound managers, inactive,
unverified or pending-sync identities are denied. Old manager tokens immediately
lose access after demotion/deactivation; stale employee/admin tokens cannot gain
manager access merely because the employee document changed. No caller tenant or
employee id may change the authenticated scope. Manager access is tenant-wide;
resource GUIDs from another tenant return404 and remain untouched.

## Contracts and operations

All administrative routes stay tenant-admin-only. Manager endpoints never call
employee admin lists or return persistence objects. Manager roster allowlist:
id, first/last name, phone, active status, display color, skills, service areas and
default hours. Exclude email, pay rate, certifications, HR notes, identity keys,
invitation metadata and persistence version. Company list: id/name/active only.
Branches: id/company id/name/active/address/contact phone. No business id,
company notes, billing, customer email, signer or report capability tokens.

Shift read/write allowlist: id, company/branch id, name, explicit typed schedule,
active state and optional validity dates. Assignment response: id, shift/employee
id, assigned UTC and active state. Internal assignment notes and QC plan/history,
form tokens and template reference are excluded. Schedule nested times and daily
ranges use explicit records, not mutable domain types for JSON output.

Manager create cannot set a template or notes. Manager edits preserve existing
notes, QC template reference and snapshot/history, using one loaded versioned
shift and optimistic concurrency; a concurrent admin template edit must conflict
rather than be overwritten. Manager edits keep the existing company/branch;
changing a shift's parents is tenant-admin-only to preserve template ownership.
Schedule edits use existing recurrence validation, record plan history where
applicable, and ensure future tasks without sending email. Deactivation retains
history. Assign/unassign reuse validated existing handlers, including active
employee/shift and overlap constraints. Parent/employee resources are resolved
inside the current tenant. Inactive or mismatched parents must be rejected.

Unknown mutation fields (role, employee credentials, notes, template, parent ids
on edit) are rejected with400. Managers cannot call template/report/PDF/share,
invoice/agreement, settings, services, role/invite/employee mutation APIs. Their
workspace exposes only supported operational controls and My shifts/account.
It verifies current identity before operational requests and handles denial,
load/save failures and retry visibly. No broad admin child mounts or data fetches.

## Role provisioning and compatibility

Add `manager` to Employee.Role validation, tenant realm provisioning and managed
role synchronization. Existing admin/employee values keep their meaning. Only
admins can select manager in employee administration and invite it. Existing
realms need the manager role created idempotently by the admin-owned invitation/
role-sync path or the documented realm preparation; creating an empty role does
not grant it to existing users. Reconciliation removes other managed roles while
preserving unrelated Keycloak roles/attributes. Dev bootstrap role validation and
docs must include manager consistently. No blanket platform/tenant role rename.

## Verification and completion

- Signed-token HTTP negatives for every admin read/write/PDF/capability family and
  every manager route; no mediator/repository sensitive query on denial.
- Exact nested serialized allowlists with payroll/identity/bank/token markers;
  manager create/update forbidden fields and template/history preservation.
- Active/inactive/missing/foreign parents, shift and employee; stale role/lifecycle,
  cross-tenant GUID reads and writes, role transition cleanup and owner binding.
- Genuine owned PostgreSQL/Keycloak manager login across two branches in one
  tenant, a second tenant isolation negative, shift create/edit/assignment flows,
  denied reports/payroll/role grants, and old-token rejection on demotion.
- Desktop/mobile manager account navigation and workspace controls: create/edit,
  cancel, deactivate, assignment/removal, retry failures and absence of admin/QC
  report/financial controls. Cleaner clock and all admin regressions remain passing.
- Full relevant tests, type/build, independent review, PR checks and production
  website/API actual commit verification before marking issues complete.

Issue65 includes this manager work plus its existing safe self/admin contracts.
Issue82's tenant-manager decision is implemented here; wider optional delegated
capabilities are not granted implicitly. Existing broader My Work UI remains70.
