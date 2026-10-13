# Issues 63/64 implementation plan

Approved spec: [audiences and platform design](../specs/2026-10-13-api-audience-platform-design.md).
The writing-plans skill is not installed; this plan records the implementation
sequence directly. No production/shared-realm configuration changes are included.

1. Add validated audience configuration and realm-aware strict JWT validation.
   Make the JWKS HTTP client injectable for genuine signed-token HTTP tests.
2. Add a platform authorization policy with explicit issuer/audience/permission;
   deny platform-realm identities on tenant protected routes independently of
   registration or claimed tenant roles. Audit system outcomes around authentication,
   authorization and controller execution using validated actors only.
3. Add the configured tenant audience mapper to new SPA clients and the import;
   replace permissive audience lists in runtime/compose/env examples.
4. Provide a scoped check/apply migration tool for existing SPA clients and a
   dedicated platform service client. Default to check; preserve unrelated client
   settings/mappers/role grants. Require explicit role grant and secret via the
   operator, never store them in the repository.
5. Add unit/config/provisioner and signed-JWT HTTP tests for audience, issuer,
   lifetime/signature, all platform routes and tenant/platform isolation. Assert
   denied calls do not reach handlers and audit output omits credentials.
6. Use disposable PostgreSQL/Keycloak24 and26.1 services to verify client issuance
   and real PKCE/refresh/logout with Playwright, including genuine platform/tenant
   permission negatives. Reuse owned fixture IDs only; clean up test services.
7. Run the backend and browser regressions, type check/build; document exact
   outcomes and remaining rollout prerequisites. Push and create a focused stacked
   draft PR, attach it and update issues63/64 with evidence.

Configuration: `Auth:Keycloak:Audience` (tenant resource),
`Auth:Keycloak:PlatformAudience` (platform resource),
`Auth:Keycloak:ProvisionerRealm` (trusted platform issuer realm). Permission name
`platform.tenants.manage` is an application contract, not a configurable bypass.
Legacy `Audiences` lists are removed from tracked configuration and cannot override
these exact resource boundaries.
