# Persistent development state

Both dev Compose stacks now put Keycloak in a separate `keycloak_dev` database,
owned by a login of the same name, inside their existing dev PostgreSQL volume.
The preview API additionally keeps `/app/uploads` and `/app/keys` in project-scoped
named volumes. Production Compose is unchanged. Keep each stack's existing
Compose project name: changing it selects new, empty volumes.

Set `DEV_KC_DB_PASSWORD` in the stack's private environment file. It must be a
separate development credential. The PostgreSQL bootstrap identity is used only
by the dev database initialization script; Keycloak uses its dedicated identity.
This does not remediate the API database or identity privileges tracked separately.

`api/dev/init/01-keycloak.sh` uses PostgreSQL16 psql environment/literal quoting,
including passwords with quotes, and retains an existing role's password rather
than silently rotating it. An existing role with elevated privileges or database
with a different owner fails validation. Initialization files only run on an empty
PostgreSQL volume. Do not remove an existing volume to make initialization run.

## Existing dev installation: preserve before switching

Do this in a maintenance window, before starting the updated Compose. Keep the
old Compose, environment file, image IDs and exact project name for rollback.
No production stack or production records are involved.

1. Stop API writes, then stop the existing Keycloak container without removing it.
   Record its container ID and image ID. Exporting a running realm or only using
   the Admin Console partial export is insufficient to preserve users.
2. Copy `/opt/keycloak/data` from that stopped container to a private backup folder
   using `docker cp`. Preserve permissions and keep the original stopped container.
   Take a PostgreSQL dev backup too. These backups contain credentials: do not
   commit, upload as CI artifacts, or put them in the checkout.
3. Make a working copy of the saved Keycloak data. Launch the **old exact image**
   with that copy mounted at `/opt/keycloak/data`, no published ports, and run
   `/opt/keycloak/bin/kc.sh export --dir /backup/export --users different_files`.
   Bind a private writable export folder at `/backup/export`. Use the old embedded
   database settings, not the new PostgreSQL environment. No other Keycloak node
   may access that data copy. Confirm every expected realm and its user files are
   present and retain the unmodified raw backup independently.
4. For the preview stack, copy existing `/app/uploads` and
   `/app/keys/entertainment.key` from its stopped API container before recreating.
   Older preview configuration may have used `/app/entertainment.key`; inspect
   the previous effective setting and preserve that exact key. Do not generate a
   replacement: prior encrypted values require the original key.
5. Set the new dev DB password. Start only PostgreSQL using the same project name.
   For an existing database volume run the mounted initialization script explicitly:

   ```sh
   docker compose -p EXISTING_PROJECT -f api/deploy/docker-compose.dev.yml exec -T postgres bash /docker-entrypoint-initdb.d/01-keycloak.sh
   ```

   For local dev use `-f api/dev/docker-compose.yml`. Check the command succeeds;
   an existing password mismatch is reconciled explicitly, never by dropping data.
6. With Keycloak stopped, run its new Compose service as a one-off container with
   the private export directory mounted read-only, and execute
   `import --dir /backup/export --override false`. Use the same Keycloak version
   that produced the export. Inspect logs and verify expected realm/user counts.
   Startup `--import-realm` skips existing realms; it cannot merge a partial export.
7. For preview, create the API container without starting it, restore saved uploads
   and the original key into its new volumes, and check ownership allows the API
   image user to read/write them. Start Keycloak, then API. Verify login, dummy
   uploads and existing encrypted game data before reopening dev access.
8. Recreate the dev services without deleting volumes and repeat these checks.
   Retain backups until this verification passes.

Rollback: stop the replacement API/Keycloak, preserve the new DB/volumes, and
restore the old exact Compose/environment/image with the saved raw embedded data
and original uploads/key. Restore the dev PostgreSQL backup only if separately
reviewed and needed. Do not overwrite either copy or run `down -v` on shared dev.
Realm export is a migration format, not a complete database backup: event/session
state has limitations. The raw backup remains the rollback source.

## API running locally

The local Compose has no API service. Configure absolute paths outside temporary
checkouts/worktrees and keep them stable between `dotnet run` invocations, e.g.:

```text
Uploads__Path=C:/Users/YOUR_USER/.readysetsiivous-dev/uploads
Entertainment__KeyFilePath=C:/Users/YOUR_USER/.readysetsiivous-dev/keys/entertainment.key
```

Create private directories first. Back up the current key/uploads before moving
them and reuse their bytes. Do not store real keys in source control.

## Verification

`api/dev/tests/dev-persistence-recreate.py` exercises fresh disposable projects
for both checked-in Compose configurations, creates a dummy realm/user, writes
synthetic upload/key bytes, removes/recreates containers and checks identity IDs,
database role restrictions and byte retention. It never starts the API or calls AI.
It also seeds an unsafe existing superuser role and a database with the wrong
owner on separate fresh projects for each configuration. Both must produce a
nonzero PostgreSQL initialization exit and prevent Keycloak from starting.
The preview API volumes are tested with a disposable helper container because
the API image/build belongs to other verification lanes. A real API encrypted-data
smoke test remains required before migrating an existing installation.

Run `python api/dev/tests/dev-persistence-recreate.py` on a Docker Linux host.
The fixture only cleans its unique `devpersist-...` projects/volumes. It is also
defined in the dedicated CI workflow. Local execution is unavailable when the
Docker Linux daemon is stopped; static checks alone do not prove persistence.

Official references: [database configuration](https://www.keycloak.org/server/db),
[24.0.5 database guide](https://github.com/keycloak/keycloak/blob/24.0.5/docs/guides/server/db.adoc),
[realm export/import](https://www.keycloak.org/server/importExport).
The `KC_DB`, `KC_DB_URL`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` settings are used
instead of newer named-datasource options; retain existing 24/26 startup options.
