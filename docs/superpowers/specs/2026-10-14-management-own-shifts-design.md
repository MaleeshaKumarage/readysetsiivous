# Own shifts for administrators and tenant managers

Admins and tenant-wide managers may also perform assigned cleaning work. Use the
existing shared `/me/shifts/` screen and `/api/v1/me` clock endpoints for every
staff role. Do not add an employee realm role to an admin or manager: current
binding checks deliberately enforce the stored role and JWT role agreement.

## Current behavior and chosen approach

The API staff policy already includes admin, manager and employee. Its middleware
loads the current employee by the authenticated subject, verifies active status,
email verification, synchronization and role agreement. Own-shift handlers use
that verified employee ID, not a client-supplied employee ID. Assignment selectors
accept all active employee profiles. Account landing and the manager workspace
already expose My shifts. The admin workspace lacks a direct navigation entry.

Keep one shared clock/checklist flow with management navigation entry points.
Separate role-specific clock screens duplicate behavior and reporting. Bundling
an employee realm role with management roles conflicts with lifecycle checks.

## Change

After the admin layout successfully verifies identity, expose a My shifts link in
the header for an active linked admin profile, visible on desktop and mobile. Reset
this eligibility when verification begins or fails, so stale identities cannot
leave the link visible. An unbound tenant owner retains administration and does
not get a fabricated work identity. On their account landing, explain that a
linked administrator employee profile is needed to receive shifts; point to
Employees and the existing invitation/linking flow. Matching verified existing
accounts can be linked through that flow without changing their management role.

Managers retain their existing My shifts entry point, shown only after verifying an active linked work identity; pending, denied or failed verification clears it. The shared screen records
only their own assigned occurrences, actual start/end times, selected activities,
optional general notes and individually annotated photos. Existing checklist,
reporting, timing, assignment and tenant restrictions continue to apply.

## Verification and documentation

Add signed-token API regressions for all three staff roles: own shifts, start and
end use the verified employee ID even with forged employee query/body fields;
unbound admins cannot clock work; inactive/pending/unverified/stale-role staff
cannot read/start/end; cross-user task completion remains denied by the existing
handler. Management privilege does not authorize another person's self-service
clock operations. Managers cannot manage templates or monthly report sharing.

Playwright covers admin and manager navigation into real clock UI, successful
start/partial activity selection/end and recorded state on desktop/mobile; admin
return navigation, unbound-admin guidance and missing/denied identity checks.
Retain existing employee and assignment tests and document profile prerequisites
and all role-specific cases for future Playwright improvements.

Only change navigation, prerequisite guidance, coverage and documentation unless
an actual backend restriction is found. Do not widen management access or create
profiles automatically. Merge passing checks and deploy the relevant components.
