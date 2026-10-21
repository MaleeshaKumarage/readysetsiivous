# Leads management without email integration

Date: 2026-10-21

## User scope

Create Leads with the same company and branch information as the current management forms, multiple email addresses, a description and at most three uploaded images. A lead can be upgraded to a customer company. The user explicitly removed email history from scope: no Resend changes, inbox connection, email sending, conversation timeline or tracking.

## Workflow and fields

A lead represents one prospective company with one prospective branch/location for this first release. Fields: company name, optional business ID, contact name, company phone, company notes, branch name, street, postal code, city, country, branch phone and optional branch contact email; description; a list of company contact emails with one primary email; zero to three images. Company name is required when saving; business ID and branch name are required at conversion. Emails may be empty during prospect entry. When present, normalize/trim and deduplicate addresses case-insensitively, validate each, and select exactly one primary. Cap at 20 addresses, each 254 characters. Reuse existing company/branch field bounds; description maximum 10,000 characters. Plain text descriptions/notes, escaped in UI.

Company email list and branch contact email remain distinct. Conversion uses selected primary for Company.ContactEmail and the branch email for Branch.ContactEmail. Every additional email and all entered lead details remain on the preserved linked lead; this release does not alter the existing company contact API/model. Clearly label this in conversion confirmation and customer link.

Lead status: New, Contacted, In discussion, Lost. Conversion sets Converted and records CompanyId, BranchId and ConvertedUtc. New leads start New. List supports name/business-ID/email search and status filter, with newest updated first and stable ID tie-breaker, server pagination. Admin can create, inspect and edit unconverted leads. Editing does not clear saved images or silently discard unsaved form fields on failure. Converted leads are read-only snapshots and show Open customer. No pipeline automation, follow-up scheduling or marketing features.

## Lifecycle and permissions

Tenant admins only, consistent with the existing companies endpoints. Managers, cleaners and anonymous users cannot read or mutate leads or images. Tenant comes only from the authenticated tenant context, never a caller-supplied tenant ID. Active/inactive filters and deactivate/reactivate are available for unconverted leads. Converted leads remain retained in the Converted filter. Only inactive unconverted leads can be permanently deleted after explicit confirmation; converted leads cannot be deleted through lead management, preserving the origin. Inactive leads must be reactivated before conversion/editing state transitions; field editing on inactive unconverted leads is allowed, matching existing management behavior.

## Images

JPEG/PNG/WebP, maximum 5 MiB each and three images total per lead. Reuse actual-content image validation and current dimension/pixel limits; no SVG or arbitrary file URL acceptance. Persist only generated image IDs and validated extension metadata. Private tenant/lead storage directory, not the public service-image uploads route. Authenticated admin image download checks lead and image membership before returning bytes. Frontend loads through authorizedFetch and revokes temporary object URLs when replaced/unmounted. Show thumbnails, upload progress/error and a Remove action.

Create metadata first, then allow uploads from saved lead detail. Saving metadata and uploading images are explicitly separate operations; if an upload fails the saved lead remains and the error permits retry. Upload/remove mutations use optimistic concurrency and return updated version/count. Concurrent fourth upload must fail without orphaning stored metadata. Reject more than three files before sending where possible and enforce server-side independently. Generated file paths never use uploaded filenames. On failed upload persistence, remove only that unreferenced newly generated file. On image/lead deletion, commit metadata removal then perform idempotent cleanup; cleanup failure must not restore deleted metadata or expose the file, and is logged. No lead images are exposed publicly on conversion.

## Conversion

Admin presses Convert to customer, reviews the company/branch fields and primary company email, and confirms. Any missing required conversion fields are corrected on the lead first. Create one active Company and one active Branch plus mark the Lead Converted in a single tenant-scoped Marten transaction. Do not call existing repository SaveAsync methods sequentially, as they commit independently. Conversion preserves the original lead record/images and links it to customer IDs. Company notes receive company notes and description without losing either; the unchanged lead remains the full provenance record. Existing company and branch validation is reused at conversion.

Business ID uniqueness includes inactive companies in the same tenant. On conflict, create nothing and show a clear existing-customer error; no silent merging or overwriting. Linking to an existing company is outside this release. Simultaneous conversion attempts create at most one company/branch; retries return the existing conversion result. Use optimistic concurrency and the unique company business-ID index; after an overlapping request completes, reload the lead before returning a conflict/result. Stale form/version returns 409 and requires refresh without silently overwriting changes. Conversion and normal edits must serialize consistently through expected document version checks. If a converted customer/branch later disappears, show historical converted status and an unavailable-customer message rather than offering a second conversion.

## Boundaries and integration

Domain Lead/LeadImage metadata, Application lead DTO/validation/service and repository/file-store contracts, Infrastructure tenant-scoped repository with atomic conversion and disk file store, API admin controller. Register Lead as conjoined tenant document with optimistic concurrency, searchable name/business ID/status fields. Add Leads to the admin navigation/dashboard, with a responsive list, edit/detail dialog and conversion confirmation. Keep images out of list responses; detail returns metadata only. Use standard ProblemDetails with actionable validation and 404 for foreign/missing IDs. Client mutations invalidate leads and company caches; no cache includes private image bytes.

API under /api/v1/admin/leads: paginated GET, POST create, GET/{id}, PUT/{id}, POST activate/deactivate, DELETE/{id}, POST/{id}/convert, POST/{id}/images, DELETE/{id}/images/{imageId}, GET/{id}/images/{imageId}. Mutations carry the expected lead version, except create. Image routes reject converted/deactivated lead uploads/removals, but existing images stay readable for admins. No public endpoints.

## Acceptance and verification

Backend: field/email validation and primary selection; empty/multiple/deduplicated emails; inactive editing/lifecycle; image type/size/count/content validation including simultaneous cap; tenant and role isolation; atomic conversion and retry, duplicate inactive/active company IDs, race/stale conversion, no partial customer on failure; preservation of descriptions, extra emails and image metadata; converted read-only and delete guard; generated-path and cleanup behavior. Real PostgreSQL regression verifies conjoined tenant persistence, optimistic concurrency and all-or-nothing conversion.

Playwright desktop/mobile: navigation and list/status/search/pagination, create/edit with multiple emails and primary selection, detail and validation failures preserving values, three-image upload/view/remove and fourth rejection/retry, active/inactive/delete behavior, conversion success and open-customer link, missing-field and duplicate-company errors, repeat conversion safeguards, read-only converted data. Typecheck and production frontend build plus full required CI/review before merge/deployment. All test/demo data synthetic; no production leads or customers created for testing.
