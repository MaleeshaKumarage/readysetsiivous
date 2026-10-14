# Company tenant integrity

## Scope and decisions

Address #98 and #67 together with the parent-company ownership portion of #81. Tenant managers remain tenant-wide; roles and existing feature permissions do not change.

Company business IDs must be unique within a tenant, allowing the same existing trimmed business ID in different tenants. PostgreSQL must enforce this for concurrent creates and updates. Keep the existing index identity while changing its tenancy scope, rehearse the generated migration against a restored database, and verify it removes global uniqueness without changing document IDs, versions or JSON. Translate only this index's unique violations into a safe 409 company conflict, and display that problem in the company UI. Other unique keys retain their intended scope: employee identities per tenant; registry slugs, public capability tokens and shared entertainment join keys global.

Public `/api/v1/public/{slug}/...` routes always select the URL tenant. Anonymous calls and matching authenticated calls work as before. An authenticated token from a different realm returns 403 before any public handler reads or writes; unknown or inactive URL tenants also return 403. This conservative policy prevents silent redirection without clearing or changing the authenticated identity. URL selection takes precedence over the authenticated branch and uses an anchored public route prefix. Services, bookings, signing and QC/report routes share this behavior. Private routes continue using the authenticated realm.

Branches and agreements must resolve their company through the current tenant repository before any durable write. Missing and foreign IDs both return 404 without exposing another tenant's existence. Company activation/cascading policy remains the separately documented open discussion: this fix adds no new active-parent requirement. Agreement upload parsing and signature artifact races remain open in #81.

Checklist template create/update must apply the same ownership rule to optional company/branch scopes. Null/null remains global; company-only and branch-only remain supported. A branch-only scope resolves its branch's company in the same tenant; when both IDs are supplied the branch must belong to that company. Missing/foreign/mismatched parents return404 before template mutation or persistence. Empty non-null GUIDs return400. No new active-parent policy or scope UI is introduced.

## Alternatives

Application-only duplicate checks lose races and leave the global index defect; retain database enforcement. A second differently named scoped index can leave the old global constraint behind; prefer replacement under the same index identity, verified from actual SQL. Ignoring foreign authenticated tokens on public routes could work today, but leaves foreign identity attached to URL-scoped handlers; explicit mismatch rejection is clearer and safer for future authorization.

## Validation and release

Use genuine PostgreSQL sessions for same-ID companies across tenants, duplicate create/update conflicts within a tenant, unchanged foreign records, and migration idempotence. Verify missing/foreign company IDs prevent branch/agreement storage, including agreement file writes. Test real signed JWTs against public endpoint families for anonymous B, A+A, A+B and inactive B, ensuring denied calls never dispatch handlers. Document feature gaps and Playwright edge cases; exercise UI conflict rendering and retained input.

Run relevant tests and CI, make a production backup, rehearse on a private restored database, merge the reviewed PR and verify the exact deployed commit. No production customer fixture writes. Do not close #81 while its binary validation or artifact race work remains.
