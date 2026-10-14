# Verify API deployment image and fail on command errors

Issue107: production root disk filled with old release images. Compose pull/up
failed but the SSH script continued and accepted the older healthy container,
reporting a successful deployment. Repair release verification while retaining
the existing reviewed-host configuration; full compose reconciliation remains90.

Use a repository shell script receiving the exact release SHA. The workflow
checks out its own approved script and copies it into the SSH invocation. The
script starts with strict shell error handling, validates a40-character hex SHA,
changes to the existing deployment directory, updates IMAGE_TAG, and validates
the resolved API compose image before replacing anything. Login remains in the
workflow with existing package read permissions, and errors must stop execution.

Pull and update only the API (`up -d --no-deps api`) for ordinary code releases,
preserving Postgres/Keycloak containers and all data/auth/upload/key volumes.
Before update, record the existing API image reference. After update, verify both
the configured image reference and container image ID against the expected local
release image, verify running state, then require a successful health response
whose JSON status is ok. A healthy old container, failed pull, failed up, image
mismatch or failed health must return nonzero. Do not print secrets, Docker env or
full compose configuration. Existing failed-release environment state can be
restored to the previous image tag on failure; do not promise automatic rollback
after a container replacement or change storage/key paths in this scope.

Production cache repair removes only unused old API release image tags after
checking every container image reference and retaining the running plus two
rollback releases. No container, volume, database or customer-file cleanup.
Clearing downloaded apt package archives is acceptable bootstrap cache recovery
when Docker GC cannot run on a full disk. Permanent cache retention automation is
documented as follow-up, not an unrestricted prune in the deploy script.

Test the actual shell script with fake docker/curl/sleep commands on an isolated
temporary directory: valid deploy, invalid SHA/config, failed pull/up, stale image
reference/ID, stopped container, invalid/error health and retry success. Assert
that pull failure never reaches up/health, that only api is updated without deps,
and that data/key/database services and volumes are never removed. Real production
verification checks the final running SHA/health after the tested image release.
Workflow-only change; no application/schema behavior changes or full issue90
reconciliation. Merge this fix and rerun release deployment for built bb82ad4.
