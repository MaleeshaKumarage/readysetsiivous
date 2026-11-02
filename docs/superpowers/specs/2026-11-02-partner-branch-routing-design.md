# Partner branch routing

Approved additions: administrators/managers may configure an optional active, synchronized, login-linked staff cleaner on a branch. New partner requests route directly to that cleaner using the existing offer workflow, without staff broadcasts. Existing requests and frozen prices stay unchanged. Invalid configured cleaners fall back to staff, and declined offers return to staff. Cleaner self-service approval preference defaults to approval required.

Auto-accept requires explicit cleaner opt-in and a concrete legal appointment; reuse acceptance scheduling checks under the existing reservation, and retain an actionable offer when validation fails. Flexible requests never invent start times. User confirmed exact-time-only automatic acceptance; flexible requests require the cleaner to choose/approve a time. No branch default time is added.

For partner-enabled companies only, new/edited branch titles are generated from Street, PostalCode City. Existing titles are not bulk-renamed. Branches expose optional square meters, existing address/room/bed/pricing data and preferred cleaner; partner creation cannot set prices or cleaner. Update omission preserves optional fields; explicit null clears. Branch lists sort by postal code then name.

Implementation: extend persisted documents and compatible DTOs; validate preferred staff in tenant-scoped handlers; add verified self preference endpoints; extract shared acceptance staging; route new requests with existing revision/idempotence and notifications; integrate generic employee push; verify domain, compatibility and database workflow tests.
