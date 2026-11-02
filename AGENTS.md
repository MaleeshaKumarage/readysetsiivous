# Local infrastructure context

Before server, infrastructure, deployment or recovery work, read
`.local/operations/SERVER-RUNBOOK.md` when present. It is intentionally Git-ignored
and contains machine-local access references and the latest operational checkpoint.
Verify live state before acting; recorded state is not authorization for a new
deployment, downtime, deletion or permission change. Never commit private keys,
credentials, database backups or the contents of the ignored operations directory.

For development API releases, use scripts/dev_release_cleanup.py around the trusted deploy-and-health command; see docs/features/release-storage-retention.md. Direct Compose release commands bypass the approved storage hooks. Actual host paths remain in .local/operations/SERVER-RUNBOOK.md.
