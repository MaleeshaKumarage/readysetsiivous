# Database privilege rollout (production gate)

This change must not merge/deploy until an operator reviews the concrete
ownership inventory, backup/restore rehearsal, credentials and release ordering.
It does not authorize a live role change. Existing volumes do not rerun init.

## Identity boundaries

`cleaning` remains the existing isolated Postgres bootstrap operator. Only the
database container and reviewed setup operation receive its credential.
`cleaning_owner` is NOLOGIN; `cleaning_migrator` is NOINHERIT and may SET ROLE to
that owner. The one-shot API `--migrate-schema` mode forces that role on every
connection and uses the same `AddCleaningMarten` mappings as runtime. Runtime
uses `cleaning_runtime`, CRUD, sequence usage and invoker functions only; no
ownership/membership, schema creation, temporary tables, role/database creation,
or connection to Keycloak/maintenance databases. Keycloak has its separate owner.
Conjoined tenant partitions and public schema names remain unchanged.

Production API configuration rejects any schema mode except None. Its startup
asserts the schema matches, without applying changes. New releases fail closed
until the explicit migration succeeds; no admin fallback exists.

## Existing production inventory and review

1. Pin the exact release image and save the current image/config/role grants.
   Take encrypted full Postgres globals and custom-format database backups,
   plus file/key-volume snapshots. Prove restore on an isolated database first.
2. Inventory database/schema owners, all `public.mt_%` tables, sequences,
   indexes and routines, existing role memberships, ACLs/default ACLs, and any
   SECURITY DEFINER routine. Verify no non-application service shares this stack.
3. Review an explicit object-by-object ownership transfer from `cleaning` to
   `cleaning_owner`, including functions by full signature and sequences. Do not
   use REASSIGN OWNED across the shared cluster: that can move unrelated or
   Keycloak objects. No automatic ownership transfer is included. The migration
   preflight refuses legacy-owned Marten relations/functions.
4. Review database ownership separately. An existing database's bootstrap owner
   remains unchanged by setup. The application owner needs only public-schema
   usage/create for migrations. Inspect public schema for non-Marten objects
   before applying the deliberately database-wide application grants.

## Rehearsal and rollout

Use synthetic new secrets, never production values, in the rehearsal. Execute
`bash api/deploy/database/setup.sh` as the bootstrap operator with POSTGRES_USER,
POSTGRES_DB, PGHOST/PGPORT/PGPASSWORD, KC_DB_PASSWORD, DB_MIGRATOR_PASSWORD and
DB_RUNTIME_PASSWORD supplied privately by the operator. psql reads passwords
from environment and quotes them with format(%L), without shell-generated SQL.
The script can run again, but intentionally resets the four managed roles'
privileges/memberships and credentials. It is not safe for a shared cluster.

After the reviewed legacy ownership transfer, run the exact release migration
image with only `ConnectionStrings__MigrationConnection`. In deployment:
`docker compose --profile migration run --rm migrate`. Keep migration credentials
out of API environment. Start the runtime with only its runtime connection and
verify health, tenant-separated create/read/update/delete, counters/functions,
advisory locks, historical reports and attachment references. Confirm the
runtime cannot perform DDL, SET ROLE, create roles/databases or connect to
Keycloak. CI provisions synthetic roles and tests those boundaries on PostgreSQL.
Perform migration a second time to verify no schema drift.

## Rollback

Stop writers before ownership/grant changes. Retain the prior image and exact
schema inventory; migrations are additive CreateOrUpdate, not destructive
CreateOrUpdateOrDrop. A failed migration prevents startup: inspect and restore
from the rehearsed backup if schema/data recovery is needed. Rolling back an
image alone is insufficient for incompatible schema changes. Do not silently
restore the bootstrap connection to the new runtime. Any temporary restoration
of old grants/ownership requires explicit operator approval and an audit entry.
Preserve Keycloak data, tenant partitions, signatures, photos and encryption keys.

## Verification status

This branch is based on PR #199 (dev persistence, corrected head 23f0428),
including its reviewed guard and negative-fixture fixes. Both dev
Compose entrypoints mount the application-role setup separately from their
existing Keycloak-dev database initialization. They set DB_SETUP_KEYCLOAK=false
so application setup never replaces the dev identity database. Supply separate
DB_RUNTIME_PASSWORD and DB_MIGRATOR_PASSWORD in each dev environment. Existing
dev volumes need an explicit reviewed setup run too; never delete a volume to
make first-boot initialization rerun. The dev migration profile requires a
reviewed DEV_IMAGE_TAG. For a host-run API, set DefaultConnection to runtime and
MigrationConnection to the migration identity, and invoke
`dotnet run --project api/CleaningSuite.Api -- --migrate-schema` before serving.
MigrationConnection must be removed from the serving process environment.

Source configuration and a conditional PostgreSQL rehearsal are included.
CI also starts isolated fresh PostgreSQL containers through both actual
non-executable init wrappers, confirms the final TCP server becomes healthy,
and checks the initialized runtime identity/grants. This guards the official
entrypoint's sourced-script path: wrappers call Bash and return, never exec.
Local Windows has no running PostgreSQL daemon; skipped database tests are not
evidence of privilege enforcement. Production ownership migration and credentials
are intentionally pending operator review. CI execution and independent review
must be recorded before this change is considered ready.
