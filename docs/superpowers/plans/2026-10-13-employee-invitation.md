# Issue49 implementation plan

Design review approved. Implement the user-authorized onboarding fix in this order:

1. Add employee identity provider/lock interfaces and invitation delivery metadata.
2. Add safe Keycloak realm/user/role/action-email operations and native invitation
   provider. Validate explicit URLs/mail/client config; preserve passwords on reuse.
3. Serialize invitation ownership changes, persist binding before email, handle
   retries/cooldown, synchronize bound employee lifecycle and guard old token access.
4. Add account landing/tenant selection and admin invite/reinvite UI.
5. Add application/provider/authorization/browser and isolated PostgreSQL/Keycloak
   regressions. Exercise a captured email's action token in a real browser.
6. Document configuration, migration/recovery/known scope, update issue49 and push
   the feature branch after tests/builds pass. Remove temporary test infrastructure.

Do not email real employees or mutate production. Existing My Work, full employee
profile editing and platform authorization redesign remain separate backlog work.
