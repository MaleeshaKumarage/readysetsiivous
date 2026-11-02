# Manual invoices implementation

## Primary goal
Admins can issue separately agreed charges from the existing invoice page, using manually entered rows and the existing invoice layout.

## Phase 0: Confirm integration patterns
How this serves the primary goal: reuse functioning billing, tenant and PDF behavior.
Read ShiftInvoiceService.cs for preview hashes, tenant-local dates, yearly number allocation and reservation; InvoiceRepository.cs for shared-session persistence; InvoicePdfRenderer.cs for generic rows; lifecycle-invoice-billing.spec.ts for browser fixtures. Existing APIs: ICompanyRepository.GetAsync, ITenantProfileRepository.GetAsync, IShiftWorkReservation.AcquireAsync, IInvoiceRepository.NextInvoiceNumberForYearAsync/SaveAsync. Do not invent billing addresses on Company (none exists). Completed during design/discovery.

## Phase 1: Manual invoice service and endpoints
How this serves the primary goal: persist server-calculated, retry-safe invoices in existing history.
Add ManualInvoiceService with normalized row validation, decimal rounding, issuer/company snapshots, deterministic preview, issuance under existing reservation, request fingerprint and tenant-scoped submission lookup. Extend Invoice with nullable retry metadata and repository lookup; register service and admin endpoints. Follow existing validation/conflict error handling and invoice numbering. Verify focused service, authorization and persistence/concurrency tests. Do not link manual lines to shifts or accept client totals.

## Phase 2: Manual mode on invoice page
How this serves the primary goal: let admins enter charges and preview/issue them without cleaning work.
Add typed API methods, mode selector and accessible row editor. Reuse company/address/language settings and history. Render appropriate manual preview columns. Invalidate stale previews on edits, freeze form during issue, preserve uncertain submissions for safe retry and refresh history on success. Verify desktop/mobile browser flows and existing shift/partner tests. Never rotate a submission identifier after an uncertain write until reconciled.

## Phase 3: Verify and deliver
How this serves the primary goal: ensure reliable billing without regressing existing invoices.
Test rounding/bounds/stale preview, immutable snapshots, missing company/issuer, retry and conflicting reuse, tenant isolation, concurrent issuance, lifecycle and generic PDF rows. Run TypeScript, production build and focused Playwright regression. Inspect final diff and report results/limits. No production invoices are created for tests.
