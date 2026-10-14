# Reactivation across management lists

Tenant admins can use Reactivate on inactive employees, agreements, companies, branches, services and checklist templates. Admin/tenant-manager shift lists already offer Reactivate with company/branch active guards. Lifecycle changes preserve the same IDs, history and metadata. Child statuses remain independent: restoring a company does not activate its branches or shifts.

Employees retain their existing Keycloak login and role. No new identity or setup/QC email is created during reactivation. Linked access synchronization must succeed; pending synchronization denies employee access. A failed reactivation leaves a Retry reactivation action even when the profile was persisted active before the external identity call. Creation with an existing active or inactive email returns 409; inactive conflicts direct the admin to Inactive and Reactivate. Matching ignores case and trims input.

Agreement reactivation preserves original/signed PDFs, signer tokens and signatures and does not change signing status. Cancelled agreements cannot reopen (409, disabled control). Signed and partially signed records are never removed or repaired by these actions.

Company/branch/service/template actions use status-only commands to avoid replacing metadata from incomplete list DTOs. QC UI Deactivate replaces physical Delete; legacy DELETE API remains separate. Restoring templates preserves company/branch scope, items and already generated form snapshots.

Playwright edge cases:
- REACT-001: linked/unlinked inactive employee restore, same identity/history, no invitation.
- REACT-002: external login synchronization fails after active save; visible error, settled controls, persistent retry heals pending state.
- REACT-003: active/inactive duplicate email (case and whitespace), 409 guidance, retained create inputs.
- REACT-004: signed/partially signed agreement restore retains evidence and status; cancelled remains blocked and PDFs readable by admins.
- REACT-005: company and branch restore independently; missing/foreign parent rejected without writes.
- REACT-006: QC restore/deactivate preserves items/scope/snapshots; failure and retry; no physical deletion.
- REACT-007: service restore/deactivate sends no metadata payload and preserves translations, additional info, currency and image.
- REACT-008: existing admin/manager shift restoration, inactive-parent conflicts, role/tenant denials on status endpoints.

Browser suites: lifecycle-reactivation-people.spec.ts, lifecycle-reactivation-entities.spec.ts, lifecycle-service-reactivation.spec.ts, existing shift-reactivation.spec.ts and manager.spec.ts. Backend suites cover full metadata equality, pending synchronization retry, duplicates, parent rejection and authorization. Browser mocks are not proof of live identity-provider behavior; CI backend fixtures and existing real identity tests remain separate.
