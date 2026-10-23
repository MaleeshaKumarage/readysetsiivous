# Leads: optional multiple branches and separate image tabs

Date: 2026-10-23. User approved this design in chat.

## Scope

Extend the current Leads release while completing voice notes and follow-ups. New leads have no branch by default. Admins explicitly add any number of branches, edit their fields, or remove draft branches. This is distinct from customer/company branch administration. Managers can view all lead branches, but metadata management remains admin-only under the approved collaboration permissions.

Each branch has an immutable generated GUID within its lead, name, street, postal code, city, country, phone and email. An explicitly added branch requires a name when saving; keep the existing field and email bounds. Do not impose a small product count cap. Bound HTTP payload size and request processing; use paginated/collapsible lists in the UI as needed for usability. No implicit branch is added on opening, saving or converting a lead.

## Backwards compatibility

Previously stored leads have one set of flat branch fields. On reading, expose that branch as a one-entry collection only when meaningful branch data exists (name/address/contact fields, not the old default country alone). Use a deterministic legacy branch ID derived from the lead ID. Preserve the original flat fields in stored historical documents until an explicit metadata update normalizes them. DTOs and the new UI consume the resolved branch collection; avoid an automatic write migration or changing signed/customer records.

On update, an explicit empty branch collection means remove draft branches and must not resurrect the old flat branch. A missing collection from an older client preserves existing additional branches: meaningful flat input updates only the projected first branch while retaining its ID. Empty flat input preserves existing branches. On a zero-branch lead, meaningful flat input adds a first branch; a missing collection with no meaningful data keeps zero. Only an explicit collection can remove branches. Branch IDs are unique within a lead and validated before persistence. Existing flat fields remain available for old clients as a projection of the first branch, but they are not a second source of truth when the collection is present. Converted lead snapshots resolve their original branch information without rewriting existing customers.

## Conversion

Business ID and company name remain required; a branch is optional. For zero branches, atomically create the company and mark the lead converted with an empty branch-ID list. For one or more, validate every branch and create all branches with the new company ID in the same Marten transaction as the company and converted lead. Preserve source branch IDs alongside created company branch IDs in a conversion mapping. Do not silently skip a bad branch or create a partial company.

Keep the legacy `BranchId` as the first converted branch for compatibility, and add the complete converted branch mappings. Repeat conversion returns the same existing company/mappings, including zero-branch conversions. Customer availability checks a linked company and all expected linked branches; zero-branch conversions check the company alone. Historical legacy conversions use their original `BranchId` when the mapping is absent. Duplicate active/inactive company business IDs, stale edits and tenant isolation retain the existing behavior. Lifecycle cancellation of follow-ups participates in this same transaction.

## Image tabs

Lead details initially select View images. That tab has private thumbnails and open/playback links only, without upload inputs or remove buttons. Manage images is separate, admin-only, and contains uploads and explicit removal with confirmation. Switching tabs does not remove images or discard unsaved metadata/recordings. Existing three-image, five-MiB, actual-content and private-download rules remain. Converted leads show view-only history; no management tab. Inactive unconverted leads can view images; management explains that reactivation is required rather than exposing destructive image actions. Managers always see view-only images.

## Verification

Backend: zero/one/multiple branches, unique IDs and invalid branch values, legacy/default-country mapping, explicit empty collection, old-client input, atomic all-branch conversion, duplicate-company rollback, idempotent zero/multiple conversion and concurrency, tenant isolation, converted historical availability and follow-up cancellation.

Playwright desktop/mobile: default View tab has no upload or remove controls; Manage tab requires deliberate selection and confirms deletion; switching does not mutate images; upload/retry/cap still work; converted/inactive/manager restrictions. New leads show no branches; Add branch creates explicit drafts; add/edit/remove several branches, validate names, save and reload, display all branches on manager view and review conversion with zero and multiple branches. Existing voice/follow-up workflows continue to pass.
