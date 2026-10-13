# API audiences and platform tenant management

Issues [63](https://github.com/MaleeshaKumarage/readysetsiivous/issues/63) and
[64](https://github.com/MaleeshaKumarage/readysetsiivous/issues/64). Implemented in
source on `fix/api-audience-platform-63-64`; rollout is pending. User approved
separate audiences and the dedicated platform permission on 2026-10-13.

## Access contract

An access token must pass issuer, signing-key/signature, lifetime and exact
audience validation before roles authorize operations. Tokens for `account`,
the SPA client ID alone, or another API no longer authenticate to protected APIs.
An ID token is for the SPA and must not be used as an API bearer token.

| Identity/token | Required audience | Tenant administration / Me | System tenant lifecycle |
| --- | --- | --- | --- |
| Tenant admin | `cleaning-suite-api` | Existing admin/employee lifecycle rules | 403 |
| Tenant employee | `cleaning-suite-api` | Existing endpoint roles and bound employee rules | 403 |
| Platform issuer + `platform.tenants.manage` | `cleaning-suite-platform-api` | 403, even with tenant roles | Allowed |
| Platform issuer without platform permission | `cleaning-suite-platform-api` | 403 | 403 |
| Tenant issuer with invented platform permission | Tenant API audience | Existing tenant rules | 403 |
| Wrong/missing resource audience, bad signature, invalid issuer/lifetime | Invalid | 401 on protected routes | 401 |
| Anonymous | None | 401 on protected routes | 401 |

Audience selection follows the validated issuer realm. A platform-only audience
from a tenant realm, or a tenant-only audience from the platform realm, is invalid.
Audience arrays may contain additional resources but must contain the exact
required value; these additional values confer no permission. Matching is case
sensitive and does not normalize trailing slashes.

All four system operations (list, provision, suspend, activate) use one policy.
It requires the configured platform issuer realm, platform audience, nonempty
subject and exact `platform.tenants.manage` realm role. Tenant `admin` never
inherits this permission. Platform identities are denied tenant protected APIs
independently of whether their realm happens to be registered as an active tenant.

Realm-role permission claims must have an object containing a string-array
`roles` property. Missing/malformed roles, unrelated roles and case variants do
not authorize platform management. Denied requests never reach tenant handlers.

Structured platform audit events contain the validated subject/issuer, operation,
affected tenant (registry for list), status and outcome. Anonymous/invalid-token
requests are logged as unauthenticated/unvalidated. Provisioning records the
requested tenant on conflicts and handler failures too. Audit events omit tokens,
secrets, owner passwords and request/response bodies.

## Configuration and client issuance

| Key | Default | Purpose |
| --- | --- | --- |
| `Auth:Keycloak:ServerUrl` | Local auth URL in appsettings | Public issuer/browser URL |
| `Auth:Keycloak:AdminServerUrl` | Public URL if omitted | Optional internal Admin/JWKS transport |
| `Auth:Keycloak:Audience` | `cleaning-suite-api` | Tenant resource |
| `Auth:Keycloak:PlatformAudience` | `cleaning-suite-platform-api` | Platform resource |
| `Auth:Keycloak:ProvisionerRealm` | `master` | Trusted platform issuer realm |

The two audience values must be explicitly configured, nonempty and distinct.
Generic `account` and the SPA client ID are rejected as resource configuration.
The old `Audiences` array cannot override these boundaries; remove stale values
from deployed environments. Both tracked compose files now set resource audiences.

New tenant SPA clients use the same configured tenant audience as JWT validation.
The dev realm import uses the default tenant resource. Changing that default
requires updating/import-migrating clients as well. The owned mapper name is
`cleaning-suite-api-audience`, type `oidc-audience-mapper`, with
`included.custom.audience=cleaning-suite-api`, `access.token.claim=true` and
`id.token.claim=false`. Browser login continues using `cleaning-suite-web` and
PKCE S256. Refresh preserves the API audience; tenant tokens do not get the
platform resource.

Use a separate confidential, service-only client such as `cleaning-platform-operator`
for application platform operations. Keep it separate from the backend's outbound
Keycloak Admin REST client/credential. An `admin-cli` bootstrap token is not an
application API bypass. Do not put service secrets or admin credentials in the
frontend. Tenant owners/admins keep their existing tenant roles.

## Existing-client migration and rollout

Import JSON does not migrate existing realms. Before strict API deployment:

1. Inventory named tenant SPA clients and the dedicated platform service identity.
   Save access-controlled backups of the selected client/mapper and role/scope
   representations using your existing Keycloak administration process. Review
   the platform identity independently of tenant admins.
2. Obtain an authorized Keycloak Admin REST token through that process; set it in
   `KEYCLOAK_MIGRATION_ADMIN_TOKEN`. The tool never prints this credential and
   does not accept it on the command line. Set `KEYCLOAK_PLATFORM_CLIENT_SECRET`
   only when explicitly creating a new confidential platform client.
3. Run the tool in check mode (the default), against your explicitly selected
   environment and named realms/clients:

   ```powershell
   python api/deploy/keycloak-api-access.py --server $keycloakUrl --tenant readysetsiivous
   python api/deploy/keycloak-api-access.py --server $keycloakUrl --platform-client cleaning-platform-operator
   ```

   Exit 0 means no differences, 2 means differences or a missing permission, and
   1 means a configuration/transport error. Check mode is read-only. Multiple
   `--tenant` arguments are supported; there is no all-realms sweep.
4. After reviewing the targets/differences, use `--apply`. A platform service-account
   grant additionally requires explicit `--grant-platform`. Without that flag,
   the tool does not grant the permission and returns a nonzero result if it is
   missing. It can define the permission and its client token scope without
   assigning it to an identity. A client newly created in check mode is reported
   as needing follow-up scope/grant checks until it exists.
5. Recheck until zero differences. The tool identifies owned mappers by stable
   names, rejects duplicate/wrong-type name collisions, preserves unrelated
   client settings/mappers/grants and never writes returned client secrets back.
   It configures the named platform client as service-only with full scope disabled;
   only its explicit platform permission scope is added. It never assigns platform
   permission to tenant owners or all master users.
6. Obtain genuine tenant, employee, platform and no-permission tokens. Inspect
   their resource/role claims and test the HTTP permission matrix against the
   intended candidate API. Claim decoding alone is not authentication proof.
7. Deploy strict validation after client issuance is ready. Previously issued
   tokens lacking the API audience must refresh or sign in again. Verify browser
   PKCE, refresh and logout on the actual deployed origins/redirect URIs.

Migration is incremental, not transactional. On interruption, fix the reported
operation and rerun check/apply; successful mapper updates/grants are idempotent.
If rollout fails, preserve the strict validation build, restore only reviewed
client configuration from backups, or stop protected access while correcting
issuance. Never use the audience-disabled API build as a security rollback. A
tenant mapper backup that omits the API audience requires restoring correct token
issuance before access can resume.

Neither production nor the shared dev realm/client configuration was changed by
this implementation. Only disposable test services/realms were used. Deployment
must reconcile existing realm SMTP/identity migration prerequisites from issue49
alongside this audience migration.

## Tests and deep cases

Traceability: AUTH-003/005/007; TEN-001/004/005.

- Signed JWT HTTP tests cover exact/multiple/missing/empty/wrong/case/slash
  audiences, bad and unsigned signatures, altered payloads, foreign/malformed
  issuers, missing expiry, expired and future-not-before tokens. Existing lifetime
  validation retains the framework's clock-skew allowance; expiry tests exceed it.
- All four system routes cover anonymous, tenant admin/employee, unrelated master
  roles, malformed/missing permissions, invented tenant platform permission and
  genuine allowed platform claims. Negative cases assert zero handler calls.
- Platform-to-tenant negatives include both audiences, tenant roles and an active
  master registry entry. Platform permission without a subject is denied.
- Audit tests cover rejected, successful, conflict and handler-failure outcomes,
  affected provisioning tenant and exclusion of tokens/temporary passwords.
- Provisioning tests verify a nondefault configured API audience; a configuration
  contract verifies dev import/runtime defaults and ID-token separation.
- Real Keycloak fixtures verify access/ID token separation, refreshed audience,
  wrong/missing resource issuance, browser PKCE and API access, browser-session
  logout, tenant/employee/platform API negatives, provision/conflict/list/suspend/
  activate and persisted lifecycle readback.
- Browser logout ends that browser session and rejects its refresh token. A
  separately created password-grant session is a different session. Already-issued
  access JWTs retain their lifetime; this change does not promise global immediate
  access-token revocation. Issue49 separately guards bound employee lifecycle.

Regular browser contracts remain mocked and cannot prove these JWT boundaries.
`playwright.auth-live.config.ts` explicitly targets local/tunneled owned services,
checks the test realm/email marker, and disables credential-bearing traces/video.
It never defaults an enabled integration run to production.

Reproduce real tests with isolated `AUDIENCE_TEST_KEYCLOAK_URL`,
`AUDIENCE_TEST_DATABASE` and optional `AUDIENCE_TEST_CONTEXT_PATH` for the .NET
fixture. Run the migration tool for its explicitly named test platform clients
`audience-platform-allowed` (with grant) and `audience-platform-denied` (without).
Then set `NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_KEYCLOAK_URL`,
`AUDIENCE_TEST_CONTEXT_PATH`, `AUDIENCE_TEST_PLATFORM_SECRET` and run:

```powershell
dotnet test api/CleaningSuite.Tests/CleaningSuite.Tests.csproj --filter 'FullyQualifiedName~Auth.'
cd site
npx playwright test --config playwright.auth-live.config.ts
```

The browser config serves frontend port5767; the fixture must allow that origin
and the API must allow it through CORS. Fixture secrets and generated context
files are test-only and must be kept outside Git and deleted after the run. Remove
only the owned containers/volumes/network and tunnels, never reset shared data.

## Verification recorded on 2026-10-13

- Final full backend run: **240 passed, 0 failed, 0 skipped**, including isolated
  PostgreSQL, Keycloak26.1 and captured Mailpit. Final focused authentication run:
  **46 passed, 0 failed, 0 skipped**, including audit-target/final-status assertions
  and the dev import consistency contract.
- Full regular Playwright: **66 passed** across desktop/mobile Chromium. Windows
  reserved port3000, so a temporary config used loopback5759 with the same tests,
  projects and assertions; no test scope was reduced.
- Real Playwright: **2 passed on Keycloak24.0 and 2 on26.1**. Real issuance/refresh
  .NET test also passed independently on24.0.
- Migration: platform-client apply followed by a zero-difference check on both
  versions; deliberate outdated tenant mapper check→apply→zero differences on
  26.1, followed by genuine issuance of the corrected audience.
- Type checking and the production frontend build passed (33 generated routes).
  Backend compilation passed as part of the final test build. Existing NuGet
  vulnerability-feed connectivity and PDF platform-analyzer warnings remain;
  neither is an authentication test failure.

Reference: [Keycloak audience support](https://www.keycloak.org/docs/26.2.5/server_admin/index.html#audience-support).
