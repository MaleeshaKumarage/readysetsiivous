# Release storage retention

Production and development share `scripts/release_cleanup.py`. It performs
image/cache cleanup before release acquisition and after verified health, with
one per-environment release lock. No Docker system/container/volume prune is used.
Database volumes, uploads, keys, credentials, backups and saved files are untouched.
Only the helper's private retention history and lock metadata are written.

Image removal is restricted to ReadySetSiivous API repositories and previously
tracked API image IDs. All container images (including stopped containers), the
current/candidate/rollback images, four recent distinct release images and images
less than 24 hours old are retained. Images with unrelated repository references
are excluded. Removal uses immutable IDs without force; Docker refusals are kept
conservatively. A missing history initializes conservatively and skips image
removal on that first pass. Malformed history or inventory stops cleanup.
Older backup/config files remain; restoring an older uncached release may require
pulling its recorded immutable image digest again.

Unused Docker build cache older than seven days is eligible for removal, with a
4 GB cache target. The helper detects modern/legacy Docker flags and pins the
local default builder. On supported clients, pre-release cleanup also targets the
configured free-space floor; the seven-day age rule still applies. Shared/in-use
layers are retained by Docker. Cleanup may legitimately recover too little space.

Production hooks are part of the authenticated release asset manifest. Default
free-space floor is 6 GiB on Docker's storage filesystem, configurable with the
GitHub repository variable `RELEASE_MIN_FREE_GIB`. A shortfall stops before pulling
or replacing the live API and reports an actionable error. Post-success cleanup
failure is a warning and never rolls back a verified healthy API.

## Development release entry point

Dev has no automatic deployment workflow. Run the existing deploy-and-verification
command through the installed wrapper for every dev API release:

```sh
python3 scripts/dev_release_cleanup.py \
  --root "$DEV_DEPLOY_ROOT" --container "$DEV_API_CONTAINER" \
  --candidate-image "$DEV_API_IMAGE" --project cleaning-dev \
  --health-url http://127.0.0.1:8090/healthz \
  -- /path/to/existing-dev-release-command --verify
```

The command is an argument array, never shell-expanded. Use an existing trusted
script if multiple deployment steps are needed. The wrapper does not replace the
existing deployment logic or perform a release merely by being installed. Direct
Compose commands bypass these hooks. Current host paths are in the ignored server
runbook. Keep both helper scripts together when installing them.

The wrapper requires the existing dev project/service, preserves its old image,
verifies the new container uses the candidate image ID, ties the loopback health
port to that container, rejects redirects/non-local URLs, and verifies JSON status
and the image's release SHA when present. Only then does it record successful
retention and run post-cleanup. Failed commands or health checks never advance
successful history. Configure headroom explicitly with `--minimum-free-gib` only
when it is sufficient for the chosen image/build; do not reduce it just to hide a
storage shortage. A future Docker Desktop/Mac migration needs a VM-aware free-space
probe because Docker's storage path may not exist on the macOS host.

## Inspection and recovery

```sh
# Image plan only; no Docker mutations or retention-history writes.
python3 scripts/release_cleanup.py --root "$DEPLOY_ROOT"
# Approved cleanup and pre-release headroom check; does not start a release.
python3 scripts/release_cleanup.py --root "$DEPLOY_ROOT" --apply --check-space
```

History is `.release-image-retention.json` (private, atomic writes); lock is
`.release-image-cleanup.lock`. Do not edit history to force removal. Fix invalid
metadata or add capacity when safe cleanup cannot satisfy headroom. The scripts
never delete old release directories or backup archives automatically.

Regression coverage checks retention, stopped containers, unrelated repositories,
recent/dangling images, nanosecond dates on Python 3.10, malformed metadata, lock
contention/symlinks, cache flags/age, low-space preflight, manifest binding,
rollback preservation, dev command/health/candidate/port failures, and post-cleanup
warning behavior. Real Linux wrapper tests use synthetic Docker/curl binaries.

Validation on Linux/Python 3.10: 53 tests passed with no skips, including the real shell/command release harness. Live dry-runs and approved cleanup ran on both hosts; running containers and application data were preserved. Dev recovered cache space but remained below default headroom, and the pre-release check reported the shortage without starting a deployment.
