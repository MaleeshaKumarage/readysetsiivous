# Visit assignees and recurring handovers

The Occurrences popup shows effective assignees for each visit. Admins can preview and save Replace, Add extra or Restore recurring assignment for a single visit. Reasons are mandatory. From this date onward supports Replace or Add extra from midnight in the tenant timezone; Replace removes later recurring handovers while retaining individual visit overrides. Restore assignment is independent of restoring the visit schedule.

The operational API is also available to tenant managers under the existing manager policy: PUT /api/v1/{admin|manager}/shifts/{id}/visit-assignees. Staff self-service routes remain limited to their own resolved visits. Template/report administration remains admin-only.

The request uses originalStartUtc for scheduled/moved visits or extraExceptionId for extras; exactly one is required. mode is Inherit=0, Replace=1 or Add=2. employeeIds, reason, rosterVersion and assignmentVersion are validated on every preview/save. fromThisDate selects handover, preview prevents writes. A stale roster returns a conflict requiring reload. All selected employees must be active and belong to the tenant.

Past dates and started/submitted visits cannot be reassigned. Cleaners can nevertheless start past active assigned visits and record actual work now. Cancelled visits must be restored before assignment. Explicit dated periods use visit-start membership; overnight work starting before midnight stays with the outgoing employee. Regular legacy assignments cover the shift's configured start date, including backdated creation.

Calendars, clock-start authorization, checklist dispatch and report expectations use the effective roster. One-visit overrides take precedence over recurring periods; Inherit also suppresses legacy single-employee overrides. Orphan unstarted draft checklists do not grant access. Duplicate employee selections are deduplicated, overlapping visits are rejected atomically, and adjacent visits are allowed.

Clock starts and scheduling writes share a tenant-scoped PostgreSQL reservation across API replicas. Real database tests cover both race outcomes, partition isolation and archived overrides. Historical QC evidence and existing shared-report snapshots remain intact. Reports apply moves at the destination, preserve the original plan attribution, omit cancellations from expected work and include extras once.

Conflict preview checks the full dated shift range up to ten years; for open-ended handovers it checks one year and states the checked-through date. As with the existing bounded recurring-assignment policy, scheduling beyond that horizon must be checked when changing assignments.

Regression coverage: Replace/Add/Restore, duplicate selection, preview without writes, required reasons, stale versions, inactive employees, cancelled/past/started work, one-off preservation, handover history, same-shift extra collisions, adjacent visits, cross-month reports, tenant scope and real database concurrency; desktop/mobile preview, failure/retry and handover flows.
