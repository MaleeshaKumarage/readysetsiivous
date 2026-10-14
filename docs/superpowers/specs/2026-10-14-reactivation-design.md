# Reactivation and duplicate-email design

Restore existing records through explicit tenant-admin-only Reactivate actions on inactive employees, agreements, companies, branches and QC templates. Existing shift and service actions remain and are verified. Dedicated status commands must preserve all unrelated fields; avoid replacing complete records with list DTOs. Reactivation does not restore old assignments or send invitations automatically.

Employee reactivation uses the existing identity lock/reload/sync-pending protocol, keeps profile ID, login ID, role, history and advanced fields, and synchronizes the linked login enabled state. Retrying must heal pending identity synchronization. Duplicate email conflicts include inactive profiles, trim/case normalization, and a clear instruction to use the Inactive tab and Reactivate rather than create another account. UI retains form inputs on failures and settles busy state.

Agreement reactivation changes visibility/access state only, never signatures, PDFs, signer tokens or completed status. Cancelled agreements cannot be reactivated into signing; return a clear conflict. Signed and partially signed evidence is always preserved. No cleanup or company-association repair.

Options considered: full-record update risks erasing advanced metadata; silently create a new identity duplicates history; dedicated status actions are recommended to preserve fields and express authorization clearly.

Verify successful and failed reactivation, duplicate active/inactive email, case/whitespace, linked/unlinked identity and pending-sync retry, role/tenant negatives, metadata preservation, cancelled/signed agreements and existing shift/service actions. Update feature and Playwright edge-case documentation. Run appropriate BE and browser tests, PR CI, merge and deploy under existing user authorization.
