# Local infrastructure context

Before server, infrastructure, deployment or recovery work, read
`.local/operations/SERVER-RUNBOOK.md` when present. It is intentionally Git-ignored
and contains machine-local access references and the latest operational checkpoint.
Verify live state before acting; recorded state is not authorization for a new
deployment, downtime, deletion or permission change. Never commit private keys,
credentials, database backups or the contents of the ignored operations directory.

For development API releases, use scripts/dev_release_cleanup.py around the trusted deploy-and-health command; see docs/features/release-storage-retention.md. Direct Compose release commands bypass the approved storage hooks. Actual host paths remain in .local/operations/SERVER-RUNBOOK.md.

# Field-service workflow implementation

For field-service workflow work, read the [approved design](docs/superpowers/specs/2026-11-02-field-service-workflows-design.md), [reusable implementation plan](docs/superpowers/plans/2026-11-02-field-service-workflows-plan.md), and [progress ledger/handoff](docs/implementation/field-service-progress.md) before starting. Resume the next dependency-ready unfinished chunk from the ledger; do not repeat completed work or treat planned capabilities as implemented.

Deliver small, sequential PRs. Each chunk requires focused Playwright E2E coverage, relevant backend verification, review and fixes, then merge. Save the result and next steps in the ledger/handoff before compacting context and starting the next chunk. Record implementation, merge and verified deployment separately; keep existing release checks and unrelated workspace files intact.
