# Local infrastructure context

Before server, infrastructure, deployment or recovery work, read
`.local/operations/SERVER-RUNBOOK.md` when present. It is intentionally Git-ignored
and contains machine-local access references and the latest operational checkpoint.
Verify live state before acting; recorded state is not authorization for a new
deployment, downtime, deletion or permission change. Never commit private keys,
credentials, database backups or the contents of the ignored operations directory.
