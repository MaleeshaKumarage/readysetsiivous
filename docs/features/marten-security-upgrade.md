# Marten security upgrade validation

Issue95 upgrades Marten8.37.0 to9.50.1 and the backend to .NET10 LTS
(SDK10.0.402, runtime10.0.13). Newtonsoft JSON, identity/dirty-tracked sessions,
GUID document versions and tenant policies are preserved. Marten's GUID version
interface moved to `JasperFx.Metadata.IVersioned`. Concurrent duplicate inserts
now also recognize `JasperFx.DocumentAlreadyExistsException` so competing QC
workers still create exactly one task.

Startup registers all18 document types, generates their schema definitions,
applies the configured AutoCreate policy and asserts the resulting schema.
Unreachable or incompatible databases prevent healthy startup. AutoCreate.None
performs verification without authorizing schema changes.

## Evidence, 2026-10-14

- All backend tests pass with synthetic PostgreSQL fixtures enabled:450 passed,
  two live Keycloak fixtures skipped. Those require isolated identity credentials.
- PostgreSQL regressions cover production document round trips, paired photo notes,
  GUID versions, cross-tenant reads, forged tenant IDs, dictionary keys/values,
  anonymous-object projected constants, concurrent task inserts and stale writes.
  Each fixture selects its own schema before registering mappings.
- The complete direct/transitive NuGet vulnerability audit reports no known
  vulnerable packages. The unmodified v8 baseline reports the advisory.
- The unchanged v8 application seeded18 document types. v9 preserved every
  document ID, JSON field and GUID version, and reapplication succeeded.
- Schema-only dump comparison showed one function-body qualification:
  `unaccent(word)` became `public.unaccent(word)`. No table/data deletion,
  document-column changes or numeric-version conversion occurred.
- The previous v8 application read and updated every migrated synthetic document;
  v9 then read all resulting content and GUID versions successfully.
- A synthetic v8 backup restored into another database and passed v8 content
  verification. The exact previous production container started against a separate
  private restore under enforced read-only permissions and a10-second deadline.
- The actual production backup also restored successfully; all table counts and
  content fingerprints matched the source and remained unchanged after the
  previous-image startup check. Its archive remains private on the production host
  under `cleaning-suite/backups/security95-20261014T095116Z/database.dump`.
- Native ARM64 patched-image startup passed strict schema verification with
  AutoCreate.None; `/api/v1/me/shifts` denied unauthenticated access with401.
  Both .NET and ASP.NET runtimes report10.0.13.
- A photo-and-note PDF generated successfully inside the native ARM64 runtime.

## Restore and rollback procedure

`scripts/verify-private-restore.sh ARCHIVE EXACT_PREVIOUS_IMAGE SOURCE_FINGERPRINT`
restores into a dedicated PostgreSQL16 container on an internal Docker network.
It publishes no ports, gives the API a non-owning SELECT-only role, rejects that
role for other databases through pg_hba, and stops/removes the API within10seconds
on success or failure. It compares restored table counts/content with the source
snapshot and verifies they remain unchanged. Containers, anonymous volumes and
network created by this script are removed; the input backup is retained.

Run outside the UTC-midnight cleanup boundary. Old images ignore the new
`BackgroundWorkers__Enabled=false` flag, so enforced permissions/network isolation
and the deadline are required. New images honor that flag, defaulting to enabled
in ordinary environments. Capture source fingerprints and dump from a consistent
snapshot or a quiet database; a concurrent legitimate write can invalidate their
comparison and requires a fresh rehearsal.

Retain the previous image/config and the verified private archive until rollout
is accepted. Revert the API image first for runtime regressions; restore a backup
only after assessing writes since its snapshot. Injection tests use synthetic
dev data, never a customer-data copy. Deployment still requires passing remote CI
and verification of the exact patched image and package versions on production.

Source: [GitHub advisory GHSA-rfx3-98h7-v3xp](https://github.com/advisories/GHSA-rfx3-98h7-v3xp).
Company business-ID uniqueness (#98) and compose reconciliation (#90) remain
separate changes.
