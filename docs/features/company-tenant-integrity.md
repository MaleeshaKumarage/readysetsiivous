# Company tenant integrity

Company business IDs are unique within each tenant. The same business ID may belong to a company in another tenant. IDs retain existing trimming and case-sensitive comparison. Deactivation does not release the business ID. Duplicate creates or updates, including concurrent requests, return 409 with a safe explanation; the company form keeps its input for correction and retry.

Branch and agreement creation require a company that exists in the current request tenant. Missing and foreign company GUIDs both return 404. Empty GUIDs remain validation errors (400). The check precedes branch persistence and agreement PDF storage. Existing inactive-company behavior is preserved; cascading deactivation and selection eligibility remain a separate policy question.

Checklist template create/update also checks optional company and branch scopes before mutation or persistence. Global, company-only, branch-only and matching combined scopes remain valid; branch-only does not automatically fill in CompanyId. A branch's actual company must exist in the current tenant, and supplied combined IDs must match. Missing/foreign/orphan/mismatched references return404; empty supplied GUIDs return400. Inactive parents retain existing behavior.

Public services/content, booking availability/create/lookup/cancel, agreement signing/files and QC/shared PDF routes use `/api/v1/public/{slug}/...` as their tenant. Anonymous access and a matching authenticated tenant work normally. A different authenticated realm receives 403 before dispatch; no request is redirected to the token's tenant. Unknown or inactive URL tenants also receive 403. Frontends should send bearer tokens only to their authenticated workspaces, as the existing frontend does.

Manager endpoints also require a registered Active tenant, matching admin/me workspaces. Unknown/suspended manager realms return403 before employee lookup or operational reads/writes, even with an existing signed token. Provisioner identities cannot enter the manager workspace. Active managers retain their tenant-wide operations. This closes the guard omission tracked in #120.

## Index migration

The existing `mt_doc_company_uidx_business_id` index keeps its name and changes to include `tenant_id`. Rehearse Marten schema reconciliation on a private restored database, verify preserved document contents/IDs/versions and a second idempotent reconciliation, then release through the normal API pipeline. Preserve a recoverable backup before rollout. A code rollback to the global definition is unsafe once separate tenants legitimately share an ID; retain the scoped index and use a forward fix.

Employee login identity uniqueness remains per tenant. Registry slugs, QC capability tokens, public report tokens and shared entertainment join keys retain global uniqueness. Booking/invoice number and service slug mappings have no unique database index; their application policies are outside this change.

## Test coverage and remaining work

PostgreSQL tests cover legacy global rejection, index replacement, unchanged records, cross-tenant reuse, duplicate create/update, concurrent creates and tenant-scoped parent lookups. Signed JWT tests cover all public route families and ensure denied calls dispatch no handlers. Playwright covers visible company conflict/branch failure, retained values and corrected retry on desktop and mobile.

Addresses #98, #67 and the branch/agreement/template parent-ownership portion of #81. #81 remains open for semantic binary validation and signature/PDF race integrity. Company lifecycle UI and broader shift policies remain tracked separately.

Validated on 2026-10-14: 496 backend tests passed, two live-Keycloak fixture tests skipped; 40 relevant Playwright checks passed on desktop/mobile. A private production dump restored into disposable containers migrated successfully and reconciled twice with every table count/content hash unchanged. The original company table contained two records (64 KB). Recoverable backup: `/home/ubuntu/cleaning-suite/backups/company-tenant-20261014/database.dump`. Rehearsal containers/network were removed; no customer records were written.
