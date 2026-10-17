# Write Integrity Design: Parent Validation, Uploads, and Agreement Evidence

Date: 2026-10-17  
Status: Approved scope; awaiting specification review and user review  
Related issues: [#81](https://github.com/MaleeshaKumarage/readysetsiivous/issues/81), [#73](https://github.com/MaleeshaKumarage/readysetsiivous/issues/73), [#77](https://github.com/MaleeshaKumarage/readysetsiivous/issues/77), [#121](https://github.com/MaleeshaKumarage/readysetsiivous/issues/121)

## Problem

Several write paths validate that parent IDs are nonempty without consistently verifying that the referenced records exist in the current tenant and form a valid relationship. Agreement and QC file paths accept content that can be malformed or too large, and the public agreement-sign endpoint decodes base64 before request validation. Agreement files currently use fixed paths, so concurrent signing attempts or a database conflict can replace evidence that belongs to the winning request.

The repository already has tenant-scoped Marten repositories, semantic checks in some command handlers, bounded service-image uploads, and signature PNG header/size checks. This work should close the remaining gaps and preserve those existing contracts.

## Goals and scope

1. Validate parent existence, tenant ownership, and relationship consistency for write commands covered by #81 before any durable file or document write. Review create and update paths together; preserve documented inactive-parent behavior unless a specific existing policy already rejects it.
2. Validate agreement PDFs, signature PNGs, service images, and QC photo payloads for bounded size and parseable content. Malformed input returns a stable `400`, oversize input returns `413`, and missing or foreign-tenant parents return the existing safe `404` behavior. No raw decoder exception or stack trace reaches a client.
3. Store agreement originals, signatures, and completed PDFs at immutable, per-attempt paths. Save metadata only after the bytes are safely stored; on persistence conflict/failure remove only the losing attempt’s staged artifacts. A losing same-token signing request must never overwrite the accepted signature or signed PDF.
4. Cover these contracts with focused unit and API integration tests, plus isolated Postgres tests for tenant ownership and concurrent agreement signing/commit behavior.
5. Separately deliver #121’s legacy association repair: tenant admins can inspect affected unsigned agreements and explicitly map each to an existing company in their tenant. Provide a preview before applying a reviewed mapping and an audit record for each association change. Never guess a company, delete/recreate an agreement, or change any signed or partially signed agreement (including its files, tokens, IDs, status, or history).

Included portions of #73 are agreement file/evidence safety needed for #81. Signer-management UI and the remaining agreement lifecycle policies are out of scope. Included portions of #77 are server-side QC payload and malformed-input handling. QC photo reviewer UX, drag/drop, matching policy, and FE/BE item-count alignment are out of scope unless a small contract adjustment is strictly necessary to submit a bounded valid payload.

## Design

### Parent and tenant integrity

Audit write commands that accept company, branch, shift, employee, agreement, template, or other parent identifiers. Use repositories scoped to the request tenant for every lookup. Before save, validate both existence and relationship: for example, a branch belongs to the submitted company; a shift’s branch belongs to its company; and an agreement’s company belongs to the current tenant. Perform these checks before storing uploads. On update, validate the full resulting relationship, not only fields that changed. Do not use unscoped/global lookups to decide ownership.

Use consistent validation errors: invalid shape or mismatched relationship is `400` where the referenced object is known and the request is contradictory; missing or foreign-tenant resources remain indistinguishable as `404`. No failure may create or update the target document or leave a referenced file.

### Bounded semantic upload validation

Apply request-level limits before buffering large bodies, then validate decoded byte length and actual file structure in application code. Treat filenames and MIME types as hints only. Keep existing service-image limits unless tests show they do not constrain the decoded body. Use explicit per-photo and per-submission limits for QC data URLs and reject remote, script, malformed, fake-MIME, truncated, zero-dimension, or unsupported image payloads. Agreement PDFs must be nonempty and parseable as PDFs; signature PNGs must be fully decodable within existing dimension limits. Return field-specific validation details through the API’s current problem-details handler.

The implementation should reuse already-installed image/PDF parsing dependencies where practical and avoid introducing a new parser without demonstrating that the current dependencies cannot reliably validate the format.

### Agreement file commit and concurrency

Replace fixed mutable file names with unique immutable artifact names. A write produces an artifact that is not yet referenced by a committed agreement. The handler validates current lifecycle state, prepares the artifact, then saves the updated agreement with its artifact path using Marten optimistic concurrency. On success, the immutable artifact is the accepted evidence. On any failed save or concurrency loss, clean up only the current attempt’s unique artifact. Cleanup failure is logged with correlation-safe metadata and must not expose signer tokens or file contents.

For final signing, generate and validate the signed PDF before committing the final signer state and signed-PDF reference. If generation or storage fails, do not mark the agreement complete. A conflicting same-token attempt cannot replace the signature bytes or final PDF chosen by the successful transaction. The retry response remains a safe conflict when another request has already completed that signer’s action.

Agreement creation follows the same stage/store-then-reference pattern: validate the company and PDF before any durable write, store to a unique artifact path, persist the agreement, and remove the artifact if persistence fails.

### Legacy agreement association repair (#121)

Add a tenant-admin-only preview and apply workflow. Preview lists only records needing repair and provides the companies available in the current tenant; it does not mutate data. Applying a mapping requires an explicit agreement-to-company selection, verifies the company still exists and is eligible under the existing lifecycle policy, and uses optimistic concurrency. Persist an audit entry with agreement ID, prior missing company ID, selected company ID, actor, and timestamp. Re-read the agreement before applying and reject if it has become signed, partially signed, completed, or otherwise changed since preview. Signed and partially signed agreements are never offered for repair and remain byte-for-byte and logically unchanged. Do not expose tokens or document contents in the preview.

## Error behavior

- Malformed JSON/base64, invalid PDF/image content, unsupported format, and contradictory parent relationships produce the API’s normal validation response (`400`).
- Payload/request limits produce `413` with a safe, actionable message.
- Missing and foreign-tenant IDs return the same not-found shape to prevent tenant enumeration.
- Optimistic-concurrency conflicts return the existing safe conflict response (`409`) and leave previously accepted file references and bytes intact.
- Storage/provider errors do not include raw filesystem paths, base64, signature tokens, or stack traces in client responses.

## Verification

- Unit tests prove validation happens before repository or file-store writes and that failures do not mutate documents.
- API tests cover malformed/null/empty/oversize bodies, fake MIME/extensions, truncated/corrupt PDF and image data, and stable 400/413/404 responses.
- Tenant-isolated Postgres tests cover nonexistent and cross-tenant parents and mismatched company/branch/shift relationships on both create and update.
- Isolated Postgres plus a real temporary file store tests simultaneous same-token signatures, different signers completing concurrently, PDF generation/storage failures, database commit conflicts, retry, and durable file byte equality for the winner.
- Legacy repair tests verify preview is read-only; explicit mapping is tenant-scoped and audited; stale/concurrent mappings fail; and signed/partially signed documents and all files remain unchanged.
- Run the API test suite and relevant Playwright/API integration suites. Use an isolated development/test database; do not mutate production records during implementation or verification.

## Alternatives considered

1. Validate payload headers and keep fixed paths. This is smaller but cannot establish file parseability and still permits a losing concurrent write to replace accepted bytes.
2. Store immutable attempt-specific artifacts and commit only references after validation, with cleanup for unreferenced artifacts. Recommended because it protects signed evidence across retries and optimistic-concurrency failures.
3. Repair #121 with an automated inferred mapping. Rejected because the correct company cannot be inferred safely; tenant-admin review and explicit mapping are required.

## Risks and boundaries

The database and filesystem cannot participate in one atomic transaction. Unique immutable artifacts, optimistic document commits, and best-effort cleanup prevent overwrites and dangling references; failed cleanup may leave an unreferenced orphan that can be identified for later maintenance. Do not add an automatic production cleanup job as part of this scope.

The live production legacy records must not be read or changed during this implementation unless an explicitly authorized, read-only verification procedure is available. The repair UI/API is designed for tenant-admin review; actual repair requires a separate explicit mapping decision for each record.
