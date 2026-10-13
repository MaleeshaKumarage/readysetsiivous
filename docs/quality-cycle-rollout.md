# Quality Cycle deployment and legacy tenant migration

The tenant-bound repository intentionally has no default-partition fallback.
Do not deploy over legacy QC data until ownership has been reconciled. This is a
release preflight, not an automatically executed production migration.

1. Back up the database and record the deployed application image. Stop QC writes
   and the old dispatcher during migration; prepare rollback before changing rows.
2. Inventory `mt_doc_qualitycycleform` and `mt_doc_qualitycycletemplate` using
   `SELECT tenant_id, count(*) FROM <table> GROUP BY tenant_id;`. Determine the
   actual default tenant identifier from those rows, not an assumed constant.
3. Map each legacy form to the unique tenant containing BOTH its ShiftId and
   EmployeeId. Verify the shift's company/branch, template reference and token.
   Reject orphaned or ambiguous mappings for manual review. Do not infer ownership
   from names or assign all global records to a convenient tenant.
4. Templates with company/branch scope must resolve those references in the same
   tenant. Truly global templates require an explicit owner decision. A template
   used by several tenants must be copied to each intended partition with verified
   references; never enable global reads in the repository to avoid this work.
5. Move/copy records in a reviewed transaction, preserving document IDs, JSON,
   token, version and created/submitted timestamps. Validate target collisions,
   duplicate shift/employee/occurrence records and token uniqueness before commit.
   Retain submitted evidence when reconciling duplicates. Update JSON snapshots
   only from verified source references; record any unknown legacy end time.
6. Verify per-tenant counts and token lookups in each owning tenant, plus negative
   lookups from another tenant. Confirm no unresolved legacy QC rows remain. Test
   rollback on a restored development copy before the production transaction.
7. Deploy API/site together. Verify schema creation includes report-share documents,
   current admin role policies and correct `App:FrontendBaseUrl` for emailed links.
   Inspect worker logs and confirm generation before enabling notification delivery.
8. Check an existing assigned shift, attach a template, record one occurrence and
   compare activity counts in shift/branch PDFs. Generate a share link, open it
   anonymously, then revoke it and verify 404.

Rollback application and database together if migration has moved records. The
old repository cannot read tenant-partitioned records. Shared links also depend
on the new API. Do not claim reconstructed assignment/schedule history for dates
before history tracking was introduced.

Development testing used a separate temporary PostgreSQL container, reached via
SSH forwarding, without modifying the shared dev business database. The shared
dev QC tables contained zero templates and zero forms at the preflight check.
No production database mutation or deployment was performed.

To run persistence regressions, set `QC_TEST_CONNECTION_STRING` to an isolated
PostgreSQL database and run the .NET tests. Without it, the database test reports
an explicit skip. Its fixtures create unique test tenant partitions. Never point
this test connection at production.
