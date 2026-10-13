# Authenticated shift activities and no QC emails — issue93

User direction: employees log in, see assigned shifts, start them, select the
activities performed and end them on the same screen. No QC form emails are sent.
Account-setup invitations remain separate. Implementation is authorized without
additional user approval gates. Base: latest main `68dedb7`.

The existing authenticated `/me/shifts` UI and start/end API remain the workflow.
The account landing gives active bound employees a prominent My Shifts link and
explains this flow, replacing the instruction to use emailed checklist links.
Legacy unbound tenant admins keep their administration link and do not get an
invented employee work identity. Tenant selection survives account-to-shifts
navigation through the existing auth tenant context.

The dispatcher continues to generate upcoming task snapshots for assignments,
template changes, manual recovery and the periodic worker. Remove its email
sender/configuration dependency and all outbound notification/retry logic. Keep
idempotent task IDs, occurrence timing, scope validation and preservation of
started/completed snapshots. Historical delivery fields are retained as evidence,
not consulted to send further messages. Booking/account mail remains unchanged.

The shift activity controls use native labeled checkboxes so clicking a checkbox
or its row changes the selection once. All unchecked and partially checked
submissions remain valid without mandatory notes. Start/end use the authenticated
employee binding; task templates and reporting remain tenant-admin-only. Monthly
shift/branch counts, PDFs and admin-created 30-day report sharing remain intact.
Historical public checklist routes are not a newly issued workflow; this change
does not email or encourage their use.

Verify worker/recovery idempotency and snapshot preservation without mail,
assignment/manual-generation regressions, account navigation, real employee login
and own shift API flow, and desktop/mobile start/select/end (none/subset/all),
including failed start/end retry without losing selected activities. Run the full
backend/browser regression and type/build checks, resolve PR review, merge and
verify production before closing93. Employee administrative scope65 stays on
its existing branch and resumes after this explicitly requested correction.
