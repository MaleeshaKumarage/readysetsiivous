# Active and Inactive lists

Admin agreements, shifts, employees, services, companies, expanded branches and checklist templates have Active and Inactive tabs. Manager shifts use the same control. Active is the initial tab. Classification uses each record's `isActive` flag; signing status, completed shift tasks and report history are separate.

Signed and partially signed agreements remain available to view and download in their matching tab. Filtering never deletes, deactivates or rewrites records, signatures, signing tokens, PDFs or history. Existing lifecycle actions and authorization remain unchanged: shifts and services retain their existing restoration actions; adding employee/company/agreement activation workflows is outside this UI change.

Management lists request inactive records where supported. Employee assignment selectors continue requesting active employees; manager permissions remain tenant-wide. Companies load every page through the existing API before displaying counts or filtered records. A failed, incomplete or inconsistent company page shows an error and Refresh action rather than a misleading partial list. Branch lists have their own tabs and reset to Active when a different company is expanded.

Tabs switch locally without an API reload. Counts reflect successfully loaded data; selected-tab empty states differ from a genuinely empty dataset. Creating an active record returns to Active. Existing deactivation/restoration actions move records to their matching tab after reload. Changing shift tabs closes assignments and discards pending roster responses, preventing hidden or stale assignment UI. Keyboard arrows, Home and End move between tabs; tab/panel associations and mobile layouts are shared.

Selecting the current tab again, including Home/End when already at that end, preserves expanded views and selection.

Checklist-template activity tabs affect only the template list. Editing an inactive template retains its activity and company/branch scope. Submissions, monthly summaries, PDF exports and public reports retain their existing historical filters.

## Permanent deletion

Each inactive record now has a Delete action. The API requires the record to be inactive and checks references before removing it. A conflict response explains why the record must stay. Companies with branches or business records, branches with shift/checklist history, shifts with assignments or forms, employees with work history, services used by bookings, and templates referenced by shifts/forms are retained. Employee profiles linked to a Keycloak login are retained; deleting a profile does not silently delete an external identity.

Agreements containing any signature, a signed PDF, or a partially/completely signed status are never deleted. Draft or cancelled unsigned inactive agreements can be deleted. Signed PDFs and signer evidence are preserved. Deleting an unsigned agreement removes the tenant record; its local source file may remain on disk for later storage cleanup.

Delete is tenant-admin-only for admin lists. Tenant-wide managers can delete inactive shifts through the manager API; the same shift history guard applies. Active records must first be deactivated. UI confirmation is required, while API checks remain authoritative against direct requests and stale pages.

## Playwright cases

- Default Active, exclusive Inactive content, switching back without extra reads, and keyboard selection.
- Draft, completed and partially signed agreements in both lifecycle states; preserved signer views and PDF downloads.
- Inactive employees visible in management, without invite/deactivation actions or selection in active assignment rosters.
- Shift create from Inactive returns to Active; deactivation/restoration transitions; delayed assignment responses after tab changes are ignored.
- Companies beyond the first100 records, missing/failed/inconsistent pages, fresh retries and scoped branch tabs.
- Service activation/deactivation, inactive-template edit preserving scope, create returning to Active, and unchanged report/submission navigation.
- Inactive Delete actions for every lifecycle list; active-record rejection; referenced-record conflict messages; clean records disappear after deletion; signed/partially signed agreements remain protected; linked-login employees remain retained.
- Desktop/mobile, empty selected tab, all-empty initial state and existing permission denials.
