# Write Integrity Design: Parent Validation, Uploads, and Agreement Evidence

Date: 2026-10-17  
Status: Approved scope; awaiting specification review and user review  
Related issues: [#81](https://github.com/MaleeshaKumarage/readysetsiivous/issues/81), [#73](https://github.com/MaleeshaKumarage/readysetsiivous/issues/73), [#77](https://github.com/MaleeshaKumarage/readysetsiivous/issues/77), [#121](https://github.com/MaleeshaKumarage/readysetsiivous/issues/121)

## Problem

Several write paths validate that parent IDs are nonempty without consistently verifying that the referenced records exist in the current tenant and form a valid relationship. Agreement and QC file paths accept content that can be malformed or too large, and the public agreement-sign endpoint decodes base64 before request validation. Agreement files currently use fixed paths, so concurrent signing attempts or a database conflict can replace evidence that belongs to the winning request.

The repository already has tenant-scoped Marten repositories, semantic checks in some command handlers, bounded service-image uploads, and signature PNG header/size checks. This work should close the remaining gaps and preserve those existing contracts.

## Goals and scope

1. Validate parent existence, tenant ownership, and relationship consistency for the #81 write families before any durable file or document write: branch creation; shift create/update and template assignment; checklist-template create/update; agreement creation; and the service-image target record. Review create and update paths together. Preserve existing inactive-parent behavior: an existing inactive parent in the same tenant remains eligible where current commands allow it; do not add an activation policy in this work.
2. Validate agreement PDFs, signature PNGs, service images, and QC photo payloads for bounded size and parseable content. Malformed input returns a stable `400`, oversize input returns `413`, and missing or foreign-tenant parents return the existing safe `404` behavior. No raw decoder exception or stack trace reaches a client.
3. Store agreement originals, signatures, and completed PDFs at immutable, per-attempt paths. Save metadata only after the bytes are safely stored; on persistence conflict/failure remove only the losing attempt’s staged artifacts. A losing same-token signing request must never overwrite the accepted signature or signed PDF.
4. Cover these contracts with focused unit and API integration tests, plus isolated Postgres tests for tenant ownership and concurrent agreement signing/commit behavior.
5. Separately deliver #121’s legacy association repair: tenant admins can inspect affected unsigned agreements and explicitly map each to an existing company in their tenant. Provide a preview before applying a reviewed mapping and an audit record for each association change. Never guess a company, delete/recreate an agreement, or change any signed or partially signed agreement (including its files, tokens, IDs, status, or history).

Included portions of #73 are agreement file/evidence safety needed for #81. Signer-management UI and the remaining agreement lifecycle policies are out of scope. Included portions of #77 are server-side QC payload and malformed-input handling. QC photo reviewer UX, drag/drop, matching policy, and FE/BE item-count alignment are out of scope unless a small contract adjustment is strictly necessary to submit a bounded valid payload.

## Design

### Parent and tenant integrity

Use repositories scoped to the request tenant for every lookup. `CompanyRepository` and `BranchRepository` are scoped through the request-scoped `ITenantSession` even though their `GetAsync` signatures do not take a tenant ID; preserve and test that scope rather than adding a second, potentially inconsistent tenant selector. `AgreementRepository` receives the tenant ID and must continue using that tenant's session. Before save, validate both existence and relationship: a branch belongs to the submitted company; a shift’s branch belongs to its company and an assigned checklist template belongs to that company/branch scope; checklist-template branches belong to their selected company; an agreement’s company is loaded through the request tenant; and a service exists in that tenant before its image file is written. Perform these checks before storing uploads. On update, validate the full resulting relationship, not only fields that changed. Do not use unscoped/global lookups to decide ownership.

Use consistent validation errors: invalid shape or mismatched relationship is `400` where the referenced object is known and the request is contradictory; missing or foreign-tenant resources remain indistinguishable as `404`. No failure may create or update the target document or leave a referenced file.

### Bounded semantic upload validation

Apply request-level limits before buffering large bodies, then validate decoded byte length and actual file structure in application code. Treat filenames and MIME types as hints only. The initial contract is: agreement PDFs at most 20 MiB and at most 500 pages; service images at most 5 MiB and JPEG/PNG/WebP, with each dimension at most 4096 and at most 20 megapixels; signature PNGs at most 2 MiB and at most 1200x400, with nonzero dimensions and full decode; QC photos at most 10 MiB each, at most 10 photos and at most 20 MiB decoded total per submission, with PNG/JPEG/WebP/BMP and maximum 40 megapixels per image. Set request limits above the base64 expansion of the applicable aggregate, but below unsafe unbounded buffering; return `413` when a byte/request bound is exceeded. If an existing hosting limit is lower, align it and the client-side preflight to this contract rather than silently relying on a framework default.

New QC writes use the paired `Photos` field with a data URL and a nonempty per-photo note (up to 500 characters). New nonempty `PhotoUrls` writes are rejected; that field remains a read-compatibility projection for historical records. Existing historical photo values are not rewritten by this change. If a historical value cannot be decoded in a report, retain the existing safe “photo unavailable” placeholder behavior and do not fail the entire report.

Agreement PDFs must be nonempty and parseable as PDFs within the page bound; unsupported/encrypted or corrupt PDFs are rejected. Signature PNGs must be fully decodable, not merely match the magic bytes, and have nonzero dimensions within the limits above. QC and service images must decode as their declared supported format, have nonzero dimensions within the limits above, and be bounded before decode to limit decompression risk. Return field-specific validation details through the API’s current problem-details handler.

The implementation should reuse already-installed image/PDF parsing dependencies where practical and avoid introducing a new parser without demonstrating that the current dependencies cannot reliably validate the format.

### Agreement file commit and concurrency

Replace fixed mutable file names with unique immutable artifact names. A write produces an artifact that is not yet referenced by a committed agreement. The handler validates current lifecycle state, prepares the artifact, then saves the updated agreement with its artifact path using Marten optimistic concurrency. On success, the immutable artifact is the accepted evidence. On any failed save or concurrency loss, clean up only the current attempt’s unique artifact. Cleanup failure is logged with correlation-safe metadata and must not expose signer tokens or file contents.

Each attempt to sign stages its own unique signature PNG. If a same-token race loses, reload the agreement; if that signer is already signed, return `409` and delete only the losing attempt’s PNG. Never replay the losing request by assigning its bytes/path over the winner’s signer record. For a different signer, reload after a document-version conflict and retry only if that signer is still pending and the agreement is still eligible; retain the same attempt-specific PNG for that retry. If the reloaded document already includes that signer or is completed, return the safe conflict/result dictated by its state and delete only an artifact not referenced by the saved agreement.

When the last pending signer is applied, generate and validate the signed PDF before committing the final signer state and signed-PDF reference. Keep the signature and completion update in one optimistic document commit. If generation or storage fails, clean up this attempt’s unreferenced signature/PDF and leave the previously committed document unchanged so the signer can retry. A conflict reload may discover that a different signer completed concurrently; never overwrite its immutable PDF. Any returned success must reflect the committed aggregate state.

Agreement creation follows the same stage/store-then-reference pattern: validate the company and PDF before any durable write, store to a unique artifact path, persist the agreement, and remove the artifact if persistence fails.

### Legacy agreement association repair (#121)

“Affected” means an agreement in the current tenant whose `CompanyId` cannot be loaded from that tenant’s `CompanyRepository`. “Unsigned” means status `Draft` or `Cancelled` and no signer has signed status, a signature image path, a typed name, or a signed timestamp. Only those records may appear in the preview; any contradictory evidence excludes the record regardless of status. An existing inactive tenant company may be selected to preserve historical relationships; foreign or missing companies cannot.

Add a tenant-admin-only preview and apply workflow. Preview lists only eligible records and the companies available in the current tenant; it does not mutate data. Include the agreement document `Version` as an opaque expected-version value. Applying a mapping requires an explicit agreement-to-company selection, verifies the company still exists in the same tenant and uses optimistic concurrency against the expected version. Save the association change and an append-only audit document (agreement ID, previous missing company ID, selected company ID, actor, timestamp) in one tenant-scoped Marten `SaveChanges` transaction. Re-read the agreement before applying and reject with `409` if it has changed since preview, become signed/partially signed/completed, acquired any signature evidence, or no longer has a missing parent. Signed and partially signed agreements are never offered for repair and remain byte-for-byte and logically unchanged. Do not expose tokens or document contents in the preview.

## Error behavior

- Malformed JSON/base64, invalid PDF/image content, unsupported format, and contradictory parent relationships produce the API’s normal validation response (`400`).
- Payload/request limits produce `413` with a safe, actionable message.
- Missing and foreign-tenant IDs return the same not-found shape to prevent tenant enumeration.
- Optimistic-concurrency conflicts return the existing safe conflict response (`409`) and leave previously accepted file references and bytes intact.
- Storage/provider errors do not include raw filesystem paths, base64, signature tokens, or stack traces in client responses.

## Verification

- Unit tests prove validation happens before repository or file-store writes and that failures do not mutate documents.
- API tests cover malformed/null/empty/missing multipart fields, request-body rejection before full buffering, byte-limit boundary and +1 cases, fake MIME/extensions, truncated/corrupt/hostile PDFs and images, image pixel bounds, and stable 400/413/404 responses.
- Tenant-isolated Postgres tests cover nonexistent and cross-tenant parents and mismatched company/branch/shift relationships on both create and update.
- Isolated Postgres plus a real temporary file store tests simultaneous same-token signatures, different signers completing concurrently, PDF generation/storage failures, database commit conflicts, retry, and durable file byte equality for the winner.
- Legacy repair tests verify preview is read-only; only missing-company Draft/Cancelled agreements with no signature evidence are eligible; explicit mapping is tenant-scoped and atomically audited; stale expected versions fail with 409; and signed/partially signed documents and all files remain unchanged.
- Run the API test suite and relevant Playwright/API integration suites. Use an isolated development/test database; do not mutate production records during implementation or verification.

## Alternatives considered

1. Validate payload headers and keep fixed paths. This is smaller but cannot establish file parseability and still permits a losing concurrent write to replace accepted bytes.
2. Store immutable attempt-specific artifacts and commit only references after validation, with cleanup for unreferenced artifacts. Recommended because it protects signed evidence across retries and optimistic-concurrency failures.
3. Repair #121 with an automated inferred mapping. Rejected because the correct company cannot be inferred safely; tenant-admin review and explicit mapping are required.

## Risks and boundaries

The database and filesystem cannot participate in one atomic transaction. Unique immutable artifacts, optimistic document commits, and best-effort cleanup prevent overwrites and dangling references; failed cleanup may leave an unreferenced orphan that can be identified for later maintenance. Do not add an automatic production cleanup job as part of this scope.

The live production legacy records must not be read or changed during this implementation unless an explicitly authorized, read-only verification procedure is available. The repair UI/API is designed for tenant-admin review; actual repair requires a separate explicit mapping decision for each record.
