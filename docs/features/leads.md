# Leads

Tenant admins can manage prospective customers at `/admin/leads/`. Managers and employees have no access to these records or their images.

## Capture and manage

Create a lead with a company name. Store business ID, company contact/phone/notes, one prospective branch and its address/phone/email, a description and up to 20 distinct company contact emails. Choose one primary company email when addresses are present. Business ID and branch name can be supplied later but are required for customer conversion. Fields are trimmed; emails are deduplicated without case sensitivity.

Filter Active, Inactive, Converted or a sales status (New, Contacted, In discussion, Lost); search company name, business ID or contact emails. Lists are paginated. Inactive leads can be edited and reactivated. Only inactive, unconverted leads can be permanently deleted, after confirmation. Errors preserve entered fields; Reload lead explicitly discards local edits after confirmation.

## Private images

Save a lead before uploading. Add up to three JPEG/PNG/WebP images, maximum 5 MiB each, 4096 pixels per dimension and 20 million pixels total. The backend checks decoded image content; file extensions alone are insufficient. Detail images use authenticated admin downloads and cannot be opened through the public uploads directory. Images can be viewed/removed on active unconverted leads. Inactive/converted records retain view access but cannot change images.

Uploads run individually with progress. If a batch partially fails, successful uploads remain attached and the UI permits retrying remaining files. A fourth image is rejected server-side even under concurrent requests. Object URLs are revoked when no longer needed. Newly written files are cleaned after failed persistence; removed image metadata is committed before best-effort file cleanup.

## Convert to customer

Save edits, choose Convert to customer and review the confirmation. Conversion atomically creates one active company and one active branch and marks the lead Converted. No customer is created when the business ID already belongs to an active or inactive company in that tenant. Repeated/concurrent conversion requests return the same customer rather than duplicates.

The selected primary address becomes the company's contact email; the branch email becomes the branch contact email. Company notes include both the original notes and description. All extra emails, description, images and source fields remain accessible on the read-only converted lead. Open customer links to the company and expands its branches. If that company/branch is later removed, conversion history remains and a clear unavailable-customer message replaces the link. Existing company contact schemas are unchanged.

## API and persistence

All routes are admin-only under `/api/v1/admin/leads`. GET list accepts search/status/skip/take (maximum 100). POST creates fields; GET by ID returns lead and private image metadata; PUT by ID receives `{version,fields}`. Activate/deactivate/convert/DELETE receive `{version}`. Image POST receives multipart `version` and `file`; image DELETE receives `{version}`. Image GET returns validated image bytes with no-store/nosniff headers.

Lead is a conjoined tenant Marten document. UUID version checks and per-tenant/lead PostgreSQL advisory reservations serialize edits, lifecycle actions, images and conversion across replicas. Conversion stores Company, Branch and Lead in one transaction, with the existing tenant business-ID uniqueness index. Authenticated tenant context controls storage and lookup; clients cannot assign tenant, customer IDs, image paths or conversion status.

Email history, sending, reply tracking, Resend changes and mailbox integration are explicitly excluded.

## Verification

Tests cover validation and multiple contacts; inactive editing/reactivation/deletion; image limits and safe cleanup; API denial for employees/managers/anonymous requests; tenant-private files; real PostgreSQL tenant isolation, email search, duplicate inactive-company rollback, concurrent conversion, stale writes and simultaneous image uploads. Browser coverage includes create/edit, primary contact, private upload/remove/failure retry, conversion confirmation/conflicts, retained read-only snapshots, unavailable customer/image retry, list/detail retry, filters/pagination and company deep links on desktop and mobile.
