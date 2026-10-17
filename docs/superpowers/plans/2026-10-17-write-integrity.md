# Write integrity implementation plan

Spec: [Write Integrity Design](../specs/2026-10-17-write-integrity-design.md)

## Tasks

1. **Inventory and shared validation primitives.** Confirm all write paths in scope and existing contracts. Add bounded upload validation helpers for PDFs and raster images, stable 400/413 mapping, and focused parser tests. Do not change inactive-parent policy.
2. **Parent relationship guards.** Verify company/branch/shift/template relationships in scoped repositories for create and update operations. Ensure the service-image target exists in the request tenant before disk writes. Add tenant-isolated Postgres tests and no-write assertions on failure.
3. **QC payload protection.** Enforce 30 MiB request size, per-image 10 MiB, 10 images, 20 MiB aggregate, supported formats/full decode, paired notes up to 500 characters, and strict `PhotoUrls` write rejection while retaining historical read compatibility. Align client preflight and test boundaries and report fallback for corrupt historical files.
4. **Immutable agreement artifacts.** Evolve `IAgreementFileStore` to create unique immutable artifacts and delete only unreferenced attempt artifacts. Validate uploads before staging; update create/sign handlers and repository calls so document references commit optimistically and failed commits clean up their own artifacts. Map invalid payloads and conflicts to safe 400/413/409 responses.
5. **Signing race and recovery tests.** Use an isolated Postgres database and real temporary file store to cover same-token races, different signer concurrency, last-signer PDF generation/storage failures, commit conflicts, and byte-for-byte preservation of the winning signature and final PDF.
6. **Legacy agreement repair (#121).** Add tenant-admin-only preview and apply API plus a focused agreement admin UI. Preview only unsigned Draft/Cancelled records with missing tenant-company parents. Use expected Marten `Version`, explicit company mapping, deterministic audit document ID, atomic association+audit commit, idempotent exact replay, and 409 for stale or conflicting mapping. Prove signed/partially signed evidence is unchanged.
7. **Verification and issue traceability.** Run focused tests, full API test suite, relevant Playwright tests and site build. Review the diff and update the linked GitHub issues with delivered portions and remaining out-of-scope acceptance items; close an issue only if its full acceptance criteria are met.

## Dependency and interface notes

- Tasks 1 and 2 establish the validation boundary before Tasks 3 and 4 consume it.
- `CompanyRepository` and `BranchRepository` use request-scoped `ITenantSession`; `AgreementRepository` uses the explicit tenant ID. Do not change or bypass these ownership boundaries.
- Agreement artifact paths must be unique per write attempt. Never truncate or replace a path already referenced by a committed agreement.
- For QC images, base64 expansion requires a 30 MiB JSON body ceiling to carry at most 20 MiB decoded image bytes.
- #121 association repair is separate from preventive guards and must never infer a company or alter signed/partially signed agreements.

## Validation

Run the focused API tests after each task, the isolated Postgres tests for tenant and concurrency behavior, then the full API test suite. Run targeted Playwright coverage and `npm run build` for the admin UI. Use only an isolated development/test database; do not apply repairs to production records.
