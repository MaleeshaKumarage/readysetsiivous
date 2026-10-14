# Marten security upgrade — issue95

The application pins Marten8.37.0 in Domain, Application and Infrastructure.
GHSA-rfx3-98h7-v3xp/CVE-2026-75513 covers versions7 through9.12;9.13 is the
first patched version. The current supported release is9.50.1 (verified from
the upstream release API on2026-10-14). The upstream migration guide requires
.NET9 or10, so use .NET10 LTS rather than a short-lived .NET9 transition.
Microsoft release metadata currently reports SDK10.0.402 and runtime10.0.13.

Upgrade all backend target frameworks, Docker SDK/runtime and CI setup together.
Align JwtBearer and MVC test host packages with the .NET10 runtime. Pin Marten and
Marten.Newtonsoft to9.50.1. Preserve existing stored JSON using explicit Newtonsoft
serialization, restore v8 StoreOptions defaults, and preserve identity session
registration. Existing manual dirty-tracked tenant sessions and GUID IVersioned
concurrency must keep their behavior; do not introduce numeric revision migration.
The application is a document store and has no event projections/daemon to migrate.
Build every configured document mapping during startup to retain fail-fast behavior
under Marten9's lazy mappings. Preserve the configured AutoCreate policy.
The current schema initializer logs and swallows migration errors. For this
runtime upgrade, schema/mapping initialization must succeed before startup can
report healthy; propagate initialization failures instead of silently serving an
incompatible schema. Test that failure boundary and keep AutoCreate.None respected.

Audit all exposed repository LINQ/raw-SQL calls and tenant lifecycle inputs against
the advisory's dictionary-key, projected runtime-constant and tenant-maintenance
sinks. Record observed exposure accurately; do not claim a demonstrated production
exploit. Keep validated tenant IDs and parameterized value queries. Add actual
PostgreSQL regressions for quoted/dictionary/constant inputs that cannot return
another tenant's rows, plus all document type round trips, private-setter JSON,
paired photo annotations, token isolation and stale-write concurrency.

Use an isolated database on shared dev with dedicated temporary credentials and
tenant partitions. Do not use real customer data for destructive/injection tests.
Rehearse starting from schema created by the current v8 application, seed synthetic
representative documents, then apply v9 mappings and verify identical IDs/content
and GUID versions. Capture and review the schema delta; refuse table/data deletion
or unexpected version-type changes. Verify a repeated apply is idempotent and
assess old-image read/write compatibility against the migrated rehearsal database.
Also restore the rehearsal backup into a separate isolated database, verify
representative document counts/content, and start the previous image against that
restore. Rehearse recovery rather than assuming a successful dump is sufficient.
A destructive or rollback-incompatible delta must be resolved or presented as a
concrete deployment decision before touching production.

Before production rollout retain a private recoverable database backup and the
previous image/config reference. Never publish credentials or customer data.
The private production backup must be complete and usable: validate its archive
and restore it to a separate private database using the rehearsed procedure, compare
table/document counts, and verify the previous image can start without exposing
that database or permitting background side effects. New images support
`BackgroundWorkers:Enabled=false` for isolated rehearsals. Previous images lack
that switch: restore into a dedicated PostgreSQL container on a private internal
network, use a non-owning reader role with SELECT-only grants and a pg_hba rule
rejecting that role for every other database. Enforce a hard 10-second API
container deadline covering startup failure/health timeout, stop immediately after
health, and unconditionally remove the smoke API container. Run away from the
entertainment cleanup's UTC-midnight boundary. The role also defaults to read-only
transactions; connection settings alone are not the write barrier.
Verify restored counts before and after that smoke test. Do not run injection/destructive tests on real
customer copies; those remain synthetic shared-dev tests. Remove only the private
verification database after proof, retaining the recoverable backup for rollout.
Merge only after compile/full tests, real tenant-isolation/concurrency, upgrade rehearsal,
package vulnerability audit and the container smoke checks pass. Deploy API only
with the hardened exact-image verification. Verify runtime/package versions in the
actual deployed image plus health and authorized/unauthorized tenant API behavior.
Do not close95 until the patched version is verified deployed.

This release changes runtime/dependencies, not product features or permissions.
Company business-ID uniqueness remains separate issue98; compose reconciliation
remains90. Use task-local ignored SDK/package/QA outputs as needed and remove only
dedicated rehearsal resources, preserving shared dev/production databases/volumes.

Sources: https://github.com/advisories/GHSA-rfx3-98h7-v3xp,
https://martendb.io/migration-guide, https://martendb.io/support-policy,
https://github.com/JasperFx/marten/releases/tag/V9.50.1,
https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json.
