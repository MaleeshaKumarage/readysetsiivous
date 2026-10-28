# Production identity promotion

Status: production rollout explicitly authorized in chat after the earlier pause.
This promotes the previously reviewed and verified extension, not the superseded
FGAP or coarse administration-role designs.

## Release contract

Use the exact ARM64 provider image8523fc048089e840643f3797aed372ecc974b88532dffb98a3331924d7319c02
containing the verified26.8.1 provider JAR. Its bytes are staged from the verified
development build into the production image store without credentials or running
service replacement. Production Compose pins that immutable local image ID with
pull_policy never; the same reviewed image must be explicitly staged for recovery
or a new host. Production start uses HTTP internally behind existing edge TLS,
fixed HTTPS hostname, PostgreSQL, health support and documented Java heap options.
The API uses private Keycloak transport while validating the public HTTPS issuer.

The signed release assets retain immutable API image/manifest binding and exact
priorAPI74559f2849821d7ffd5f5b8f1f9fbd2e4d31a25b allowlisting. Application Marten
schema is unchanged. Identity role conversion is not backward compatible with
the old API's direct-role mutation behavior: API artifact rollback alone is not a
complete system rollback. A literal cloudflared.service maintenance gate is
required before deployment mutations/swap, after verification and across rollback.
The controller never stops or starts public ingress. The operator coordinates
Keycloak database/image restoration and only reopens ingress once a coherent old
or new stack has been verified. Automatic deployments while ingress is active
must fail before changing host deployment state.

## Identity migration

Quiesce ingress and identity/application writes before final snapshots and migration.
Use explicit production setup, never the development-only CLI opt-in flag.
Privileged preflight validates the exact HTTPS issuer,26.8.1version, canonical
realm, complete inventories, safe default roles and no tenant management grants.
Store runtime credentials outside the checkout in a canonical mode0600 file.

Build an exact host-private plan from database employee UUID+KeycloakUserId links,
with email only a consistency check. Duplicate/conflicting ownership, unexpected
privileges and group drift abort. A legacy missing owner may be bound only where
the database's unique existing login UUID proves the association and the plan
explicitly permits it. Preserve credentials/MFA/required actions and default roles;
synchronize the exact recorded active state. Missing logins for inactive records
remain untouched. Unbound tenant-admin owners are explicitly enrolled without
inventing employee IDs. Unmatched accounts are not adopted through email lookup.

Create only the four fixed groups, owner/ticket profile protection and a dedicated
runtime client with no Admin REST/FGAP grants. Add the intended group memberships
before removing only the planned direct application roles. Verify effective role
and enablement, then activate the extension binding last using fresh realm attrs.
No setup emails or password resets are part of migration.

## Cutover and recovery

Prepare and verify all source/build artifacts before maintenance. Preserve exact
old images, Compose/env and private database backups. Stop old Keycloak nodes
before26.8.1schema migration; never start26.1against an upgraded schema. Backups
must restore successfully, and final maintenance snapshots must be fresh.

With ingress closed, upgrade Keycloak, run the reviewed explicit migration,
verify scoped runtime success/denials and private issuer/JWKS transport, then use
the authenticated release controller to swap API. Verify release identity and
synthetic isolated tenant/login/API role behavior; clean only owned canaries.
Reopen ingress only after the new stack is coherent. On failure keep ingress
closed, restore pre-upgrade Keycloak database+image/config and old API/config,
verify consistency, then reopen. Private backup material/plan/credentials stay on
host and never enter source, logs, attachments or published reports.

## Evidence and limits

Prior normal-image verification:40API canaries,139invitation/MFAchecks and
10separate test-image rollback checks passed on PostgreSQL16.15. Final invitation
retry regression passed in native.NET CI before main merge. Backup archive index
and actual restore checks passed on production-host private temporary databases;
these checks did not replace live services. This promotion additionally requires
focused deployment/migration-helper checks and independent review of new tooling.
It is single-node promotion, not a multi-node/external-user-store proof.

## Pinned primary references

- https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/docs/documentation/upgrading/topics/prep_migration.adoc
- https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/docs/documentation/upgrading/topics/migrate_db.adoc
- https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/docs/guides/server/containers.adoc
- https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/docs/guides/server/hostname.adoc
- https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/quarkus/dist/src/main/content/bin/kc.sh

Independent source review resolved the stopped-API sequence conflict: only the
explicit closed-maintenance policy may accept an exact stopped previous API;
its release/image/digest checks remain. Ordinary deployments still require a
running healthy baseline. The coordinator verifies old health before stopping.
