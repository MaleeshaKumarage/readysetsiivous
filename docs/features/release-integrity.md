# Release-bound API deployment

API builds publish a release manifest with the successful build's repository, SHA, run/attempt, immutable image digest and hashes of its Compose, deployment wrapper/helper and compatibility policy. The deployment controller is checked out at the workflow's own SHA. It verifies the same-repository successful `main` push build and the artifact's provenance, exact ZIP digest and single bounded JSON member before checking out release assets. Selected assets must match every hash before transfer or execution. Manual deployment resolves a successful matching build; missing, expired or unrelated artifacts fail closed. Commit tags remain useful labels but are not immutable deployment identities.

Assets are staged under `releases/<SHA>` on the host. SCP never overwrites the active Compose file. The authenticated manifest hash and every staged asset are verified again before execution. Compose receives an explicit project directory and the existing project's name; API replacement uses `--no-deps --pull never`. Database, Keycloak and named volumes are never recreated or rolled back by this script.

Before any Compose operation, pull or replacement, the deployment invokes the exact hash-bound `api/deploy/check-keycloak-runtime-env.py` from its staged release. The host `.env` must contain exactly one unquoted canonical absolute `API_IDENTITY_ENV_FILE=/protected/path/tenant-runtime.env`; inherited workflow variables cannot override this selection. Missing, malformed, symlinked, checkout-local or insecurely permissioned identity files fail closed. The checker requires complete dedicated tenant credential pairs and never prints their values. No optional bypass is supported.

Before replacement the script preserves the original raw Compose and env file, previous release, local image ID and repository digest in a host-only `.deploy-backups/<deployment>` directory (0700; files 0600). Every Docker command explicitly pins `--host unix:///var/run/docker.sock`, including preflight before registry-auth isolation, so a saved remote Docker context cannot redirect backup or replacement. Commands receive a sanitized environment so current workflow secrets cannot override restored env-file values. Registry credentials use a temporary private Docker config, removed after the attempt. Resolved configuration and credentials are not logged or uploaded.

The candidate is pulled by digest and checked for its build-time `APP_RELEASE_SHA`. Verification checks container identity/running state, exact health `releaseSha`, and stable container identity after health. Failure after replacement restores the old raw configuration and env, recreates API with a saved immutable-image override, and verifies the previous image ID/release health. A successful rollback still reports the deployment as failed. Rollback failure keeps private recovery inputs and reports that recovery was not verified. Health is currently liveness, not database readiness.

## Activation requires a separate concrete rollout review

`api/deploy/release-policy.json` intentionally defaults to `approved: false`. This blocks replacement; merging this implementation alone does not activate production deployment. Existing production continues running, while an automatic deployment of this default policy fails safely. Pages that require the new API retain the prior release if their API wait times out.

Before activation, review the exact candidate SHA/digest/assets and current deployed SHA/digest, confirm the current on-disk Compose/env matches the running API, and rehearse candidate plus application rollback on a disposable stack with synthetic data. Inspect all schema changes: runtime Marten `CreateOrUpdate` still exists and can make old code incompatible. The company business-ID index migration is an existing example where reverting old schema definitions is unsafe. Never undo schema/data changes automatically.

Only after that review, commit a policy with `approved: true`, `schema_changes: "backward-compatible-reviewed"`, and an explicit `rollback_compatible_from` list containing the actual prior release SHA. Rebuild that policy commit so its bytes are bound in the new manifest. Recheck the previous deployed SHA immediately before replacement. No wildcard compatibility or unsafe bypass is supported. First deployment requires an existing healthy versioned API and its locally available repository digest; do not bootstrap through this rollback path.

Preserve the backup's `image.json` override when performing an operator recovery: run Compose with its saved raw configuration/env, the stable project directory/name and that override. Raw old configuration may still contain a historical tag; the override is what pins the recovery image. Never prune the current or rollback images as part of deployment. Review private backup retention separately; backups contain secrets and must not become Actions artifacts.

## Verification

`python3 scripts/tests/test_deploy_api.py` exercises production deployment logic with synthetic Docker/curl command responses, including partial replacement, immutable rollback, wrong release health, failed rollback, secret redaction and migration gates. On Linux it additionally executes the real shell wrapper and external fake commands. `python3 scripts/tests/test_release_manifest.py` covers build/artifact provenance, ZIP bytes, asset hashes and workflow trust order. `deployment-tests.yml` runs both suites and shell syntax checks without production access.

API references: [build action outputs](https://github.com/docker/build-push-action#outputs), [workflow runs](https://docs.github.com/en/rest/actions/workflow-runs), [artifacts](https://docs.github.com/en/rest/actions/artifacts), [Compose project/file/env options](https://docs.docker.com/reference/cli/docker/compose/), [Compose up](https://docs.docker.com/reference/cli/docker/compose/up/).


## PDF companion lifecycle

The selected Compose binds API and `pdf-worker` to the same immutable `API_IMAGE`.
The controller snapshots the previous companion's presence, image ID and digest;
it verifies existing native readiness before changing either service. Candidate
worker startup and native readiness precede API replacement. Readiness executes a
credential-free `--pdf-worker-check` in the worker cgroup: it checks release-aware
HTTP health, signs a synthetic one-page PDF and reinspects the result. No published
worker port or additional probe image is needed. Container checks also enforce the
reviewed UID, limits, filesystem, environment and private-network boundary.

Failure restores the previous worker before restoring API. If no previous worker
existed, cleanup removes only the candidate container after checking Compose
ownership and immutable identity; unrelated containers and networks are untouched.
Worker-only startup failures do not recreate the still-running API. Recovery is
reported verified only when both previous components (or previous worker absence)
and the old API release have been checked. Private backups record companion state.

Compatibility approval includes the PDF HTTP protocol and persisted output format:
the previous API may briefly use the candidate worker before API replacement, and
vice versa during recovery. Keep `approved: false` until both directions have been
reviewed and rehearsed with the actual release pair. The protected tenant identity
file is consumed only by API; the PDF companion never receives it. PR202 runtime
identity support and PR203 database-role migration remain separate activation
prerequisites. Merging deployment code does not authorize those production changes.
