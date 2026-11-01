# Partner production deployment correction

PR #230 passed build/test but its production release stopped before replacement:
the policy requires closed ingress and still names `cb33068` rather than the
healthy deployed API `2c9f6f2198b680e8b2b0b790e693409839f30584` as its rollback baseline.
Pages correctly retained the old site while waiting for the API release.

This correction preserves the maintenance guard and immutable manifest checks,
allowlists only the observed current API baseline, and pins the matching ARM64
partner identity image `sha256:ec2484e749c4f33b989e3cc3de2ced15366d10e8090f68a08020cc67ceb370eb`.
The image is staged on production without service replacement. Its provider JAR
SHA256 is `6048faa4ca396feb0bef0894ef55be4b73c1e05659540a9a23f2034222849c1e`,
matching [native verification](../security/2026-11-01-partner-native-verification.json).
Keycloak remains 26.8.1; this is an extension/group update, not a database-version upgrade.

Read-only production preflight proved the existing public issuer, master login,
dedicated runtime credentials and Admin REST denials. Only the flat `partner`
role and fixed `app-partner` group/mapping are missing. Do not rerun legacy user
enrollment, reset passwords or send invitations during release preparation.
Partner schema additions preserve existing document registrations and indexes;
new partner documents and optional JSON fields do not require deleting old data.

During the explicitly requested production fix, take private database/config
backups and verify recovery, close cloudflared ingress and pause the watchdog,
stop API writes, reconcile only the missing role/group, then start the staged
extension. Deploy the hash-bound API release through the normal controller and
prove identity/API health before restoring ingress and watchdog. Keep the old
API and identity images/configuration available; on failure restore a coherent
baseline. Publish Pages only after the matching API is healthy.

Local checks: release-manifest suite 10 passed; deployment suite 21 passed and
one Linux-only shell integration skipped on Windows. Do not represent local
checks as proof that production deployment has already completed.
