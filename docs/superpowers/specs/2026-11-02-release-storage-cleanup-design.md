# Release-time storage cleanup (production and development)

Approved in chat: before image acquisition and after verified successful releases,
remove obsolete API images only; preserve database volumes, uploads, saved files,
backups and credentials. Apply the same safeguards to prod and dev.

A shared cleanup helper targets only known ReadySetSiivous API repositories. Never
use system/container/volume prune or forced image removal. Inspect every container,
including stopped ones, and protect its image. Also protect explicit current,
rollback and candidate image IDs and retain the current plus three recent release
images. Preserve very recent images (24 hours) to avoid disrupting in-flight work.
Delete by immutable image ID without force; skip refusal/races conservatively.
Retain a private per-environment successful-image history; malformed inventory or
history prevents deletion. Historical backup/config files are never removed.

Before pulling: cleanup, measure space on Docker's actual storage filesystem, and
fail clearly without replacing the live API if less than a configurable 6 GiB
headroom remains. After verified health: record the successful image and clean
again. A post-success cleanup error is a warning, not a reason to roll back a
healthy release. Failed deployments preserve their current/rollback images.

Production: invoke within the authenticated release controller and include the
helper in manifest asset verification. Development currently has no deployment
workflow: provide and install a wrapper that runs the same hooks around the
operator's existing deployment-and-health-verification command (argument array,
no shell evaluation). The wrapper does not modify Compose, credentials, or start a
new deployment by itself. Document it as the required dev release entry point.

Approved unused build-cache cleanup removes only cache older than 7 days, with a
4 GB retained-cache target, in both environments. It never touches volumes.

Validate image retention, unrelated repositories, stopped containers, malformed
state, race refusal, low-space early exit, failure/rollback, post-success warning,
manifest binding and dev command failure. Dry-run inventory on both hosts before
applying. No timer or background cleanup outside release boundaries is introduced.

Review refinement: the dev wrapper requires an explicit API container name,
candidate image reference and loopback health URL. Before running the command it
captures the existing container's project/service/image identity. Afterwards it
requires the same project/service, a running container using the resolved candidate
image ID, successful JSON health status, and matching release SHA when the image
contains one; re-inspect before recording success. No command exit code alone can
mark an image successful. Preserve the previous image regardless of command result.

Retention means current plus three distinct previous release images, plus all
container-referenced and explicitly protected IDs. On missing history, skip image
deletions for the first pass and initialize conservatively from recent API images;
subsequent pruning follows the verified-success history. Never delete history or
backups to gain space. Malformed state/inventory fails closed.

User also approved stale build-cache cleanup in BOTH environments: unused cache
older than 7 days, with a 4 GB retained-cache target using options supported by the
installed Docker client. Never use --all, volume prune or system prune. Cached
build layers are disposable; no live container or saved application data is removed.
