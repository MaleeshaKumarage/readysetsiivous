# Production identity promotion execution

Design and scope: [approved production promotion](../specs/2026-10-28-production-identity-promotion.md).

1. Prepare source/artifacts: review promotion and helper, run focused Linux and
   native helper checks, publish PR and merge only after CI/review. Stage exact
   normal image and immutable API release manifest; automatic deployment must
   fail closed while public ingress is active. No development-only setup bypass.
2. Verify recovery: preserve exact old deployment/image state and prove private
   database archive restoration; stop ingress/API and take fresh final backups.
3. Upgrade identity: stop old Keycloak, start the pinned26.8.1provider against
   existing PostgreSQL, refresh bootstrap maintenance authentication privately,
   generate current exact UUID plan, perform reviewed setup and activation last.
4. Activate API: ensure original stoppedAPI identity matches the release policy,
   use the authenticated release controller with scoped tenant config and
   immutable candidate digest. Keep public ingress stopped throughout.
5. Verify/open or recover: check exact release, private token/role/API behavior,
   clean only owned canaries, open ingress then verify public HTTPS/redirects.
   On failure keep ingress closed and restore old KCdatabase+image+API/config
   coherently; never downgrade the migrated database or expose API-only rollback.

No unrelated database-role/PDF-worker PR activation is included. No production
customer credentials are requested or printed. Evidence contains checks and
artifact identities only; actual plans/secrets/backups remain host-private.
