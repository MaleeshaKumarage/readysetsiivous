# Personal shifts for every staff role — issue117

Tenant administrators, tenant-wide managers and employees use the same My shifts
screen at `/me/shifts/`. An administrator can assign any active linked employee
profile, including admin/manager profiles, through the normal shift roster.
Keep that person's Administrator or Manager role; adding an Employee realm role
is unnecessary and can conflict with identity checks.

The administrator header and manager workspace expose My shifts after verifying
a current active linked profile. Account landing offers the same entry point.
The clock screen has My account navigation, from which management staff can return
to administration or tenant operations. Desktop and mobile use the same flow.

An original tenant owner without an employee record retains administration but
cannot receive/clock work until their existing login is linked to an active
Administrator employee profile. Add the profile with the same verified email and
Administrator role, then use the existing Invite action. Verified matching users
are linked without replacing their passwords. Conflicting, unverified, disabled
or already-owned identities require explicit reconciliation. Account landing
explains this prerequisite; no profile or role is created automatically.

The `/api/v1/me/shifts` and clock APIs already allow all three roles. They resolve
identity from the verified login subject and enforce current binding, email
verification, role and lifecycle. Client-supplied employee IDs cannot select
another person's work; management privileges do not bypass own-task ownership.
An unbound admin gets404 from own-work clock routes; invalid linked identities
get403. Missing assignments produce an empty schedule, not a permissions grant.

Every role may record any subset of checklist activities, including none, with
optional general notes and a separate required note for each photo. Existing
occurrence timing, assignment/parent activity, conflict/retry and report rules
apply. Submitted management staff work contributes to reports like other assigned
staff. Only tenant admins create/assign templates and download/create/revoke
monthly report shares; managers retain operational permissions only.

Coverage: signed-token API self-clock/forged-ID/cross-owner/lifecycle regressions,
real shift-start handlers for all roles, and desktop/mobile management navigation,
start/select/end, account return, empty assignments and unbound/denied shortcuts.
See [edge cases](../testing/edge-case-catalog.md#personal-shifts-for-management-staff--issue117).
